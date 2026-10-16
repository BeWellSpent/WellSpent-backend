-- +goose Up

CREATE TABLE IF NOT EXISTS users (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email           VARCHAR(255) NOT NULL UNIQUE,
    hashed_password TEXT,
    first_name      VARCHAR(100),
    last_name       VARCHAR(100),
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    is_superuser    BOOLEAN NOT NULL DEFAULT FALSE,
    is_verified     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS oauth_account (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id       UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    oauth_name    VARCHAR(50) NOT NULL,
    account_id    VARCHAR(255) NOT NULL,
    account_email VARCHAR(255) NOT NULL,
    UNIQUE (oauth_name, account_id)
);

CREATE TABLE IF NOT EXISTS budget (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name       VARCHAR(100) NOT NULL,
    start_date DATE,
    end_date   DATE,
    active     BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS budget_to_user_mapping (
    id        SERIAL PRIMARY KEY,
    budget_id UUID NOT NULL REFERENCES budget(id) ON DELETE CASCADE,
    user_name VARCHAR(100),
    user_id   UUID REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS income_to_budget_mapping (
    id        SERIAL PRIMARY KEY,
    budget_id UUID NOT NULL REFERENCES budget(id) ON DELETE CASCADE,
    user_id   UUID REFERENCES users(id),
    name      VARCHAR(100),
    amount    NUMERIC(15, 4) NOT NULL DEFAULT 0,
    recurring BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS transaction_type (
    id   SERIAL PRIMARY KEY,
    name VARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS transaction_frequency (
    id   SERIAL PRIMARY KEY,
    name VARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS category_type (
    id   SERIAL PRIMARY KEY,
    name VARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS payment_type (
    id   SERIAL PRIMARY KEY,
    name VARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS category (
    id      SERIAL PRIMARY KEY,
    name    VARCHAR(100) NOT NULL,
    type_id INTEGER REFERENCES category_type(id),
    user_id UUID REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS payment_methods (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(100) NOT NULL,
    payment_type_id INTEGER REFERENCES payment_type(id),
    user_id         UUID REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS transaction (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name                     VARCHAR(100),
    amount                   NUMERIC(15, 4) NOT NULL DEFAULT 0,
    planned_amount           NUMERIC(15, 4) NOT NULL DEFAULT 0,
    date                     DATE,
    renewal_date             DATE,
    recurring                BOOLEAN,
    budget_id                UUID REFERENCES budget(id) ON DELETE CASCADE,
    category_id              INTEGER REFERENCES category(id),
    payment_method_id        UUID REFERENCES payment_methods(id),
    transaction_frequency_id INTEGER REFERENCES transaction_frequency(id),
    transaction_type_id      INTEGER REFERENCES transaction_type(id)
);

INSERT INTO transaction_type (name)
    SELECT v FROM (VALUES ('Fixed'), ('Variable')) AS t(v)
    WHERE v NOT IN (SELECT name FROM transaction_type);

INSERT INTO transaction_frequency (name)
    SELECT v FROM (VALUES ('One-off'), ('Weekly'), ('Bi-weekly'), ('Monthly'), ('Yearly')) AS t(v)
    WHERE v NOT IN (SELECT name FROM transaction_frequency);

INSERT INTO category_type (name)
    SELECT v FROM (VALUES ('Expense'), ('Saving'), ('Income')) AS t(v)
    WHERE v NOT IN (SELECT name FROM category_type);

INSERT INTO payment_type (name)
    SELECT v FROM (VALUES ('Cash'), ('Credit'), ('Debit'), ('Digital Wallet'), ('Bank Transfer'), ('Crypto'), ('Investment'), ('Other')) AS t(v)
    WHERE v NOT IN (SELECT name FROM payment_type);

-- +goose Down

DROP TABLE IF EXISTS transaction CASCADE;
DROP TABLE IF EXISTS payment_methods CASCADE;
DROP TABLE IF EXISTS payment_type CASCADE;
DROP TABLE IF EXISTS category CASCADE;
DROP TABLE IF EXISTS category_type CASCADE;
DROP TABLE IF EXISTS transaction_frequency CASCADE;
DROP TABLE IF EXISTS transaction_type CASCADE;
DROP TABLE IF EXISTS income_to_budget_mapping CASCADE;
DROP TABLE IF EXISTS budget_to_user_mapping CASCADE;
DROP TABLE IF EXISTS budget CASCADE;
DROP TABLE IF EXISTS oauth_account CASCADE;
DROP TABLE IF EXISTS users CASCADE;
