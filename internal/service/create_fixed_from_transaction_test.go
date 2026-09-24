package service

import (
	"context"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

type fixedFromTxFixture struct {
	userID     uuid.UUID
	profileID  uuid.UUID
	periodID   uuid.UUID
	txID       uuid.UUID
	feID       uuid.UUID
	spawnedID  uuid.UUID
	tx         db.Transaction
	archived   bool
	existingRv *db.TransactionReview

	createdFE     *db.CreateFixedExpenseParams
	upsertCalled  bool
	markedPaid    *db.MarkTransactionAsPaidParams
	excluded      *db.SetTransactionExcludedParams
	statusUpdated string
}

func newFixedFromTxFixture(t *testing.T) *fixedFromTxFixture {
	t.Helper()
	f := &fixedFromTxFixture{
		userID:    uuid.New(),
		profileID: uuid.New(),
		periodID:  uuid.New(),
		txID:      uuid.New(),
		feID:      uuid.New(),
		spawnedID: uuid.New(),
	}
	name := "Netflix"
	varType := int32(2)
	f.tx = db.Transaction{
		ID:                f.txID,
		Name:              &name,
		Amount:            numeric(t, "15.00"),
		BudgetPeriodID:    &f.periodID,
		TransactionTypeID: &varType,
	}
	return f
}

func (f *fixedFromTxFixture) svc(t *testing.T) *BudgetProfileService {
	t.Helper()
	fixedType := int32(1)
	spawned := db.Transaction{
		ID:                f.spawnedID,
		Name:              f.tx.Name,
		Amount:            f.tx.Amount,
		PlannedAmount:     f.tx.Amount,
		BudgetPeriodID:    &f.periodID,
		TransactionTypeID: &fixedType,
		FixedExpenseID:    &f.feID,
	}
	startDate, err := time.Parse("2006-01-02", "2026-09-01")
	require.NoError(t, err)

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: f.periodID, BudgetProfileID: f.profileID, IsArchived: f.archived}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: f.profileID, UserID: f.userID}, nil
			},
			getLatestPeriod: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: f.periodID, BudgetProfileID: f.profileID, StartDate: pgtype.Date{Time: startDate, Valid: true}}, nil
			},
		},
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				if id == f.spawnedID {
					return spawned, nil
				}
				out := f.tx
				out.IsExcluded = f.excluded != nil
				return out, nil
			},
			create: func(_ context.Context, _ db.CreateTransactionParams) (db.Transaction, error) {
				return spawned, nil
			},
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				f.markedPaid = &arg
				out := spawned
				out.IsPaid = true
				return out, nil
			},
			setExcluded: func(_ context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
				f.excluded = &arg
				out := f.tx
				out.IsExcluded = true
				return out, nil
			},
		},
		&mockFixedExpenseRepo{
			create: func(_ context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
				f.createdFE = &arg
				return db.FixedExpense{ID: f.feID, BudgetProfileID: f.profileID, Name: arg.Name, PlannedAmount: arg.PlannedAmount, CategoryID: arg.CategoryID, PaymentMethodID: arg.PaymentMethodID}, nil
			},
		},
		&mockUserRepo{},
	)
	svc.WithReviews(&mockTransactionReviewRepo{
		getByTransactionID: func(_ context.Context, _ uuid.UUID) (db.TransactionReview, error) {
			if f.existingRv != nil {
				return *f.existingRv, nil
			}
			return db.TransactionReview{}, apperr.NotFound("transaction_review", "")
		},
		upsert: func(_ context.Context, periodID, transactionID, matchedTransactionID uuid.UUID, score float64) (db.TransactionReview, error) {
			f.upsertCalled = true
			return db.TransactionReview{ID: uuid.New(), BudgetPeriodID: periodID, TransactionID: transactionID, MatchedTransactionID: matchedTransactionID, MatchScore: numeric(t, "100"), Status: "pending"}, nil
		},
		updateStatus: func(_ context.Context, _ uuid.UUID, status string) error {
			f.statusUpdated = status
			return nil
		},
	})
	return svc
}

func (f *fixedFromTxFixture) input() FixedFromTransactionInput {
	return FixedFromTransactionInput{
		TransactionID:  f.txID,
		BudgetPeriodID: f.periodID,
		FrequencyUnit:  1,
		IntervalMonths: 1,
	}
}

func TestCreateFixedExpenseFromTransaction_CreatesAndAutoConfirmsMatch(t *testing.T) {
	f := newFixedFromTxFixture(t)
	fe, tx, err := f.svc(t).CreateFixedExpenseFromTransaction(context.Background(), f.userID, f.input())
	require.NoError(t, err)

	require.NotNil(t, f.createdFE)
	assert.Equal(t, "Netflix", f.createdFE.Name, "the fixed expense inherits the transaction's name")
	assert.EqualValues(t, centsOf(t, f.tx.Amount), centsOf(t, f.createdFE.PlannedAmount))

	assert.True(t, f.upsertCalled, "must create a real transaction_review row, not a bespoke link")
	require.NotNil(t, f.markedPaid, "the spawned fixed transaction must be marked paid")
	require.NotNil(t, f.excluded, "the original transaction must be excluded, same as a normal review confirm")
	assert.Equal(t, "confirmed", f.statusUpdated)
	assert.Equal(t, f.feID, fe.ID)
	assert.True(t, tx.IsExcluded)
}

func TestCreateFixedExpenseFromTransaction_HonoursNameOverride(t *testing.T) {
	f := newFixedFromTxFixture(t)
	inp := f.input()
	inp.Name = "Streaming"
	_, _, err := f.svc(t).CreateFixedExpenseFromTransaction(context.Background(), f.userID, inp)
	require.NoError(t, err)
	assert.Equal(t, "Streaming", f.createdFE.Name)
}

func TestCreateFixedExpenseFromTransaction_RejectsFixedTransaction(t *testing.T) {
	f := newFixedFromTxFixture(t)
	fixedType := int32(1)
	f.tx.TransactionTypeID = &fixedType
	_, _, err := f.svc(t).CreateFixedExpenseFromTransaction(context.Background(), f.userID, f.input())
	require.Error(t, err)
	assert.Nil(t, f.createdFE)
}

func TestCreateFixedExpenseFromTransaction_RejectsReceivedAmount(t *testing.T) {
	f := newFixedFromTxFixture(t)
	f.tx.Amount = numeric(t, "-15.00")
	_, _, err := f.svc(t).CreateFixedExpenseFromTransaction(context.Background(), f.userID, f.input())
	require.Error(t, err)
	assert.Nil(t, f.createdFE)
}

func TestCreateFixedExpenseFromTransaction_RejectsArchivedPeriod(t *testing.T) {
	f := newFixedFromTxFixture(t)
	f.archived = true
	_, _, err := f.svc(t).CreateFixedExpenseFromTransaction(context.Background(), f.userID, f.input())
	require.Error(t, err)
	assert.Nil(t, f.createdFE)
}

func TestCreateFixedExpenseFromTransaction_RejectsAlreadyMatchedTransaction(t *testing.T) {
	f := newFixedFromTxFixture(t)
	existing := db.TransactionReview{ID: uuid.New(), Status: "confirmed"}
	f.existingRv = &existing
	_, _, err := f.svc(t).CreateFixedExpenseFromTransaction(context.Background(), f.userID, f.input())
	require.Error(t, err)
	assert.Nil(t, f.createdFE)
}

func TestCreateFixedExpenseFromTransaction_AllowsPreviouslyDismissedTransaction(t *testing.T) {
	f := newFixedFromTxFixture(t)
	existing := db.TransactionReview{ID: uuid.New(), Status: "dismissed"}
	f.existingRv = &existing
	_, _, err := f.svc(t).CreateFixedExpenseFromTransaction(context.Background(), f.userID, f.input())
	require.NoError(t, err)
	assert.NotNil(t, f.createdFE)
}
