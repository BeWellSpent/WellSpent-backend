-- +goose Up

DROP TABLE IF EXISTS transaction;
DROP TABLE IF EXISTS income_to_budget_mapping;
DROP TABLE IF EXISTS payment_methods;
DROP TABLE IF EXISTS budget_to_user_mapping;
DROP TABLE IF EXISTS budget;

CREATE TABLE budget_profile (
    id         UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name       VARCHAR(100) NOT NULL,
    cycle      VARCHAR(20)  NOT NULL DEFAULT 'monthly',
    created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE budget_to_profile_mapping (
    id                SERIAL       PRIMARY KEY,
    budget_profile_id UUID         NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    user_name         VARCHAR(100),
    user_id           UUID         REFERENCES users(id),
    is_active         BOOLEAN      NOT NULL DEFAULT TRUE
);

CREATE TABLE payment_methods (
    id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name             VARCHAR(100) NOT NULL,
    payment_type_id  INTEGER      REFERENCES payment_type(id),
    user_id          UUID         REFERENCES users(id),
    is_active        BOOLEAN      NOT NULL DEFAULT TRUE,
    budget_person_id INTEGER      REFERENCES budget_to_profile_mapping(id)
);

CREATE TABLE income_source (
    id                SERIAL         PRIMARY KEY,
    budget_profile_id UUID           NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    budget_person_id  INTEGER        REFERENCES budget_to_profile_mapping(id),
    name              VARCHAR(100)   NOT NULL,
    income_type       VARCHAR(20)    NOT NULL DEFAULT 'other',
    default_amount    NUMERIC(15, 4) NOT NULL DEFAULT 0,
    recurring         BOOLEAN        NOT NULL DEFAULT TRUE,
    created_at        TIMESTAMPTZ    NOT NULL DEFAULT NOW()
);

CREATE TABLE budget_period (
    id                UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    budget_profile_id UUID        NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    start_date        DATE        NOT NULL,
    end_date          DATE        NOT NULL,
    is_archived       BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE income_entry (
    id               SERIAL         PRIMARY KEY,
    budget_period_id UUID           NOT NULL REFERENCES budget_period(id) ON DELETE CASCADE,
    income_source_id INTEGER        REFERENCES income_source(id),
    budget_person_id INTEGER        REFERENCES budget_to_profile_mapping(id),
    name             VARCHAR(100),
    amount           NUMERIC(15, 4) NOT NULL DEFAULT 0,
    created_at       TIMESTAMPTZ    NOT NULL DEFAULT NOW()
);

CREATE TABLE transaction (
    id                       UUID           PRIMARY KEY DEFAULT gen_random_uuid(),
    name                     VARCHAR(100),
    amount                   NUMERIC(15, 4) NOT NULL DEFAULT 0,
    planned_amount           NUMERIC(15, 4) NOT NULL DEFAULT 0,
    date                     DATE,
    renewal_date             DATE,
    recurring                BOOLEAN,
    budget_period_id         UUID           REFERENCES budget_period(id) ON DELETE CASCADE,
    category_id              INTEGER        REFERENCES category(id),
    payment_method_id        UUID           REFERENCES payment_methods(id),
    transaction_frequency_id INTEGER        REFERENCES transaction_frequency(id),
    transaction_type_id      INTEGER        REFERENCES transaction_type(id)
);

-- +goose Down

DROP TABLE IF EXISTS transaction;
DROP TABLE IF EXISTS income_entry;
DROP TABLE IF EXISTS budget_period;
DROP TABLE IF EXISTS income_source;
DROP TABLE IF EXISTS payment_methods;
DROP TABLE IF EXISTS budget_to_profile_mapping;
DROP TABLE IF EXISTS budget_profile;

CREATE TABLE budget (
    id         UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name       VARCHAR(100) NOT NULL,
    start_date DATE,
    end_date   DATE,
    active     BOOLEAN      NOT NULL DEFAULT TRUE
);

CREATE TABLE budget_to_user_mapping (
    id        SERIAL  PRIMARY KEY,
    budget_id UUID    NOT NULL REFERENCES budget(id) ON DELETE CASCADE,
    user_name VARCHAR(100),
    user_id   UUID    REFERENCES users(id),
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE income_to_budget_mapping (
    id               SERIAL         PRIMARY KEY,
    budget_id        UUID           NOT NULL REFERENCES budget(id) ON DELETE CASCADE,
    user_id          UUID           REFERENCES users(id),
    name             VARCHAR(100),
    amount           NUMERIC(15, 4) NOT NULL DEFAULT 0,
    recurring        BOOLEAN        NOT NULL DEFAULT FALSE,
    budget_person_id INT            REFERENCES budget_to_user_mapping(id)
);

CREATE TABLE payment_methods (
    id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name             VARCHAR(100) NOT NULL,
    payment_type_id  INTEGER      REFERENCES payment_type(id),
    user_id          UUID         REFERENCES users(id),
    is_active        BOOLEAN      NOT NULL DEFAULT TRUE,
    budget_person_id INTEGER      REFERENCES budget_to_user_mapping(id)
);

CREATE TABLE transaction (
    id                       UUID           PRIMARY KEY DEFAULT gen_random_uuid(),
    name                     VARCHAR(100),
    amount                   NUMERIC(15, 4) NOT NULL DEFAULT 0,
    planned_amount           NUMERIC(15, 4) NOT NULL DEFAULT 0,
    date                     DATE,
    renewal_date             DATE,
    recurring                BOOLEAN,
    budget_id                UUID           REFERENCES budget(id) ON DELETE CASCADE,
    category_id              INTEGER        REFERENCES category(id),
    payment_method_id        UUID           REFERENCES payment_methods(id),
    transaction_frequency_id INTEGER        REFERENCES transaction_frequency(id),
    transaction_type_id      INTEGER        REFERENCES transaction_type(id)
);
