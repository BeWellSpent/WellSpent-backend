using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    /// <summary>
    /// applicationName overrides config.ApplicationName's "wellspent-api-{env}"
    /// default — standalone jobs (e.g. plaid-sync) send their own distinct
    /// Postgres application_name, matching Go's db.NewPool call sites, so a
    /// connection is identifiable in pg_stat_activity by which process opened it.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, AppConfig config, string? applicationName = null)
    {
        services.AddSingleton(config);
        services.AddWellSpentDbContext(config.DatabaseUrl, applicationName ?? config.ApplicationName);

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IStatusBannerRepository, StatusBannerRepository>();
        services.AddScoped<IChangelogRepository, ChangelogRepository>();
        services.AddScoped<IBudgetProfileRepository, BudgetProfileRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IInviteRepository, InviteRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IFixedExpenseRepository, FixedExpenseRepository>();
        services.AddScoped<IExpenseAllocationRepository, ExpenseAllocationRepository>();
        services.AddScoped<ITransactionReviewRepository, TransactionReviewRepository>();
        services.AddScoped<IPlaidItemRepository, PlaidItemRepository>();

        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ICryptoService, AesCryptoService>();

        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>();
        services.AddHttpClient<IAppleAuthClient, AppleAuthClient>();
        services.AddHttpClient<ICaptchaVerifier, TurnstileCaptchaVerifier>();

        // Named, not typed: Going.Plaid's PlaidClient dispatches through
        // IHttpClientFactory.CreateClient("PlaidClient") internally, so the
        // retry/redaction handler has to be registered under that exact name.
        services.AddTransient<PlaidLoggingRetryHandler>();
        services.AddHttpClient("PlaidClient").AddHttpMessageHandler<PlaidLoggingRetryHandler>();
        services.AddSingleton(sp =>
        {
            var env = config.PlaidEnv == "production" ? Going.Plaid.Environment.Production : Going.Plaid.Environment.Sandbox;
            return new Going.Plaid.PlaidClient(
                env,
                config.PlaidClientId,
                config.PlaidSecret,
                accessToken: null,
                httpClientFactory: sp.GetRequiredService<IHttpClientFactory>(),
                logger: sp.GetRequiredService<ILogger<Going.Plaid.PlaidClient>>());
        });
        services.AddSingleton<IPlaidClient, GoingPlaidClient>();

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
