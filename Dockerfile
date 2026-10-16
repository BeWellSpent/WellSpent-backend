# Build context is the repo root (WellSpent-backend/) — the migrator needs
# db/migrations, which lives outside src/ and is the schema source of truth.
#
#   docker build -t wellspent-backend-csharp .
#
# One builder stage, one slim runtime image holding every binary this
# backend needs: the API host, the migration runner, and the two background
# jobs (cycle-budgets, plaid-sync).

# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /src

# Restore first, with only the project files, so a source-only change doesn't
# invalidate the NuGet restore layer.
COPY WellSpent.slnx ./
COPY src/WellSpent.Api/WellSpent.Api.csproj src/WellSpent.Api/
COPY src/WellSpent.Domain/WellSpent.Domain.csproj src/WellSpent.Domain/
COPY src/WellSpent.Application/WellSpent.Application.csproj src/WellSpent.Application/
COPY src/WellSpent.Infrastructure/WellSpent.Infrastructure.csproj src/WellSpent.Infrastructure/
COPY src/WellSpent.Migrator/WellSpent.Migrator.csproj src/WellSpent.Migrator/
COPY src/WellSpent.Jobs.PlaidSync/WellSpent.Jobs.PlaidSync.csproj src/WellSpent.Jobs.PlaidSync/
COPY src/WellSpent.Jobs.CycleBudgets/WellSpent.Jobs.CycleBudgets.csproj src/WellSpent.Jobs.CycleBudgets/
COPY tests/WellSpent.Api.Tests/WellSpent.Api.Tests.csproj tests/WellSpent.Api.Tests/
COPY tests/WellSpent.Application.Tests/WellSpent.Application.Tests.csproj tests/WellSpent.Application.Tests/
COPY tests/WellSpent.Infrastructure.Tests/WellSpent.Infrastructure.Tests.csproj tests/WellSpent.Infrastructure.Tests/
RUN dotnet restore WellSpent.slnx

COPY src/ src/
RUN dotnet publish src/WellSpent.Api/WellSpent.Api.csproj -c Release -o /app/api --no-restore
RUN dotnet publish src/WellSpent.Migrator/WellSpent.Migrator.csproj -c Release -o /app/migrator --no-restore
RUN dotnet publish src/WellSpent.Jobs.PlaidSync/WellSpent.Jobs.PlaidSync.csproj -c Release -o /app/plaid-sync --no-restore
RUN dotnet publish src/WellSpent.Jobs.CycleBudgets/WellSpent.Jobs.CycleBudgets.csproj -c Release -o /app/cycle-budgets --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql probes for GSSAPI (Kerberos) support on every connection attempt;
# the base image doesn't ship it, which otherwise logs a benign but noisy
# "Cannot load library libgssapi_krb5.so.2" warning on every connect.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/api ./api
COPY --from=builder /app/migrator ./migrator
COPY --from=builder /app/plaid-sync ./plaid-sync
COPY --from=builder /app/cycle-budgets ./cycle-budgets

# The migrator locates its schema source of truth (the existing goose SQL
# files) by walking up from its own binary to find WellSpent.slnx — the same
# lookup AppConfig.Load uses for .env.<env> in local dev. Both need to exist
# here in the same relative shape as the repo so that lookup resolves
# identically in every environment.
COPY WellSpent.slnx ./WellSpent.slnx
COPY db/migrations ./db/migrations

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Cloud Run overrides this per Job (--command=./migrator, ./plaid-sync).
ENTRYPOINT ["dotnet", "api/WellSpent.Api.dll"]
