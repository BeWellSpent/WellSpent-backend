using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using WellSpent.Api.Endpoints;
using WellSpent.Api.Errors;
using WellSpent.Application;
using WellSpent.Application.Configuration;
using WellSpent.Infrastructure;
using WellSpent.Infrastructure.Configuration;

var config = AppConfig.Load();

Log.Logger = new LoggerConfiguration()
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

    var app = builder.Build();

    app.UseMiddleware<ErrorHandlingMiddleware>();
    app.UseSerilogRequestLogging();
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
    app.MapUserEndpoints();
    app.MapStatusEndpoints();
    app.MapChangelogEndpoints();
    app.MapNotificationEndpoints();
    app.MapInviteEndpoints();

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
