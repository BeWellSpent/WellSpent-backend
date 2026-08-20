package service

import (
	"context"
	"errors"
	"testing"

	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// The rule these cover: marking a fixed expense paid at an amount other than
// its plan rewrites the FixedExpense template — so the *next* period is planned
// at what the bill actually costs — but only when the budget opts in.
//
// Before this helper existed the three callers disagreed: only the manual
// button rewrote the template, so the same bill paid three ways left the next
// period planned at two different figures.

func TestMarkFixedTransactionPaid_AutoUpdateOn_RewritesTheTemplate(t *testing.T) {
	txID, feID := uuid.New(), uuid.New()
	paid := numericFromNanos(dollars(67.82))
	var updated *db.UpdateFixedExpensePlannedAmountParams

	tx, err := markFixedTransactionPaid(context.Background(),
		&mockTransactionRepo{
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				return db.Transaction{ID: arg.ID, FixedExpenseID: &feID}, nil
			},
		},
		&mockFixedExpenseRepo{
			updatePlannedAmount: func(_ context.Context, arg db.UpdateFixedExpensePlannedAmountParams) error {
				updated = &arg
				return nil
			},
		},
		db.MarkTransactionAsPaidParams{ID: txID, Amount: paid},
		true,
		"test",
	)

	require.NoError(t, err)
	assert.Equal(t, txID, tx.ID)
	require.NotNil(t, updated, "the template should follow the amount actually paid")
	assert.Equal(t, feID, updated.ID)
	assert.Equal(t, dollars(67.82), numericToNanos(updated.PlannedAmount))
}

func TestMarkFixedTransactionPaid_AutoUpdateOff_LeavesTheTemplateAlone(t *testing.T) {
	feID := uuid.New()
	called := false

	_, err := markFixedTransactionPaid(context.Background(),
		&mockTransactionRepo{
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				return db.Transaction{ID: arg.ID, FixedExpenseID: &feID}, nil
			},
		},
		&mockFixedExpenseRepo{
			updatePlannedAmount: func(_ context.Context, _ db.UpdateFixedExpensePlannedAmountParams) error {
				called = true
				return nil
			},
		},
		db.MarkTransactionAsPaidParams{ID: uuid.New(), Amount: numericFromNanos(dollars(67.82))},
		false,
		"test",
	)

	require.NoError(t, err)
	assert.False(t, called, "the plan must stay put when the budget hasn't opted in")
}

// A one-off Fixed transaction with no template behind it has nothing to update.
func TestMarkFixedTransactionPaid_NoTemplate_SkipsTheUpdate(t *testing.T) {
	called := false

	_, err := markFixedTransactionPaid(context.Background(),
		&mockTransactionRepo{
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				return db.Transaction{ID: arg.ID}, nil // FixedExpenseID nil
			},
		},
		&mockFixedExpenseRepo{
			updatePlannedAmount: func(_ context.Context, _ db.UpdateFixedExpensePlannedAmountParams) error {
				called = true
				return nil
			},
		},
		db.MarkTransactionAsPaidParams{ID: uuid.New()},
		true,
		"test",
	)

	require.NoError(t, err)
	assert.False(t, called)
}

// The payment is what matters; a failed template write is a wrong plan, not a
// wrong payment, and must not cost the caller a recorded payment.
func TestMarkFixedTransactionPaid_TemplateUpdateFails_PaymentStillStands(t *testing.T) {
	feID := uuid.New()

	tx, err := markFixedTransactionPaid(context.Background(),
		&mockTransactionRepo{
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				return db.Transaction{ID: arg.ID, FixedExpenseID: &feID}, nil
			},
		},
		&mockFixedExpenseRepo{
			updatePlannedAmount: func(_ context.Context, _ db.UpdateFixedExpensePlannedAmountParams) error {
				return errors.New("boom")
			},
		},
		db.MarkTransactionAsPaidParams{ID: uuid.New()},
		true,
		"test",
	)

	require.NoError(t, err)
	assert.NotEqual(t, uuid.Nil, tx.ID)
}

// Marking paid failing IS fatal — it used to be discarded with `_, _ =` in
// ConfirmTransactionReview, so a bill that never got marked paid was
// indistinguishable from one that did.
func TestMarkFixedTransactionPaid_MarkFails_ReturnsTheError(t *testing.T) {
	_, err := markFixedTransactionPaid(context.Background(),
		&mockTransactionRepo{
			markAsPaid: func(_ context.Context, _ db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				return db.Transaction{}, errors.New("db down")
			},
		},
		&mockFixedExpenseRepo{},
		db.MarkTransactionAsPaidParams{ID: uuid.New()},
		true,
		"test",
	)

	require.Error(t, err)
}

// A profile that can't be read must not silently flip the behaviour: the column
// defaults to true, and so does the fallback.
func TestAutoUpdatePlannedAmountFor_UnreadableProfileDefaultsToTrue(t *testing.T) {
	got := autoUpdatePlannedAmountFor(context.Background(),
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{}, errors.New("gone")
			},
		}, uuid.New(), "test")

	assert.True(t, got)
}

func TestAutoUpdatePlannedAmountFor_ReadsTheProfileSetting(t *testing.T) {
	got := autoUpdatePlannedAmountFor(context.Background(),
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{AutoUpdatePlannedAmount: false}, nil
			},
		}, uuid.New(), "test")

	assert.False(t, got)
}
