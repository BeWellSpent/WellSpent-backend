-- name: GetUserByID :one
SELECT id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
       country_code, state_code, filing_status, tax_payment_frequency, language, currency,
       email_verification_token, email_verification_expires_at, email_verification_last_sent_at
FROM users
WHERE id = $1
LIMIT 1;

-- name: GetUserByEmail :one
SELECT id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
       country_code, state_code, filing_status, tax_payment_frequency, language, currency,
       email_verification_token, email_verification_expires_at, email_verification_last_sent_at
FROM users
WHERE email = $1
LIMIT 1;

-- name: CreateUser :one
INSERT INTO users (email, hashed_password, first_name, last_name, country_code, state_code, language, currency)
VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
RETURNING id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
          country_code, state_code, filing_status, tax_payment_frequency, language, currency,
          email_verification_token, email_verification_expires_at, email_verification_last_sent_at;

-- name: UpdateUser :one
UPDATE users
SET first_name            = sqlc.arg('first_name'),
    last_name             = sqlc.arg('last_name'),
    country_code          = sqlc.arg('country_code'),
    state_code            = sqlc.arg('state_code'),
    filing_status         = sqlc.arg('filing_status'),
    tax_payment_frequency = sqlc.arg('tax_payment_frequency'),
    language              = sqlc.arg('language'),
    currency              = sqlc.arg('currency')
WHERE id = sqlc.arg('id')
RETURNING id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
          country_code, state_code, filing_status, tax_payment_frequency, language, currency,
          email_verification_token, email_verification_expires_at, email_verification_last_sent_at;

-- name: UpdateUserPassword :exec
UPDATE users
SET hashed_password = $2
WHERE id = $1;

-- name: DeleteUser :exec
DELETE FROM users
WHERE id = $1;

-- name: SetEmailVerificationToken :one
UPDATE users
SET email_verification_token = sqlc.arg('token'),
    email_verification_expires_at = sqlc.arg('expires_at'),
    email_verification_last_sent_at = sqlc.arg('last_sent_at')
WHERE id = sqlc.arg('id')
RETURNING id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
          country_code, state_code, filing_status, tax_payment_frequency, language, currency,
          email_verification_token, email_verification_expires_at, email_verification_last_sent_at;

-- name: GetUserByVerificationToken :one
SELECT id, email, hashed_password, first_name, last_name, is_active, is_superuser, is_verified, created_at,
       country_code, state_code, filing_status, tax_payment_frequency, language, currency,
       email_verification_token, email_verification_expires_at, email_verification_last_sent_at
FROM users
WHERE email_verification_token = sqlc.arg('token')
LIMIT 1;

-- name: MarkUserVerified :exec
UPDATE users
SET is_verified = TRUE,
    email_verification_token = NULL,
    email_verification_expires_at = NULL
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
