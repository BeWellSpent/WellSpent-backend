using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.AppAttest;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Auth.Register;

public sealed record RegisterCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string CountryCode,
    string StateCode,
    string Language,
    string Currency,
    string CaptchaToken,
    string DeviceId,
    string ClientIp = "",
    AppAttestProof? AppAttest = null,
    string? AppAttestUnavailable = null) : IRequest<RegisterResult>;

public sealed record RegisterResult(string AccessToken, long ExpiresIn, string RefreshToken);

public sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ICaptchaVerifier captcha,
    AppAttestGate appAttest,
    IAuthRateLimiter rateLimiter,
    IJwtService jwt,
    IRefreshTokenRepository refreshTokens,
    VerificationMailer mailer,
    IOptions<AuthOptions> options,
    ILogger<RegisterCommandHandler> logger) : IRequestHandler<RegisterCommand, RegisterResult>
{
    public async Task<RegisterResult> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = EmailValidator.NormalizeAndValidate(request.Email);
        PasswordValidator.Validate(request.Password);

        var outcome = await appAttest.CheckAsync("register", email, request.AppAttest, request.AppAttestUnavailable, ct);
        AuthRateLimits.Apply(rateLimiter, "register", request.ClientIp, outcome == AppAttestOutcome.Attested || !appAttest.EnforcementEnabled);
        if (outcome == AppAttestOutcome.Invalid && appAttest.EnforcementEnabled)
        {
            throw new AppValidationException("couldn't verify this device");
        }

        // A device App Attest vouches for (or one that can't attest) skips the captcha; anything else needs it.
        var deviceVouched = outcome is AppAttestOutcome.Attested or AppAttestOutcome.Unavailable;
        if (!deviceVouched && !options.Value.CaptchaEnforcementEnabled && appAttest.EnforcementEnabled)
        {
            throw new AppValidationException("couldn't verify this device");
        }

        // "Couldn't verify" and "verification failed" are treated the same: failing open on a captcha outage would defeat it.
        if (!deviceVouched && options.Value.CaptchaEnforcementEnabled)
        {
            bool verified;
            try
            {
                verified = await captcha.VerifyAsync(request.CaptchaToken, "", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "auth.register.captcha_verify_error");
                verified = false;
            }
            if (!verified)
            {
                throw new AppValidationException("captcha verification failed");
            }
        }

        try
        {
            await users.GetByEmailAsync(email, ct);
            throw new DuplicateException("user", "email", email);
        }
        catch (NotFoundException)
        {
            // expected — fall through to create
        }

        var hashed = passwordHasher.Hash(request.Password);
        var lang = string.IsNullOrEmpty(request.Language) ? "en" : request.Language;
        var currency = string.IsNullOrEmpty(request.Currency) ? "USD" : request.Currency;

        var user = await users.CreateAsync(new User
        {
            Email = email,
            HashedPassword = hashed,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CountryCode = string.IsNullOrEmpty(request.CountryCode) ? null : request.CountryCode,
            StateCode = string.IsNullOrEmpty(request.StateCode) ? null : request.StateCode,
            Language = lang,
            Currency = currency,
            Attested = outcome == AppAttestOutcome.Attested,
        }, ct);

        // Best-effort — the account is already created; a failed send just
        // means the user falls back to "resend verification email" later.
        try
        {
            await mailer.SendAsync(user, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "auth.verification_email.failed to={To}", user.Email);
        }

        var accessToken = jwt.GenerateToken(user.Id, AuthConstants.AccessTokenLifetime);
        var rawRefreshToken = RefreshTokenGenerator.GenerateRaw();
        await refreshTokens.IssueAsync(user.Id, request.DeviceId, RefreshTokenGenerator.Hash(rawRefreshToken),
            (int)AuthConstants.DefaultTokenLifetime.TotalSeconds, DateTime.UtcNow.Add(AuthConstants.DefaultTokenLifetime), ct);
        logger.LogInformation("auth.register.success user_id={UserId} device_id={DeviceId} app_attest={Outcome}", user.Id, request.DeviceId, outcome);

        return new RegisterResult(accessToken, (long)AuthConstants.AccessTokenLifetime.TotalSeconds, rawRefreshToken);
    }
}
