using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
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
    string DeviceId) : IRequest<RegisterResult>;

public sealed record RegisterResult(string AccessToken, long ExpiresIn, string RefreshToken);

public sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ICaptchaVerifier captcha,
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

        // Cheap local checks first, then the external network call, then the
        // DB round-trip below. "Couldn't verify" and "verification failed"
        // are deliberately treated the same — Register has no use for the
        // distinction, and failing open on a captcha outage would defeat the
        // point of having one.
        if (options.Value.CaptchaEnforcementEnabled)
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
        logger.LogInformation("auth.register.success user_id={UserId} device_id={DeviceId}", user.Id, request.DeviceId);

        return new RegisterResult(accessToken, (long)AuthConstants.AccessTokenLifetime.TotalSeconds, rawRefreshToken);
    }
}
