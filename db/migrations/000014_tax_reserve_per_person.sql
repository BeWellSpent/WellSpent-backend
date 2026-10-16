-- +goose Up
-- Allow one tax reserve entry per person per budget (was per budget only).
DROP INDEX IF EXISTS idx_savings_source_tax_reserve;
CREATE UNIQUE INDEX idx_savings_source_tax_reserve
  ON savings_source (budget_profile_id, budget_person_id)
  WHERE is_tax_reserve = TRUE;

-- +goose Down
DROP INDEX IF EXISTS idx_savings_source_tax_reserve;
CREATE UNIQUE INDEX idx_savings_source_tax_reserve
  ON savings_source (budget_profile_id)
  WHERE is_tax_reserve = TRUE;
