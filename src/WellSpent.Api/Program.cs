using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using WellSpent.Api.Endpoints;
using WellSpent.Api.Errors;
using WellSpent.Application;
using WellSpent.Application.AppAttest;
using WellSpent.Application.Configuration;
using WellSpent.Infrastructure;
using WellSpent.Infrastructure.Configuration;

var config = AppConfig.Load();

Log.Logger = new LoggerConfiguration()
    // EF Core logs every executed SQL command (with parameters) at
    // Information by default - useful in dev, too noisy/sensitive for prod.
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", config.Env == "prod" ? LogEventLevel.Warning : LogEventLevel.Information)
    .Enrich.WithProperty("application", config.ApplicationName)
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Services.AddInfrastructure(config);
    builder.Services.AddApplication();

    // The slice of AppConfig the Application layer needs — see
    // AuthOptions's doc comment for why this indirection exists.
    builder.Services.Configure<AuthOptions>(o =>
    {
        o.FrontendUrl = config.FrontendUrl;
        o.ResendFromEmail = config.ResendFromEmail;
        o.CaptchaEnforcementEnabled = config.CaptchaEnforcementEnabled;
        o.EncryptionKey = config.EncryptionKey;
    });
    builder.Services.Configure<AppAttestOptions>(o =>
    {
        o.EnforcementEnabled = config.AppAttestEnforcementEnabled;
        o.AppId = config.AppAttestAppId;
        o.AllowDevelopment = config.AppAttestAllowDevelopment;
    });

    // Cloud Run's front end appends the caller's address to X-Forwarded-For; trust only that last hop.
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        o.ForwardLimit = 1;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });

    // Restores the Go backend's per-IP token bucket. Health probes and Plaid webhooks are exempt.
    builder.Services.AddRateLimiter(o =>
    {
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            var path = http.Request.Path;
            if (path.StartsWithSegments("/health") || path.StartsWithSegments("/webhooks"))
            {
                return RateLimitPartition.GetNoLimiter("exempt");
            }
            return RateLimitPartition.GetTokenBucketLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = config.RateLimitBurst,
                TokensPerPeriod = config.RateLimitRps,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 0,
            });
        });
        o.OnRejected = async (ctx, ct) =>
        {
            Log.Warning("http.rate_limited ip={Ip} path={Path}", ctx.HttpContext.Connection.RemoteIpAddress, ctx.HttpContext.Request.Path);
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.HttpContext.Response.Headers.RetryAfter = "1";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new ErrorBody("resource_exhausted", "too many requests"), ct);
        };
    });

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Keeps inbound claim types exactly as the Go backend's tokens
            // carry them ("sub", not a remapped URI claim type) — both
            // backends must read the same JWT the same way while both are
            // live during the strangler-fig cutover.
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Same secret, same HS256 algorithm as internal/auth/jwt.go —
                // a token either backend issues must validate on both.
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.JwtSecret)),
            };
        });
    builder.Services.AddAuthorization();

    // Mirrors the Go backend's CORS policy exactly, so a browser cutting a domain over mid-transition sees the same cross-origin behavior either way.
    const string corsPolicy = "default";
    builder.Services.AddCors(options => options.AddPolicy(corsPolicy, policy => policy
        .WithOrigins(config.CorsAllowedOrigins)
        .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
        .WithHeaders("Authorization", "Content-Type", "If-None-Match")
        .WithExposedHeaders("ETag")
        .AllowCredentials()));

    var app = builder.Build();

    app.UseForwardedHeaders();
    app.UseMiddleware<ErrorHandlingMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors(corsPolicy);
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // Liveness only — no dependencies. Cloud Run's own health probe target.
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    // Proves the Npgsql pool + EF Core wiring actually reach the real schema.
    app.MapGet("/health/db", async (WellSpentDbContext db) =>
    {
        var canConnect = await db.Database.CanConnectAsync();
        return canConnect
            ? Results.Ok(new { status = "ok" })
            : Results.Problem("Database unreachable", statusCode: StatusCodes.Status503ServiceUnavailable);
    });

    app.MapAuthEndpoints();
    app.MapAppAttestEndpoints();
    app.MapUserEndpoints();
    app.MapCountryEndpoints();
    app.MapStatusEndpoints();
    app.MapChangelogEndpoints();
    app.MapNotificationEndpoints();
    app.MapInviteEndpoints();
    app.MapBudgetEndpoints();
    app.MapCategoryEndpoints();
    app.MapPaymentMethodEndpoints();
    app.MapTransactionEndpoints();
    app.MapPlaidEndpoints();
    app.MapWebhookEndpoints();

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

// Top-level statements generate an internal Program class by default; making
// it public/partial here is what lets WebApplicationFactory<Program> in the
// test project construct this app in-process.
public partial class Program;
