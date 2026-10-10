# Build context is the repo root (WellSpent-backend/), not dotnet/ — the
# migrator needs internal/db/migrations, which lives outside dotnet/ and is
# the schema source of truth for both the Go and C# backends.
#
#   docker build -f dotnet/Dockerfile -t wellspent-backend-csharp .
#
# Mirrors the Go Dockerfile's shape: one builder stage, one slim runtime
# image holding every binary this backend needs (here: the API host and the
# migration runner — more will join as background jobs are ported in later
# sub-issues, same as Go's server/cycle-budgets/plaid-sync).

# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /src

# Restore first, with only the project files, so a source-only change doesn't
# invalidate the NuGet restore layer.
COPY dotnet/WellSpent.slnx dotnet/
COPY dotnet/src/WellSpent.Api/WellSpent.Api.csproj dotnet/src/WellSpent.Api/
COPY dotnet/src/WellSpent.Domain/WellSpent.Domain.csproj dotnet/src/WellSpent.Domain/
COPY dotnet/src/WellSpent.Application/WellSpent.Application.csproj dotnet/src/WellSpent.Application/
COPY dotnet/src/WellSpent.Infrastructure/WellSpent.Infrastructure.csproj dotnet/src/WellSpent.Infrastructure/
COPY dotnet/src/WellSpent.Migrator/WellSpent.Migrator.csproj dotnet/src/WellSpent.Migrator/
COPY dotnet/src/WellSpent.Jobs.PlaidSync/WellSpent.Jobs.PlaidSync.csproj dotnet/src/WellSpent.Jobs.PlaidSync/
COPY dotnet/src/WellSpent.Jobs.CycleBudgets/WellSpent.Jobs.CycleBudgets.csproj dotnet/src/WellSpent.Jobs.CycleBudgets/
COPY dotnet/tests/WellSpent.Api.Tests/WellSpent.Api.Tests.csproj dotnet/tests/WellSpent.Api.Tests/
COPY dotnet/tests/WellSpent.Application.Tests/WellSpent.Application.Tests.csproj dotnet/tests/WellSpent.Application.Tests/
COPY dotnet/tests/WellSpent.Infrastructure.Tests/WellSpent.Infrastructure.Tests.csproj dotnet/tests/WellSpent.Infrastructure.Tests/
RUN dotnet restore dotnet/WellSpent.slnx

COPY dotnet/ dotnet/
RUN dotnet publish dotnet/src/WellSpent.Api/WellSpent.Api.csproj -c Release -o /app/api --no-restore
RUN dotnet publish dotnet/src/WellSpent.Migrator/WellSpent.Migrator.csproj -c Release -o /app/migrator --no-restore
RUN dotnet publish dotnet/src/WellSpent.Jobs.PlaidSync/WellSpent.Jobs.PlaidSync.csproj -c Release -o /app/plaid-sync --no-restore
RUN dotnet publish dotnet/src/WellSpent.Jobs.CycleBudgets/WellSpent.Jobs.CycleBudgets.csproj -c Release -o /app/cycle-budgets --no-restore

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
# files) by walking up from its own binary to find go.mod — the same lookup
# AppConfig.Load uses for .env.<env> in local dev. Both need to exist here in
# the same relative shape as the repo so that lookup resolves identically in
# every environment.
COPY go.mod ./go.mod
COPY internal/db/migrations ./internal/db/migrations

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Cloud Run overrides this per Job (--command=./migrator, ./plaid-sync), same as Go's three binaries.
ENTRYPOINT ["dotnet", "api/WellSpent.Api.dll"]
