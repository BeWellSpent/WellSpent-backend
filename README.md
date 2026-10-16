# WellSpent backend — C# rewrite (in progress)

This is the .NET 10 rewrite of the WellSpent backend, tracked by
[GitHub issue #78](https://github.com/BeWellSpent/WellSpent/issues/78) and the living
roadmap at `docs/features/refactor-backend-csharp.md` in the workspace root repo.

**This is not the live backend.** It lives on the long-lived `rewrite/csharp` branch so
the Go backend above this directory keeps shipping normal fixes/features on its usual
`develop`→`main` flow throughout the migration. Nothing here is deployed to Cloud Run yet.

## Projects

| Project | Purpose |
|---|---|
| `src/WellSpent.Api` | ASP.NET Core host — DI, JWT auth, global error mapping, Serilog, health endpoints |
| `src/WellSpent.Infrastructure` | EF Core `DbContext` + Npgsql config, hand-mapped against the *existing* schema |
| `src/WellSpent.Migrator` | DbUp console app that replays the existing goose SQL files (`../internal/db/migrations/`) — proven only against a fresh database so far; see the caveat below |
| `tests/WellSpent.Api.Tests` | xUnit smoke tests for the scaffold |

No business domain is ported yet — see the sub-issues under #78 (B2 onward).

## Local dev

```bash
cd dotnet
dotnet build
dotnet test
```

Running the API locally needs the same env vars the Go backend uses — `DATABASE_URL`
and `JWT_SECRET` — read from `../.env.<ENV>` (default `dev`) the same way
`internal/config/config.go` reads them, so no second secrets file is needed:

```bash
cd src/WellSpent.Api
dotnet run
```

`DATABASE_URL` must be the same `postgresql://...` URI format already used everywhere
in this repo — `WellSpent.Infrastructure.PostgresConnectionString` converts it to the
Npgsql keyword=value format Npgsql itself requires, since Npgsql (unlike Go's
`pgxpool.ParseConfig`) doesn't parse the URI form directly.

## Migration runner

`WellSpent.Migrator` tracks applied migrations in goose's own `goose_db_version` table
(`GooseCompatibleJournal`, schema and version-parsing mirrored from goose v3's Postgres
dialect) instead of DbUp's default `SchemaVersions` table. This is what lets this runner
and the Go backend's `cmd/migrate` safely interleave against the *same* database during
the transition — whichever one runs first records what it applied in the one table the
other already reads, so neither re-applies nor skips a migration the other already
handled. Verified against all three shapes: a fresh database, a database `cmd/migrate`
(goose) had already fully migrated, and a mixed sequence where each runner applies one
new migration and the other correctly recognizes it.

## Docker

Build from the **repo root**, not this directory — the image also needs
`internal/db/migrations/`, which lives outside `dotnet/`:

```bash
cd ..   # WellSpent-backend/
docker build -f dotnet/Dockerfile -t wellspent-backend-csharp .
```

The image bundles both the API and the migrator (mirrors the Go image bundling
`server`/`cycle-budgets`/`plaid-sync`). The API runs by default; to run the migrator
instead, override the entrypoint:

```bash
docker run --rm --entrypoint dotnet \
  -e DATABASE_URL=... -e ENV=... \
  wellspent-backend-csharp migrator/WellSpent.Migrator.dll up <env>
```
