-- name: GetUserByID :one
SELECT id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
       country_code, state_code, filing_status, tax_payment_frequency
FROM users
WHERE id = $1
LIMIT 1;

-- name: GetUserByEmail :one
SELECT id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
       country_code, state_code, filing_status, tax_payment_frequency
FROM users
WHERE email = $1
LIMIT 1;

-- name: CreateUser :one
INSERT INTO users (email, hashed_password, first_name, last_name, country_code, state_code)
VALUES ($1, $2, $3, $4, $5, $6)
RETURNING id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
          country_code, state_code, filing_status, tax_payment_frequency;

-- name: UpdateUser :one
UPDATE users
SET first_name            = sqlc.arg('first_name'),
    last_name             = sqlc.arg('last_name'),
    country_code          = sqlc.arg('country_code'),
    state_code            = sqlc.arg('state_code'),
    filing_status         = sqlc.arg('filing_status'),
    tax_payment_frequency = sqlc.arg('tax_payment_frequency')
WHERE id = sqlc.arg('id')
RETURNING id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
          country_code, state_code, filing_status, tax_payment_frequency;

-- name: UpdateUserPassword :exec
UPDATE users
SET hashed_password = $2
WHERE id = $1;

-- name: DeleteUser :exec
DELETE FROM users
WHERE id = $1;

-- name: GetOAuthAccount :one
SELECT id, user_id, oauth_name, account_id, account_email
FROM oauth_account
WHERE oauth_name = $1 AND account_id = $2
LIMIT 1;

-- name: CreateOAuthAccount :one
INSERT INTO oauth_account (user_id, oauth_name, account_id, account_email)
VALUES ($1, $2, $3, $4)
RETURNING id, user_id, oauth_name, account_id, account_email;

-- name: ListEnabledCountries :many
SELECT code, name, is_enabled
FROM countries
WHERE is_enabled = TRUE
ORDER BY name;

-- name: ListCountryFeatures :many
SELECT country_code, feature_name, is_enabled
FROM country_features
ORDER BY country_code, feature_name;
