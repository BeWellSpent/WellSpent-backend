# WellSpent backend

C#/.NET 10 REST + JWT API server. The Go + ConnectRPC backend this replaced
(issue #78, `docs/features/refactor-backend-csharp.md` in the workspace root)
was fully decommissioned 2026-10-16 after every client cut over — this is the
live production backend, not a parallel rewrite. Deployed on Google Cloud Run
today; an Azure Container Apps migration is in progress, see
`infra/terraform/README.md`.

## Projects

| Project | Purpose |
|---|---|
| `src/WellSpent.Api` | ASP.NET Core host — minimal-API endpoints, DI, JWT auth, global error mapping |
| `src/WellSpent.Application` | CQRS commands/queries + handlers, business logic (NSubstitute-mocked tests, no DB) |
| `src/WellSpent.Domain` | Entities, repository interfaces, exceptions |
| `src/WellSpent.Infrastructure` | EF Core `DbContext` + Npgsql, Fluent API mapping against the existing schema |
| `src/WellSpent.Migrator` | DbUp console app replaying `db/migrations/*.sql` forward-only |
| `src/WellSpent.Jobs.CycleBudgets`, `src/WellSpent.Jobs.PlaidSync` | Background jobs, deployed as separate Cloud Run Jobs |
| `tests/` | xUnit — `WellSpent.Application.Tests`, `WellSpent.Infrastructure.Tests`, `WellSpent.Api.Tests` |

See `CLAUDE.md` for the full command reference, architecture, and conventions.

## Local dev

```bash
make secrets-decrypt ENV=dev   # once; .env.dev is gitignored
make run                       # dotnet run, API on http://localhost:8080
make test
make build
```

Local dev runs against a standalone Postgres in Docker, not Neon — see
`CLAUDE.md`'s "Local dev database" section for setup. Production uses Neon
(serverless Postgres), unaffected by the in-progress Azure migration below.

## Docker

```bash
docker build -t wellspent-backend .
docker run --rm -p 8080:8080 --env-file .env.dev wellspent-backend
```

The image bundles the API, migrator, and both jobs. Runs the API by default;
override the entrypoint to run the migrator or a job instead:

```bash
docker run --rm --entrypoint dotnet wellspent-backend migrator/WellSpent.Migrator.dll up <env>
```

## Infrastructure

`infra/terraform/` — Azure Container Apps, Key Vault, and Container Registry,
replacing the GCP Cloud Run + Secret Manager setup. See that directory's own
`README.md` for setup and current migration status.
