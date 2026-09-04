# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
make generate            # buf generate + oapi-codegen + sqlc generate — run after any contract change
make generate-rest       # REST types only, from WellSpent-proto's OpenAPI contract
make run                 # start dev server (ENV=dev by default)
make test                # go test ./...
make build               # compile to bin/server
make migrate ENV=dev     # apply pending goose migrations to the local dev DB
make migrate-down ENV=dev
make secrets-decrypt ENV=dev   # decrypt .env.dev.enc → .env.dev (gitignored)
make secrets-encrypt ENV=dev   # encrypt .env.dev → .env.dev.enc (commit this)
```

Run a single test:
```bash
go test ./internal/service/... -run TestRegister_Success
```

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
docker compose -f ../docker-compose.dev.yml up backend    # from the workspace root
```

- **Migrations run automatically at container start** (`Dockerfile.dev`'s CMD), and the
  container fails to start if they fail — a server on a stale schema produces errors a long way
  from their cause. Adding a migration while it's running needs
  `docker compose -f ../docker-compose.dev.yml restart backend`, not a rebuild.
- **`make generate` still runs on the host.** air rebuilds the binary; it does not run
  `buf`/`sqlc`. But `gen/` and `internal/sqlc/` are watched, so regenerating does trigger a
  rebuild — they were excluded until 2026-08-18, which made regenerated types silently
  invisible to the running server and looked like air being broken.
- **A rebuild (`docker compose ... build backend`) is only needed when `Dockerfile.dev`
  itself changes.** Source, generated code, and dependencies all come through the volume mounts
  and the cached module volume.

## Two transports

Almost everything is ConnectRPC. A small, deliberately narrow set of endpoints
is plain REST instead, under `/rest/v1/`, served by `internal/rest/`.

An endpoint qualifies for REST only if it is **both** global (the identical
response for every caller) **and** rarely-changing. That is currently three
endpoints plus a probe. Every personalized read and **every mutation without
exception** stays on Connect — HTTP caching is the only thing the REST
transport buys, so an endpoint that cannot be cached does not belong there.
`WellSpent-proto/openapi/README.md` holds the rule and the rejected candidates.

Things worth knowing before touching `internal/rest/`:

- **It owns no business logic.** Each controller calls the same
  `internal/service` method the Connect handler called. `internal/rest/convert.go`
  is the REST counterpart of `internal/handler/convert.go` and does nothing else.
- **Routes mount on the same mux** in `cmd/server/main.go`, which is why they
  inherit CORS, IP rate limiting and h2c for free. Do not stand up a second
  server.
- **Auth is `auth.AuthenticateHeader`**, shared with the Connect interceptor, so
  the two transports cannot drift on what a valid caller is.
- **Errors mirror `internal/handler/errors.go` one-for-one** (`internal/rest/errors.go`).
  The same service error must mean the same thing on both transports.
- **The OpenAPI contract does not come from BSR** — there is no equivalent
  channel. `make generate-rest` fetches the raw file from the public
  `WellSpent-proto` repo. Output lands in `gen/rest/`, gitignored like `gen/`.

## Architecture

Four layers, in dependency order for new features:

1. **Proto** — define/update RPC in `WellSpent-proto/`, push to BSR, then `make generate`
2. **Repository** — add SQL to `internal/db/query/*.sql`, run `sqlc generate`, implement interface in `internal/repository/`
3. **Service** — business logic in `internal/service/`, write unit tests with mock repo
4. **Handler** — RPC impl in `internal/handler/`, map errors via `toConnectError()` in `errors.go`

Service wiring: `cmd/server/main.go` constructs all repos → services → handlers and registers them on the mux.

## Key patterns

**Errors:** use `apperr.NotFound / Forbidden / Duplicate / Invalid` in service layer. Handlers call `toConnectError(err)` in `internal/handler/errors.go` to map these to Connect status codes.

**Auth:** `h.currentUserID(ctx)` in any handler returns the authenticated user's UUID. RPCs that skip auth are in the bypass map in `cmd/server/main.go`:
```go
bypass := map[string]bool{
    AuthServiceRegisterProcedure:         true,
    AuthServiceLoginProcedure:            true,
    AuthServiceGetGoogleAuthURLProcedure: true,
    AuthServiceExchangeGoogleCodeProcedure: true,
}
```

**Money:** stored as `pgtype.Numeric` in DB, converted to/from proto `Money` (units + nanos) in `internal/handler/convert.go`.

**Tests:** mock repos use function-field structs (no third-party mock library). See `internal/service/auth_service_test.go` for the pattern — each field is a `func(...)` that the test overrides per case.

## Generated files — do not edit

- `internal/sqlc/` — generated by sqlc from `internal/db/query/*.sql`
- `gen/` — generated by buf from WellSpent-proto
- `gen/rest/` — generated by oapi-codegen from WellSpent-proto's `openapi/v1/`

## Database

- **Dev**: a standalone local Postgres in Docker, shared across your LAN — see "Local dev
  database" above. **Prod**: Neon serverless PostgreSQL.
- Prod only: use the **direct** connection URL (no `-pooler` hostname) — pgx prepared statements
  are incompatible with Neon's PgBouncer pooler. Not a concern for the local dev DB, which has no
  pooler in front of it.
- `MinConns = 0` in pgxpool (`internal/db/conn.go`) was originally to allow Neon scale-to-zero;
  harmless (just means no pre-warmed idle connections) against the local dev DB too, so it's
  left as one setting for both rather than branching on environment.
- Every connection sends a distinct `application_name` (`wellspent-server-dev`,
  `wellspent-plaid-sync-prod`, …) — see `NewPool` in `internal/db/conn.go`. Useful for telling
  connections apart in `pg_stat_activity`, which is how the Neon CU investigation above started.
- Migrations: goose v3 as a library via `cmd/migrate/main.go` (not the goose CLI — Neon's prod
  connection string requires pgx/v5, which the CLI doesn't use; kept as the one path for both
  environments rather than a second migration runner for local dev)
- Schema source of truth: `internal/db/migrations/000001_init_schema.sql`
- Adding a migration: create `000002_*.sql`, add it to `schema:` in `sqlc.yaml`

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

The server has a version now: `internal/version/version.go`'s `Current`. Bump it
once per feature, the same way `WellSpent-web`'s `package.json` is bumped —
patch for fixes, minor for new behaviour, major for a breaking change.

It is not decorative. `ListChangelog` reports it so a client can tell which
server releases are new to it, and a changelog release is keyed on it. If the
constant doesn't move, the changelog has nothing to hang off and readers never
see what shipped.

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
git add internal/... cmd/...

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
