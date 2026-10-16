-- +goose Up

-- Let a budget profile be deleted while it still has payment methods.
--
-- `payment_methods` is the one table reachable from a budget that has no
-- `budget_profile_id` of its own — it is user-scoped, and belongs to a budget
-- only through `budget_person_id`. Every other table with a `budget_person_id`
-- (income_source, savings_source, expense_allocation, budget_invite) also
-- carries a `budget_profile_id` with ON DELETE CASCADE, so those rows are gone
-- by the time the constraint is checked. Payment methods are not, so
-- `DELETE FROM budget_profile` failed with a foreign-key violation for any
-- budget that had ever had one.
--
-- SET NULL rather than CASCADE deliberately. CASCADE would be one line and no
-- service change, but it also means a *hard* delete of a person destroys their
-- payment methods. RemoveBudgetPerson soft-deletes today (`is_active`), so it
-- would not fire now — it is a trap laid for whoever changes that later.
-- SET NULL makes the constraint incapable of blocking; deleting the orphans is
-- then an explicit, visible step in BudgetProfileService.Delete.
ALTER TABLE payment_methods
    DROP CONSTRAINT IF EXISTS payment_methods_budget_person_id_fkey;

ALTER TABLE payment_methods
    ADD CONSTRAINT payment_methods_budget_person_id_fkey
    FOREIGN KEY (budget_person_id) REFERENCES budget_to_profile_mapping(id)
    ON DELETE SET NULL;

-- +goose Down

ALTER TABLE payment_methods
    DROP CONSTRAINT IF EXISTS payment_methods_budget_person_id_fkey;

ALTER TABLE payment_methods
    ADD CONSTRAINT payment_methods_budget_person_id_fkey
    FOREIGN KEY (budget_person_id) REFERENCES budget_to_profile_mapping(id);
