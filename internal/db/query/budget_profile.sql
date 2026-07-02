-- ── BudgetProfile ─────────────────────────────────────────────────────────────

-- name: CreateBudgetProfile :one
INSERT INTO budget_profile (user_id, name, cycle, country_code)
VALUES ($1, $2, $3, $4)
RETURNING id, user_id, name, cycle, created_at, country_code;

-- name: ListBudgetProfilesByUser :many
SELECT id, user_id, name, cycle, created_at, country_code
FROM budget_profile
WHERE user_id = $1
ORDER BY created_at DESC;

-- name: GetBudgetProfileByID :one
SELECT id, user_id, name, cycle, created_at, country_code
FROM budget_profile
WHERE id = $1
LIMIT 1;

-- name: ExistsBudgetProfileByNameAndUser :one
SELECT EXISTS (
    SELECT 1 FROM budget_profile WHERE name = $1 AND user_id = $2
) AS exists;

-- name: UpdateBudgetProfile :one
UPDATE budget_profile
SET name = $2, cycle = $3
WHERE id = $1
RETURNING id, user_id, name, cycle, created_at, country_code;

-- name: DeleteBudgetProfile :exec
DELETE FROM budget_profile WHERE id = $1;

-- ── BudgetPeriod ──────────────────────────────────────────────────────────────

-- name: CreateBudgetPeriod :one
INSERT INTO budget_period (budget_profile_id, start_date, end_date)
VALUES ($1, $2, $3)
RETURNING id, budget_profile_id, start_date, end_date, is_archived, created_at;

-- name: GetBudgetPeriodByID :one
SELECT id, budget_profile_id, start_date, end_date, is_archived, created_at
FROM budget_period
WHERE id = $1
LIMIT 1;

-- name: ListBudgetPeriods :many
SELECT id, budget_profile_id, start_date, end_date, is_archived, created_at
FROM budget_period
WHERE budget_profile_id = $1
ORDER BY start_date DESC;

-- name: GetLatestBudgetPeriod :one
SELECT id, budget_profile_id, start_date, end_date, is_archived, created_at
FROM budget_period
WHERE budget_profile_id = $1
ORDER BY start_date DESC
LIMIT 1;

-- Used by the cycling job to find profiles whose current period just ended.
-- name: ListProfileIDsWithLatestPeriodEndingOn :many
SELECT budget_profile_id
FROM budget_period bp
WHERE bp.end_date = $1::date
  AND NOT EXISTS (
    SELECT 1 FROM budget_period bp2
    WHERE bp2.budget_profile_id = bp.budget_profile_id
      AND bp2.start_date > bp.start_date
  );

-- ── People ────────────────────────────────────────────────────────────────────

-- name: AddBudgetPersonToProfile :one
INSERT INTO budget_to_profile_mapping (budget_profile_id, user_name, user_id, color)
VALUES ($1, $2, $3, $4)
RETURNING id, budget_profile_id, user_name, user_id, is_active, color;

-- name: ListBudgetPeopleByProfile :many
SELECT id, budget_profile_id, user_name, user_id, is_active, color
FROM budget_to_profile_mapping
WHERE budget_profile_id = $1 AND is_active = TRUE
ORDER BY id;

-- name: GetBudgetPersonByProfileID :one
SELECT id, budget_profile_id, user_name, user_id, is_active, color
FROM budget_to_profile_mapping
WHERE id = $1 AND budget_profile_id = $2
LIMIT 1;

-- name: UpdateBudgetPerson :one
UPDATE budget_to_profile_mapping
SET color = sqlc.arg('color')
WHERE id = sqlc.arg('id') AND budget_profile_id = sqlc.arg('budget_profile_id')::uuid AND is_active = TRUE
RETURNING id, budget_profile_id, user_name, user_id, is_active, color;

-- name: ExistsBudgetPersonInProfile :one
SELECT EXISTS (
    SELECT 1 FROM budget_to_profile_mapping
    WHERE budget_profile_id = $1::uuid AND user_name = $2 AND is_active = TRUE
) AS exists;

-- name: SoftRemovePersonFromProfile :exec
WITH soft_delete_pms AS (
    UPDATE payment_methods
    SET is_active = FALSE
    WHERE budget_person_id = sqlc.arg('person_id')
)
UPDATE budget_to_profile_mapping
SET is_active = FALSE
WHERE budget_to_profile_mapping.id = sqlc.arg('person_id')
  AND budget_to_profile_mapping.budget_profile_id = sqlc.arg('budget_profile_id')::uuid;

-- name: SoftRemovePersonAndReassignFromProfile :exec
WITH reassign_transactions AS (
    UPDATE transaction
    SET payment_method_id = sqlc.arg('replacement_pm_id')::uuid
    WHERE payment_method_id IN (
        SELECT pm.id FROM payment_methods pm WHERE pm.budget_person_id = sqlc.arg('person_id')
    )
      AND budget_period_id IN (
        SELECT bp.id FROM budget_period bp WHERE bp.budget_profile_id = sqlc.arg('budget_profile_id')::uuid
      )
),
soft_delete_pms AS (
    UPDATE payment_methods
    SET is_active = FALSE
    WHERE budget_person_id = sqlc.arg('person_id')
),
reassign_income AS (
    UPDATE income_entry
    SET budget_person_id = sqlc.arg('replacement_person_id')
    WHERE budget_person_id = sqlc.arg('person_id')
      AND budget_period_id IN (
        SELECT bp.id FROM budget_period bp WHERE bp.budget_profile_id = sqlc.arg('budget_profile_id')::uuid
      )
)
UPDATE budget_to_profile_mapping
SET is_active = FALSE
WHERE budget_to_profile_mapping.id = sqlc.arg('person_id')
  AND budget_to_profile_mapping.budget_profile_id = sqlc.arg('budget_profile_id')::uuid;

-- ── Income Sources ────────────────────────────────────────────────────────────

-- name: AddIncomeSource :one
INSERT INTO income_source (budget_profile_id, budget_person_id, name, income_type, default_amount, recurring, payment_frequency, before_tax)
VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
RETURNING id, budget_profile_id, budget_person_id, name, income_type, default_amount, recurring, created_at, payment_frequency, before_tax;

-- name: ListIncomeSources :many
SELECT id, budget_profile_id, budget_person_id, name, income_type, default_amount, recurring, created_at, payment_frequency, before_tax
FROM income_source
WHERE budget_profile_id = $1
ORDER BY id;

-- name: UpdateIncomeSource :one
UPDATE income_source
SET name = $3, income_type = $4, default_amount = $5, recurring = $6, budget_person_id = $7, payment_frequency = $8, before_tax = $9
WHERE id = $1 AND budget_profile_id = $2
RETURNING id, budget_profile_id, budget_person_id, name, income_type, default_amount, recurring, created_at, payment_frequency, before_tax;

-- name: DeleteIncomeSource :exec
DELETE FROM income_source WHERE id = $1 AND budget_profile_id = $2;

-- ── Savings Sources ───────────────────────────────────────────────────────────

-- name: AddSavingsSource :one
INSERT INTO savings_source (budget_profile_id, budget_person_id, name, amount, frequency, payment_method_id)
VALUES ($1, $2, $3, $4, $5, $6)
RETURNING id, budget_profile_id, budget_person_id, name, amount, frequency, created_at, is_tax_reserve, federal_amount, state_amount, payment_method_id;

-- name: ListSavingsSources :many
SELECT id, budget_profile_id, budget_person_id, name, amount, frequency, created_at, is_tax_reserve, federal_amount, state_amount, payment_method_id
FROM savings_source
WHERE budget_profile_id = $1
ORDER BY id;

-- name: UpdateSavingsSource :one
UPDATE savings_source
SET name = $3, amount = $4, frequency = $5, budget_person_id = $6, payment_method_id = $7
WHERE id = $1 AND budget_profile_id = $2
RETURNING id, budget_profile_id, budget_person_id, name, amount, frequency, created_at, is_tax_reserve, federal_amount, state_amount, payment_method_id;

-- name: DeleteSavingsSource :exec
DELETE FROM savings_source WHERE id = $1 AND budget_profile_id = $2;

-- name: DeleteTaxReserveSavingsSource :exec
DELETE FROM savings_source WHERE budget_profile_id = $1 AND is_tax_reserve = TRUE;

-- Upserts the system-managed tax reserve savings source for a budget profile.
-- Uses the partial unique index idx_savings_source_tax_reserve.
-- name: UpsertTaxReserveSavingsSource :one
INSERT INTO savings_source (budget_profile_id, budget_person_id, name, amount, frequency, is_tax_reserve, federal_amount, state_amount)
VALUES (sqlc.arg('budget_profile_id')::uuid, sqlc.arg('budget_person_id'), 'Future Tax Payment', sqlc.arg('amount'), 'monthly', TRUE, sqlc.arg('federal_amount'), sqlc.arg('state_amount'))
ON CONFLICT (budget_profile_id, budget_person_id) WHERE is_tax_reserve = TRUE
DO UPDATE SET amount = EXCLUDED.amount, federal_amount = EXCLUDED.federal_amount, state_amount = EXCLUDED.state_amount
RETURNING id, budget_profile_id, budget_person_id, name, amount, frequency, created_at, is_tax_reserve, federal_amount, state_amount, payment_method_id;

-- ── Income Entries ────────────────────────────────────────────────────────────

-- name: CreateIncomeEntry :one
INSERT INTO income_entry (budget_period_id, income_source_id, budget_person_id, name, amount)
VALUES ($1, $2, $3, $4, $5)
RETURNING id, budget_period_id, income_source_id, budget_person_id, name, amount, created_at;

-- name: ListIncomeEntries :many
SELECT id, budget_period_id, income_source_id, budget_person_id, name, amount, created_at
FROM income_entry
WHERE budget_period_id = $1
ORDER BY id;

-- name: UpdateIncomeEntry :one
UPDATE income_entry
SET amount = $3
WHERE id = $1 AND budget_period_id = $2
RETURNING id, budget_period_id, income_source_id, budget_person_id, name, amount, created_at;
