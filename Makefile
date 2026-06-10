.PHONY: generate buf-update run test build tidy

ENV ?= dev

generate:
	buf generate
	sqlc generate

buf-update:
	buf dep update

run:
	ENV=$(ENV) go run ./cmd/server

build:
	go build -o bin/server ./cmd/server

test:
	go test ./...

tidy:
	go mod tidy
