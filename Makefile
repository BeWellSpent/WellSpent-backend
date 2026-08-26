.PHONY: generate run cycle test build tidy secrets-encrypt secrets-decrypt migrate migrate-down

export ENV ?= dev

# The OpenAPI contract lives in WellSpent-proto but does not go through BSR —
# there is no OpenAPI equivalent. That repo is public, so this fetches the raw
# file. Override OPENAPI_SPEC with a local path to iterate against an unmerged
# contract change.
OPENAPI_SPEC ?= https://raw.githubusercontent.com/BeWellSpent/WellSpent-proto/main/openapi/v1/wellspent.yaml

generate: generate-proto generate-rest
	sqlc generate

generate-proto:
	buf generate

generate-rest:
	@mkdir -p gen/rest
	go tool oapi-codegen -config oapi-codegen.yaml $(OPENAPI_SPEC)

run:
	go run ./cmd/server

cycle:
	go run ./cmd/jobs/cycle-budgets

build:
	go build -o bin/server ./cmd/server

test:
	go test ./...

tidy:
	go mod tidy

secrets-encrypt:
	sops --encrypt --output .env.$(ENV).enc .env.$(ENV)

secrets-decrypt:
	sops --decrypt --output .env.$(ENV) .env.$(ENV).enc

migrate:
	go run ./cmd/migrate up $(ENV)

migrate-down:
	go run ./cmd/migrate down $(ENV)
