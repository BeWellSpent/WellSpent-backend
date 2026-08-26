package service

import (
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
)

// A fixed expense whose payment method was deactivated used to respawn onto
// that dead account every month. Deleting a method reassigns its templates to
// the replacement the user picked, but the Plaid refresh path deactivates one
// with no replacement to offer, and nothing re-checked the template afterwards.
func TestLivePaymentMethod(t *testing.T) {
	live := uuid.New()
	dead := uuid.New()
	active := map[uuid.UUID]bool{live: true}

	t.Run("keeps a still-active method", func(t *testing.T) {
		assert.Equal(t, &live, livePaymentMethod(&live, active))
	})

	t.Run("drops a deactivated method", func(t *testing.T) {
		assert.Nil(t, livePaymentMethod(&dead, active),
			"a bill must not be attributed to an account the user has removed")
	})

	t.Run("passes through a template that never had one", func(t *testing.T) {
		assert.Nil(t, livePaymentMethod(nil, active))
	})

	t.Run("leaves the template alone when the lookup failed", func(t *testing.T) {
		// nil active set means ListPaymentMethods errored. Losing attribution
		// on every bill in the period because one query failed would be worse
		// than the problem this guards against.
		assert.Equal(t, &dead, livePaymentMethod(&dead, nil))
	})
}
