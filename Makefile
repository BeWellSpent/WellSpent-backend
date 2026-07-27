.PHONY: generate run cycle test build tidy secrets-encrypt secrets-decrypt migrate migrate-down

export ENV ?= dev

generate:
	buf generate
	sqlc generate

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
