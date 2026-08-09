package handler

import (
	"testing"

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
