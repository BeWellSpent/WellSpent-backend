using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using WellSpent.Api.Configuration;
using WellSpent.Api.Errors;
using WellSpent.Infrastructure;

var config = AppConfig.Load();

Log.Logger = new LoggerConfiguration()
    .Enrich.WithProperty("application", config.ApplicationName)
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Services.AddWellSpentInfrastructure(config.DatabaseUrl, config.ApplicationName);

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

    // Proves the Npgsql pool + EF Core wiring actually reach the real schema,
    // without mapping any table yet.
    app.MapGet("/health/db", async (WellSpentDbContext db) =>
    {
        var canConnect = await db.Database.CanConnectAsync();
        return canConnect
            ? Results.Ok(new { status = "ok" })
            : Results.Problem("Database unreachable", statusCode: StatusCodes.Status503ServiceUnavailable);
    });

    // Proves the JWT middleware round-trips a token issued the same way the
    // Go backend issues one. Removed once a real authenticated endpoint
    // exists (B2).
    app.MapGet("/debug/whoami", (HttpContext ctx) =>
    {
        var sub = ctx.User.FindFirst("sub")?.Value;
        return Results.Ok(new { sub });
    }).RequireAuthorization();

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
