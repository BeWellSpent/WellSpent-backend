using Microsoft.Extensions.DependencyInjection;
using Resend;
using WellSpent.Application.Abstractions;
using WellSpent.Domain.Abstractions;
using WellSpent.Infrastructure.Configuration;
using WellSpent.Infrastructure.ExternalServices;
using WellSpent.Infrastructure.Persistence;
using WellSpent.Infrastructure.Security;

namespace WellSpent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, AppConfig config)
    {
        services.AddSingleton(config);
        services.AddWellSpentDbContext(config.DatabaseUrl, config.ApplicationName);

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IStatusBannerRepository, StatusBannerRepository>();
        services.AddScoped<IChangelogRepository, ChangelogRepository>();

        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ICryptoService, AesCryptoService>();

        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>();
        services.AddHttpClient<IAppleAuthClient, AppleAuthClient>();
        services.AddHttpClient<ICaptchaVerifier, TurnstileCaptchaVerifier>();

        if (string.IsNullOrEmpty(config.ResendApiKey))
        {
            // Mirrors Go: no RESEND_API_KEY means every send logs a warning
            // and no-ops, rather than failing the caller.
            services.AddSingleton<IEmailSender, NoOpEmailSender>();
        }
        else
        {
            services.AddResend(o => o.ApiToken = config.ResendApiKey);
            services.AddScoped<IEmailSender, ResendEmailSender>();
        }

        return services;
    }
}
