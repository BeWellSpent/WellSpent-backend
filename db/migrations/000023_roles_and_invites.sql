-- +goose Up

-- Add role to budget_to_profile_mapping
ALTER TABLE budget_to_profile_mapping
  ADD COLUMN role TEXT NOT NULL DEFAULT 'unspecified'
    CHECK (role IN ('unspecified', 'admin', 'collaborator', 'viewer'));

-- Back-fill: budget creator → admin; other linked users → collaborator; unlinked placeholders → unspecified
UPDATE budget_to_profile_mapping btpm
SET role = CASE
    WHEN btpm.user_id = bp.user_id THEN 'admin'
    WHEN btpm.user_id IS NOT NULL THEN 'collaborator'
    ELSE 'unspecified'
END
FROM budget_profile bp
WHERE btpm.budget_profile_id = bp.id;

-- Invite table
CREATE TABLE budget_invite (
    id                UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    budget_profile_id UUID        NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    email             VARCHAR(255) NOT NULL,
    role              TEXT        NOT NULL CHECK (role IN ('admin', 'collaborator', 'viewer')),
    token             UUID        NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    status            TEXT        NOT NULL DEFAULT 'pending'
                                    CHECK (status IN ('pending', 'accepted', 'cancelled', 'expired')),
    invited_by        UUID        NOT NULL REFERENCES users(id),
    budget_person_id  BIGINT      REFERENCES budget_to_profile_mapping(id),
    expires_at        TIMESTAMPTZ NOT NULL,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_budget_invite_token ON budget_invite(token);
CREATE INDEX idx_budget_invite_profile ON budget_invite(budget_profile_id);

-- +goose Down
DROP TABLE IF EXISTS budget_invite;
ALTER TABLE budget_to_profile_mapping DROP COLUMN IF EXISTS role;
