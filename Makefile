.PHONY: run cycle test build secrets-encrypt secrets-decrypt migrate

export ENV ?= dev

run:
	cd src/WellSpent.Api && dotnet run

cycle:
	dotnet run --project src/WellSpent.Jobs.CycleBudgets

build:
	dotnet build WellSpent.slnx -c Release

test:
	dotnet test WellSpent.slnx -c Release

secrets-encrypt:
	sops --encrypt --output .env.$(ENV).enc .env.$(ENV)

secrets-decrypt:
	sops --decrypt --output .env.$(ENV) .env.$(ENV).enc

migrate:
	dotnet run --project src/WellSpent.Migrator up $(ENV)
