package handler

import (
	"testing"

	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
)

func TestIsApplePrivateEmail(t *testing.T) {
	tests := []struct {
		name  string
		email string
		want  bool
	}{
		{"relay alias", "a1b2c3d4e5@privaterelay.appleid.com", true},
		{"relay alias is matched case-insensitively", "A1B2C3@PrivateRelay.AppleID.com", true},
		{"relay alias with surrounding whitespace", "  a1b2c3@privaterelay.appleid.com  ", true},
		{"ordinary address", "jane@example.com", false},
		{"a real iCloud address is not a relay alias", "jane@icloud.com", false},
		{"an appleid.com address that isn't the relay subdomain", "jane@appleid.com", false},
		{"empty", "", false},
		// The suffix must anchor at the end — an address merely containing the
		// relay domain earlier in the string is not an alias.
		{"relay domain appearing mid-address", "privaterelay.appleid.com@evil.example.com", false},
	}

	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			assert.Equal(t, tc.want, isApplePrivateEmail(tc.email))
		})
	}
}

func TestIsVerificationSatisfied(t *testing.T) {
	tests := []struct {
		name        string
		verified    bool
		accountType string
		want        bool
	}{
		{"an ordinary verified account", true, "standard", true},
		{"an ordinary unverified account is gated", false, "standard", false},
		// The whole point: a test account reaches the app without ever
		// proving an address.
		{"a test account is exempt even though it never verified", false, accountTypeTest, true},
		{"a verified test account stays satisfied", true, accountTypeTest, true},
		// Guards the CHECK constraint's contract — only the exact 'test'
		// value exempts, so a typo'd or future account_type never silently
		// switches verification off.
		{"an unrecognised account type does not exempt", false, "tester", false},
		{"an empty account type does not exempt", false, "", false},
	}

	for _, tc := range tests {
		t.Run(tc.name, func(t *testing.T) {
			got := isVerificationSatisfied(db.User{IsVerified: tc.verified, AccountType: tc.accountType})
			assert.Equal(t, tc.want, got)
		})
	}
}

// The stored column must stay truthful — the exemption lives in account_type,
// and laundering it into is_verified would destroy the record of whether an
// address was ever actually proven.
func TestToProtoUser_TestAccountReportsVerifiedWithoutAlteringTheStoredFlag(t *testing.T) {
	stored := db.User{ID: uuid.New(), Email: "qa@example.com", IsVerified: false, AccountType: accountTypeTest}

	proto := toProtoUser(stored)

	assert.True(t, proto.IsVerified, "a test account must not be gated by either client")
	assert.False(t, stored.IsVerified, "the stored verification flag must be left alone")
}
