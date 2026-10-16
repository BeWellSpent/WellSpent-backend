# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This is the WellSpent backend — ASP.NET Core / .NET 10, REST + JWT. The Go/ConnectRPC
backend that used to live here was fully decommissioned (all clients had cut over to
this one) and deleted; `dotnet/` was promoted to the repo root in the same change.

## Commands

```bash
make run                      # start the API host (dotnet run), ENV=dev by default
make test                     # dotnet test WellSpent.slnx
make build                    # dotnet build WellSpent.slnx -c Release
make cycle                    # run the cycle-budgets job locally
make migrate ENV=dev          # apply pending migrations to the local dev DB
make secrets-decrypt ENV=dev  # decrypt .env.dev.enc → .env.dev (gitignored)
make secrets-encrypt ENV=dev  # encrypt .env.dev → .env.dev.enc (commit this)
```

Run a single test:
```bash
dotnet test WellSpent.slnx --filter "FullyQualifiedName~Register_Success"
```

There is no `make generate` — nothing here is generated from a contract. The REST
endpoints are hand-written; the OpenAPI contract in `WellSpent-proto/openapi/v1/` is
what *clients* generate from, not this repo.

## Local dev database

Dev runs on a standalone local Postgres in Docker (`docker-compose.db.yml`), not Neon — the
Neon dev branch was removed 2026-09-04 after it turned out to be accumulating CU from a stray
polling bug (see `docs/features/configurable-alerts.md`'s 2026-08-29 change log entry) and,
separately, being usable across multiple developer machines was never really its purpose. **Prod
is still Neon** — only dev moved.

It's deliberately its own compose file, decoupled from `../docker-compose.dev.yml` below:
started once on whichever machine you designate as the DB host, left running (`restart:
unless-stopped`), and reachable from every other machine on the LAN — instead of each
developer's machine spinning up its own throwaway instance.

```bash
# On the host machine, once:
cp .env.db.example .env.db          # fill in a real POSTGRES_PASSWORD
docker compose -f docker-compose.db.yml --env-file .env.db up -d
```

Every machine's own `.env.dev` (including the host's) points `DATABASE_URL` at the host's LAN
address — never `localhost`, since the backend running inside `docker-compose.dev.yml` would
resolve that to its own container, not the DB host:

```
DATABASE_URL=postgresql://wellspent:<password>@<host-lan-address>:5432/wellspent_dev?sslmode=disable
```

`<host-lan-address>` needs to actually resolve from other machines — a DHCP reservation (static
LAN IP) for the host is the reliable option; an mDNS `.local` hostname works between Macs
out of the box but needs a Bonjour/mDNS responder installed on Windows to be reachable *from* a
Mac. The host's firewall needs an inbound allow rule for TCP 5432, or nothing outside that
machine can reach it regardless of the address used.

## Local dev via Docker

```bash
make secrets-decrypt ENV=dev                              # once; .env.dev is gitignored
docker build -t wellspent-backend .
docker run --rm -p 8080:8080 --env-file .env.dev wellspent-backend
```

`db/migrations` are applied by the separate `migrator` binary baked into the same image
(`docker run --rm --entrypoint dotnet wellspent-backend migrator/WellSpent.Migrator.dll up dev`),
not automatically at API container start — run it once after any new migration lands, same
as the production deploy pipeline does.

## REST contract

Everything is plain REST + JWT — there is no second transport to reconcile. The contract
lives in `WellSpent-proto/openapi/v1/`, hand-authored and PR-reviewed there like any other
API change; this repo implements it, it doesn't generate from it.

- `src/WellSpent.Api/Endpoints/` — one file per domain, minimal-API route registration
- `src/WellSpent.Api/Errors/ErrorHandlingMiddleware.cs` — maps application exceptions to
  the same lowercase error codes (`invalid_argument`/`forbidden`/`already_exists`/
  `not_found`/`internal`) every client already expects
- `src/WellSpent.Api/Auth/CurrentUser.cs` — the authenticated user's ID, read from the JWT
- Auth is JWT-only; RPCs that skip it (register/login/etc.) are the same set the old Go
  bypass list had — see `src/WellSpent.Api/Program.cs`

## Architecture

Four layers, in dependency order for new features:

1. **Contract** — add/update the endpoint in `WellSpent-proto/openapi/v1/`
2. **Domain + Infrastructure** — entity in `src/WellSpent.Domain/Entities/`, repository
   interface in `src/WellSpent.Domain/Abstractions/`, EF Core implementation + Fluent API
   mapping in `src/WellSpent.Infrastructure/Persistence/`
3. **Application** — CQRS command/query + handler in `src/WellSpent.Application/<Domain>/`,
   business logic + unit tests (mock repo via NSubstitute, no DB)
4. **Api** — minimal-API endpoint in `src/WellSpent.Api/Endpoints/`, wired to MediatR

Service wiring: `src/WellSpent.Infrastructure/DependencyInjection.cs` and
`src/WellSpent.Application/DependencyInjection.cs` register everything; `Program.cs` calls both.

## Key patterns

**CQRS/MediatR:** every write is a `Command`, every read a `Query`, each with its own
`Handler` class in the same folder. No generic repository-of-everything — one repository
interface per aggregate, in `WellSpent.Domain.Abstractions`.

**Errors:** throw `NotFoundException`/`ForbiddenException`/`AlreadyExistsException`/
`AppValidationException` from a handler; `ErrorHandlingMiddleware` maps them to the matching
REST error code. Never catch-and-swallow — an unhandled exception type falls through to a
500, which is the signal something new needs a mapping.

**Money:** `{units, nanos, currency}` on the wire, `NUMERIC(15,4)` in Postgres — same shape
the Go backend used, so no client-side formatting changed. Conversion lives in each DTO's
mapping code (`*Dto.cs` / `*Mapping*.cs` per domain), not one shared converter.

**Tests:** NSubstitute for mocked repositories (`Substitute.For<ISomeRepository>()`), xUnit.
**NSubstitute returns `null`, not an empty collection, for an unconfigured `Task<List<T>>`
method** — code reading from a repo result should defensively `?? []` rather than assume a
real-looking empty list in tests that didn't explicitly configure one.

## Generated files — do not edit

None. Nothing in this repo is code-generated. `obj/`/`bin/` are build output (gitignored).

## Database

- **Dev**: a standalone local Postgres in Docker, shared across your LAN — see "Local dev
  database" above. **Prod**: Neon serverless PostgreSQL.
- Prod only: use the **direct** connection URL (no `-pooler` hostname) — Npgsql's behavior
  under PgBouncer's transaction pooling mode is the same class of incompatibility the old Go
  backend hit with pgx prepared statements. Not a concern for the local dev DB, which has no
  pooler in front of it.
- Every connection sends a distinct `application_name` — see `NewPool`/connection setup in
  `src/WellSpent.Infrastructure/`. Useful for telling connections apart in `pg_stat_activity`.
- Migrations: `src/WellSpent.Migrator` (DbUp), not EF Core Migrations — `db/migrations/*.sql`
  stays the schema source of truth, written by hand, applied forward-only. There is no
  automated rollback; write a new forward migration or apply the reversal by hand.
- Schema source of truth: `db/migrations/000001_init_schema.sql` onward
- Adding a migration: create `db/migrations/000060_*.sql` (next sequential number), following
  the existing files' `-- +goose Up` / `-- +goose Down` comment markers — the Migrator only
  runs the Up section; the Down section is there for a human doing a manual rollback

## Secrets

`.env.dev` is gitignored. `.env.dev.enc` is the SOPS-encrypted version (committed). Age private key
location is OS-specific, not one path for "Linux/macOS" — `sops` follows Go's native
`os.UserConfigDir()`, which differs between the two:
- **Windows:** `%APPDATA%\sops\age\keys.txt`
- **macOS:** `~/Library/Application Support/sops/age/keys.txt`
- **Linux:** `~/.config/sops/age/keys.txt`

Confirmed against `sops` 3.13.3's own error output on macOS, which lists every path it checked —
`~/.config/...` isn't among them, contrary to what this file used to claim. If `sops` can't find
a key that's definitely on disk, check it's at the path for the OS actually running it, not the
other one.

## Version bump (required for every feature)

`src/WellSpent.Application/Changelog/ChangelogConstants.cs`'s `ServerVersion`. Bump it
once per feature, same convention as every other repo here — patch for fixes, minor for new
behaviour, major for a breaking change.

It is not decorative. `ListChangelog` reports it so a client can tell which server releases
are new to it, and a changelog release is keyed on it. If the constant doesn't move, the
changelog has nothing to hang off and readers never see what shipped.

Write the matching release notes in the same change: `changelog/server-<version>.json`
(see `changelog/README.md`). Authoring is part of the feature; **publishing is
not** — that happens at release time via `./scripts/changelog.sh publish`,
because at feature time nothing has shipped yet.

## Git workflow

`main` is production. Never commit or push directly to `main`. Never add a `Co-Authored-By: Claude` (or any AI attribution) trailer to commit messages — standing rule across the whole workspace.

**Before starting any work:**

```bash
git checkout develop
git pull origin develop
```

**Final steps after implementation:**

```bash
# Run checks first
make test
make build

# Stage specific files (never git add -A)
git add src/... tests/...

# Commit and push
git commit -m "feat: meaningful description of what changed"
git push origin develop

# Create PR from develop → main and immediately enable auto-merge
gh pr create --base main --head develop --title "Short title" --body "Description"
gh pr merge develop --auto --merge
```

- Always pull `develop` before starting — never work from a stale base
- Commit directly to `develop`; never commit directly to `main`
- `gh pr merge --auto` enables auto-merge — the PR lands once CI passes; no manual merge needed
- Never merge PRs manually — always let auto-merge handle it after checks pass
- The `Contribution` ruleset on `main` requires `ci-csharp.yml`'s `Build & Test` check
  (`.github/workflows/ci-csharp.yml`, runs on push/PR to `develop`) — keep that job name in
  sync with the ruleset if it's ever renamed, the same way the old Go `Generate`/`Test` names
  were pinned there before.
