.PHONY: generate run test build tidy secrets-encrypt secrets-decrypt migrate migrate-down

ENV ?= dev

generate:
	buf generate
	sqlc generate

run:
	ENV=$(ENV) go run ./cmd/server

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
	@URL=$$(grep '^DATABASE_URL=' .env.$(ENV) | cut -d= -f2- | sed 's|postgresql://|postgres://|') && \
		migrate -path internal/db/migrations -database "$$URL" up

migrate-down:
	@URL=$$(grep '^DATABASE_URL=' .env.$(ENV) | cut -d= -f2- | sed 's|postgresql://|postgres://|') && \
		migrate -path internal/db/migrations -database "$$URL" down 1
