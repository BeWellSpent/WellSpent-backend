# SpendSense Backend (Go)

Income-first expense tracker. Set your total income, allocate fixed expenses, savings, and needs — then add variable expenses gradually to see how much you have left to spend.

Built with **Go + ConnectRPC**. Works natively with a React frontend via `@connectrpc/connect-web` — no Envoy proxy required.

> **API contract (proto files)** are maintained in the separate [`SpendSense-proto`](https://github.com/XpendSense/SpendSense-proto) repository and published to the [Buf Schema Registry](https://buf.build/xpendsense/spendsense).

---

## Stack

| Concern | Library |
|---|---|
| RPC | [ConnectRPC](https://connectrpc.com) |
| API contract | [buf.build/xpendsense/spendsense](https://buf.build/xpendsense/spendsense) |
| Database | Local Postgres in Docker (dev) / [Neon](https://neon.tech) serverless PostgreSQL (prod), via `jackc/pgx/v5` |
| Queries | `sqlc` (type-safe Go from SQL) |
| Auth | JWT (`golang-jwt/jwt/v5`) + Google OAuth2 |
| Secrets | [SOPS](https://github.com/getsops/sops) + [age](https://github.com/FiloSottile/age) |
| Migrations | [goose v3](https://github.com/pressly/goose) (library mode via `cmd/migrate`) |
| CI | GitHub Actions — build, test, migrate on every push |

---

## Prerequisites

- Go 1.23+
- [buf CLI](https://buf.build/docs/installation)
- [sqlc CLI](https://docs.sqlc.dev/en/latest/overview/install.html)
- [SOPS CLI](https://github.com/getsops/sops/releases) (for secrets)
- [age CLI](https://github.com/FiloSottile/age/releases) (for generating/using encryption keys)

```powershell
go install github.com/bufbuild/buf/cmd/buf@latest
go install github.com/sqlc-dev/sqlc/cmd/sqlc@latest
```

---

## One-time setup

### 1. Get your age private key

Secret files (`.env.dev`, `.env.prod`, etc.) are encrypted with SOPS + age. To decrypt them you need the private key. Place it at the SOPS default location — **this is OS-specific, not one path
for "Linux/macOS"**, since `sops` follows Go's native `os.UserConfigDir()`:

- **Windows:** `%APPDATA%\sops\age\keys.txt`
- **macOS:** `~/Library/Application Support/sops/age/keys.txt`
- **Linux:** `~/.config/sops/age/keys.txt`

If `sops` fails with "failed to load age identities" even though the key file exists, it's
almost always this — the key is sitting at the *other* platform's default path. `sops`'s own
error output lists every location it actually checked, which is the fastest way to confirm.

Ask a team member for the key, or if setting up a new environment, generate one:

```powershell
age-keygen -o "$env:APPDATA\sops\age\keys.txt"
# then share the public key so .sops.yaml can be updated
```

### 2. Start (or connect to) the local database

Dev runs on a plain Postgres in Docker, not Neon — Neon is prod-only now. Two cases:

**The DB doesn't exist on your LAN yet — you're setting up the host machine:**

```powershell
cp .env.db.example .env.db      # then edit POSTGRES_PASSWORD to something real
docker compose -f docker-compose.db.yml --env-file .env.db up -d
```

This starts an empty Postgres and leaves it running (`restart: unless-stopped`), listening on
`:5432` on every network interface of this machine — including your LAN, not just `localhost`.
Open an inbound firewall rule for TCP 5432 on this machine, or nothing else on the network can
reach it. It stays up across reboots as long as Docker itself is set to launch on login.

**The DB already runs on another machine on your LAN:** you don't run the command above at all —
skip straight to step 3 and point `DATABASE_URL` at that machine's LAN address.

Either way, `up -d` only gives you an *empty* Postgres — no tables yet. That's step 4, below.

### 3. Decrypt secrets

```powershell
make secrets-decrypt ENV=dev
# produces .env.dev (gitignored)
```

`.env.dev` contains:

```
DATABASE_URL=postgresql://wellspent:<password>@<db-host-lan-address>:5432/wellspent_dev?sslmode=disable
JWT_SECRET=<a-long-random-string>
GOOGLE_CLIENT_ID=<from Google Cloud Console>
GOOGLE_CLIENT_SECRET=<from Google Cloud Console>
```

`<db-host-lan-address>` is whichever machine you ran step 2 on — **never `localhost`**, even if
that happens to be this machine: the backend running inside `docker-compose.dev.yml` would
resolve `localhost` to its own container, not the DB host. A DHCP reservation (static LAN IP) for
the host machine is worth setting up so this address doesn't drift.

### 4. Apply the database schema

```powershell
make migrate ENV=dev
```

Applies every pending migration in [internal/db/migrations/](internal/db/migrations/) (numbered
`000001_*.sql` files, applied in order — not a single `schema.sql`) via `cmd/migrate`, goose v3
wired to `pgx/v5`.

### 5. Authenticate with the Buf Schema Registry

```powershell
buf registry login buf.build
# Enter your BSR token when prompted (generate one at buf.build/settings/user)
```

Required to pull the proto module from BSR. Without it, `buf generate` will fail even for public modules.

### 6. Generate code

```powershell
make generate
# runs: buf generate + sqlc generate
```

> `buf generate` must run before `go build` — handlers import from `gen/` which only exists after generation.

### 7. Start the server

```powershell
make run
# Server starts on http://localhost:8080
```

---

## Daily development

```powershell
make run              # start server (dev env)
make test             # go test ./...
make build            # compile binary to bin/
make generate         # buf generate + sqlc generate
make tidy             # go mod tidy

make secrets-encrypt ENV=dev   # encrypt .env.dev → .env.dev.enc (commit this)
make secrets-decrypt ENV=dev   # decrypt .env.dev.enc → .env.dev (gitignored)
```

---

## Secrets workflow

Plaintext `.env.*` files are **gitignored**. Only the encrypted `.env.*.enc` files are committed.

To update a secret:

```powershell
# 1. Edit .env.dev as needed
# 2. Re-encrypt
make secrets-encrypt ENV=dev
# 3. Commit the .enc file
git add .env.dev.enc
```

CI decrypts using the `AGE_SECRET_KEY` repository secret (key contents, not a file path).

---

## Picking up proto changes

When `SpendSense-proto` publishes a new version:

```powershell
make generate   # fetches latest proto from BSR and regenerates Go code
```

---

## CI pipeline

There's no shared CI database — `ci-develop.yml`/`ci-main.yml` only generate and build/test
against mocked repos, no real `DATABASE_URL` involved. Each developer's local dev DB is their
own, and prod migrates itself on deploy:

**Every push to `develop`** (`ci-develop.yml`) — gates the `develop` → `main` PR:
1. **Generate** — proto + REST types from BSR/`WellSpent-proto`
2. **Build** — `go build ./...`
3. **Test** — `go test ./...`

**Every push to `main`** (`ci-main.yml`) — same generate/build/test, then on success:
4. **Migrate prod** — `go run ./cmd/migrate up` against Neon, using the `DATABASE_URL_PROD` secret
5. **Deploy** — builds and pushes the image, deploys the Cloud Run service and the two Cloud Run
   Jobs (`cycle-budgets`, `plaid-sync`)

### Why `cmd/migrate` instead of a CLI tool

We use goose as a **library** (`pressly/goose/v3`) wired to a `pgx/v5` connection rather than the goose or golang-migrate CLI. The CLI tools use `lib/pq` which does not handle Neon's `channel_binding=require` connection parameter, silently falling back to a local socket. `pgx/v5` handles all Neon parameters correctly — and using the same runner for local dev and prod means there's only one migration path to trust, not a second tool for local.

### Adding a GitHub secret

The CI decrypt step requires `AGE_SECRET_KEY` in GitHub repo secrets (Settings → Secrets → Actions). The value is the **full contents** of your age private key file (`%APPDATA%\sops\age\keys.txt`), not the file path.

---

## Schema changes

The schema is a sequence of numbered migrations in [internal/db/migrations/](internal/db/migrations/)
(`000001_init_schema.sql`, `000002_*.sql`, …), not one file. To change it:

1. Create the next `000NNN_*.sql` file
2. Add it to `schema:` in `sqlc.yaml`, then run `sqlc generate` to regenerate typed query methods
3. Apply it locally with `make migrate ENV=dev`
4. Commit the new migration and the regenerated `internal/sqlc/` files together

Prod picks up new migrations automatically — `ci-main.yml` runs `go run ./cmd/migrate up` against
Neon as part of every deploy. Nothing runs migrations against the local dev DB for you; that's
always a manual `make migrate ENV=dev`.

---

## Project structure

```
.
├── buf.yaml                    # Declares proto dependency (buf.build/bewellspent/wellspent)
├── buf.gen.yaml                # Code generation config — outputs Go types to gen/
├── buf.lock                    # Pinned proto version (commit this)
├── sqlc.yaml                   # Points at local schema + query SQL
├── .sops.yaml                  # age public key for SOPS encryption rules
├── docker-compose.db.yml       # Standalone local dev Postgres — see "Start (or connect to) the local database" above
├── .env.db.example             # Template for docker-compose.db.yml's .env.db (gitignored, not SOPS-encrypted)
│
├── gen/                        # Generated by `buf generate` — do not edit
│
├── internal/
│   ├── config/                 # Env-based configuration
│   ├── db/
│   │   ├── conn.go             # pgxpool setup; every connection tags itself with application_name
│   │   ├── migrations/         # Numbered goose migrations (000001_*.sql, 000002_*.sql, …)
│   │   └── query/              # SQL queries consumed by sqlc
│   ├── sqlc/                   # Generated by `sqlc generate` — do not edit
│   ├── auth/                   # JWT token service + Google OAuth client
│   ├── apperr/                 # Typed error values (NotFound, Forbidden, Duplicate, Invalid)
│   ├── middleware/             # ConnectRPC interceptors (auth, logging)
│   ├── repository/             # Database access layer
│   ├── service/                # Business logic layer (unit-tested)
│   └── handler/                # RPC handler implementations
│
└── cmd/
    └── server/main.go          # Application entry point
```

---

## Connecting a React frontend

The frontend consumes pre-generated npm packages published automatically by the BSR — no local proto tooling required.

```typescript
import { createConnectTransport } from "@connectrpc/connect-web";
import { createClient } from "@connectrpc/connect";
import { BudgetService } from "@buf/xpendsense_spendsense.connectrpc_es/spendsense/v1/budget_pb";

const transport = createConnectTransport({
  baseUrl: import.meta.env.VITE_API_URL ?? "http://localhost:8080",
  interceptors: [
    (next) => async (req) => {
      const token = localStorage.getItem("access_token");
      if (token) req.header.set("Authorization", `Bearer ${token}`);
      return next(req);
    },
  ],
});

export const budgetClient = createClient(BudgetService, transport);
```

---

## Docker

Two independent things run in Docker here — don't confuse them:

**The database** (`docker-compose.db.yml`) — see "Start (or connect to) the local database" above.
Started once on a designated host machine, left running, shared over the LAN by every other
machine's `DATABASE_URL`. Not something you build; it's the stock `postgres` image.

**The server** — this repo's own image, built from source, run against whatever `DATABASE_URL`
you point it at:

```powershell
docker build -t wellspent-backend .
docker run -p 8080:8080 --env-file .env.dev wellspent-backend
```

If the server itself is running in a container and the DB is on a different machine, no special
handling is needed — `.env.dev`'s `DATABASE_URL` already has to be the DB host's real LAN address
rather than `localhost` (see step 2 above), so it resolves the same way whether the server is
containerized or not. The one case that *does* need care is running both on the **same** machine
in separate containers with no `docker-compose.dev.yml` network between them: use that machine's
own LAN address for `DATABASE_URL`, not `localhost` or `127.0.0.1` — from inside the server's
container, those resolve to the server's own container, not the DB's.
