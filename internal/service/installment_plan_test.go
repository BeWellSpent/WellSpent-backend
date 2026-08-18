package service

import (
	"context"
	"math/big"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

func numeric(t *testing.T, s string) pgtype.Numeric {
	t.Helper()
	var n pgtype.Numeric
	require.NoError(t, n.Scan(s))
	return n
}

func centsOf(t *testing.T, n pgtype.Numeric) int64 {
	t.Helper()
	require.True(t, n.Valid)
	require.EqualValues(t, -2, n.Exp, "installment amounts are always at cent scale")
	return n.Int.Int64()
}

func TestInstallmentAmount_EvenSplit(t *testing.T) {
	assert.EqualValues(t, 30000, centsOf(t, installmentAmount(numeric(t, "900.00"), 3)))
}

func TestInstallmentAmount_RoundsToNearestCentDown(t *testing.T) {
	// 1000/3 = 333.333... -> 333.33, so the plan totals 999.99. The one-cent
	// residue is structural: a fixed_expense carries a single planned_amount
	// that every payment inherits, so no payment can absorb the difference.
	assert.EqualValues(t, 33333, centsOf(t, installmentAmount(numeric(t, "1000.00"), 3)))
}

func TestInstallmentAmount_RoundsToNearestCentUp(t *testing.T) {
	// 1000/7 = 142.857... -> 142.86
	assert.EqualValues(t, 14286, centsOf(t, installmentAmount(numeric(t, "1000.00"), 7)))
}

func TestInstallmentAmount_ExactHalfCentRoundsAwayFromZero(t *testing.T) {
	// 0.05/2 = 0.025 exactly -> 0.03, not 0.02.
	assert.EqualValues(t, 3, centsOf(t, installmentAmount(numeric(t, "0.05"), 2)))
}

func TestInstallmentAmount_RoundsOnceNotTwice(t *testing.T) {
	// 100.004 / 2 = 50.002 -> 50.00. Scaling to cents first would floor the
	// input to 100.00 and give the same answer here, so pick a case where it
	// differs: 0.019 / 1 = 0.019 -> 0.02, whereas truncating to cents first
	// yields 0.01.
	assert.EqualValues(t, 2, centsOf(t, installmentAmount(numeric(t, "0.019"), 1)))
}

func TestInstallmentAmount_HandlesLargeAmountsExactly(t *testing.T) {
	// Well past float64's exact-integer range, to prove the big.Int path is
	// real and not a float64 in disguise.
	got := installmentAmount(pgtype.Numeric{Int: big.NewInt(1), Exp: 20, Valid: true}, 4)
	assert.Equal(t, "25000000000000000000", new(big.Int).Quo(got.Int, big.NewInt(100)).String())
}

func TestInstallmentAmount_InvalidInputs(t *testing.T) {
	assert.False(t, installmentAmount(pgtype.Numeric{}, 3).Valid)
	assert.False(t, installmentAmount(numeric(t, "100.00"), 0).Valid)
}

func TestInstallmentEndDate_LastPaymentNotOnePast(t *testing.T) {
	first := time.Date(2026, 9, 18, 0, 0, 0, 0, time.UTC)
	// 3 payments starting September: Sep, Oct, Nov — the plan ends in November,
	// not December.
	assert.Equal(t, time.Date(2026, 11, 18, 0, 0, 0, 0, time.UTC), installmentEndDate(first, 3))
}

func TestInstallmentEndDate_SinglePaymentEndsOnItself(t *testing.T) {
	first := time.Date(2026, 9, 18, 0, 0, 0, 0, time.UTC)
	assert.Equal(t, first, installmentEndDate(first, 1))
}

// ── CreateInstallmentPlan ─────────────────────────────────────────────────────

type installmentFixture struct {
	userID    uuid.UUID
	profileID uuid.UUID
	periodID  uuid.UUID
	txID      uuid.UUID
	feID      uuid.UUID
	tx        db.Transaction
	archived  bool

	createdParams *db.CreateFixedExpenseParams
	linkedParams  *db.SetTransactionInstallmentPlanParams
}

func newInstallmentFixture(t *testing.T) *installmentFixture {
	t.Helper()
	f := &installmentFixture{
		userID:    uuid.New(),
		profileID: uuid.New(),
		periodID:  uuid.New(),
		txID:      uuid.New(),
		feID:      uuid.New(),
	}
	name := "Laptop"
	varType := int32(2)
	f.tx = db.Transaction{
		ID:                f.txID,
		Name:              &name,
		Amount:            numeric(t, "1000.00"),
		BudgetPeriodID:    &f.periodID,
		TransactionTypeID: &varType,
	}
	return f
}

func (f *installmentFixture) svc() *BudgetProfileService {
	return NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: f.periodID, BudgetProfileID: f.profileID, IsArchived: f.archived}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: f.profileID, UserID: f.userID}, nil
			},
		},
		&mockTransactionRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.Transaction, error) {
				return f.tx, nil
			},
			setInstallmentPlan: func(_ context.Context, arg db.SetTransactionInstallmentPlanParams) (db.Transaction, error) {
				f.linkedParams = &arg
				out := f.tx
				out.IsExcluded = true
				out.InstallmentFixedExpenseID = &arg.InstallmentFixedExpenseID
				return out, nil
			},
		},
		&mockFixedExpenseRepo{
			create: func(_ context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
				f.createdParams = &arg
				return db.FixedExpense{
					ID:                f.feID,
					BudgetProfileID:   f.profileID,
					Name:              arg.Name,
					PlannedAmount:     arg.PlannedAmount,
					IsInstallmentPlan: arg.IsInstallmentPlan,
				}, nil
			},
		},
		&mockUserRepo{},
	)
}

func (f *installmentFixture) input() InstallmentPlanInput {
	return InstallmentPlanInput{
		TransactionID:    f.txID,
		BudgetPeriodID:   f.periodID,
		FirstPaymentDate: time.Date(2026, 9, 18, 0, 0, 0, 0, time.UTC),
		TotalPayments:    4,
	}
}

func TestCreateInstallmentPlan_SplitsAmountAndLinksTransaction(t *testing.T) {
	f := newInstallmentFixture(t)
	fe, tx, err := f.svc().CreateInstallmentPlan(context.Background(), f.userID, f.input())
	require.NoError(t, err)

	require.NotNil(t, f.createdParams)
	assert.EqualValues(t, 25000, centsOf(t, f.createdParams.PlannedAmount), "1000.00 over 4 payments")
	assert.Equal(t, "Laptop", f.createdParams.Name, "the plan inherits the purchase's name")
	assert.True(t, f.createdParams.IsInstallmentPlan, "must be flagged, or Plaid review will match against it")
	assert.EqualValues(t, 4, *f.createdParams.TotalPayments)
	assert.Equal(t, time.Date(2026, 12, 18, 0, 0, 0, 0, time.UTC), f.createdParams.EndDate.Time,
		"4 payments from September ends in December, not January")

	require.NotNil(t, f.linkedParams)
	assert.Equal(t, fe.ID, f.linkedParams.InstallmentFixedExpenseID)
	assert.True(t, tx.IsExcluded, "the purchase stops counting toward the period")
	require.NotNil(t, tx.InstallmentFixedExpenseID)
	assert.Equal(t, fe.ID, *tx.InstallmentFixedExpenseID)
}

// The whole point of scoping this RPC by profile rather than period: realising
// months later that a purchase was financed is normal, and its payments land in
// current and future periods regardless of when it happened.
func TestCreateInstallmentPlan_AllowedOnArchivedPeriod(t *testing.T) {
	f := newInstallmentFixture(t)
	f.archived = true
	_, tx, err := f.svc().CreateInstallmentPlan(context.Background(), f.userID, f.input())
	require.NoError(t, err)
	assert.True(t, tx.IsExcluded)
}

func TestCreateInstallmentPlan_HonoursEndDateOverride(t *testing.T) {
	f := newInstallmentFixture(t)
	override := time.Date(2027, 3, 18, 0, 0, 0, 0, time.UTC)
	inp := f.input()
	inp.EndDate = &override
	_, _, err := f.svc().CreateInstallmentPlan(context.Background(), f.userID, inp)
	require.NoError(t, err)
	assert.Equal(t, override, f.createdParams.EndDate.Time)
}

func TestCreateInstallmentPlan_RejectsFewerThanTwoPayments(t *testing.T) {
	f := newInstallmentFixture(t)
	inp := f.input()
	inp.TotalPayments = 1
	_, _, err := f.svc().CreateInstallmentPlan(context.Background(), f.userID, inp)
	require.Error(t, err)
	assert.Nil(t, f.createdParams, "nothing is created when the input is rejected")
}

func TestCreateInstallmentPlan_RejectsAlreadyConverted(t *testing.T) {
	f := newInstallmentFixture(t)
	existing := uuid.New()
	f.tx.InstallmentFixedExpenseID = &existing
	_, _, err := f.svc().CreateInstallmentPlan(context.Background(), f.userID, f.input())
	require.Error(t, err)
	assert.Nil(t, f.createdParams)
}

func TestCreateInstallmentPlan_RejectsFixedTransaction(t *testing.T) {
	f := newInstallmentFixture(t)
	fixedType := int32(1)
	f.tx.TransactionTypeID = &fixedType
	_, _, err := f.svc().CreateInstallmentPlan(context.Background(), f.userID, f.input())
	require.Error(t, err)
	assert.Nil(t, f.createdParams)
}

// A negative amount is money received (docs/features/negative-positive-transactions.md).
func TestCreateInstallmentPlan_RejectsReceivedAmount(t *testing.T) {
	f := newInstallmentFixture(t)
	f.tx.Amount = numeric(t, "-1000.00")
	_, _, err := f.svc().CreateInstallmentPlan(context.Background(), f.userID, f.input())
	require.Error(t, err)
	assert.Nil(t, f.createdParams)
}

func TestCreateInstallmentPlan_RejectsTransactionFromAnotherPeriod(t *testing.T) {
	f := newInstallmentFixture(t)
	other := uuid.New()
	f.tx.BudgetPeriodID = &other
	_, _, err := f.svc().CreateInstallmentPlan(context.Background(), f.userID, f.input())
	require.Error(t, err)
	assert.Nil(t, f.createdParams)
}

// A plan whose payments never appear on a bank feed must never be a review
// candidate — scoreBestMatch skips it regardless of how well it scores.
func TestScoreBestMatch_SkipsInstallmentPlans(t *testing.T) {
	pmID := uuid.New()
	catID := int32(7)
	installment := db.FixedExpense{
		ID:                uuid.New(),
		Name:              "Laptop",
		PlannedAmount:     numeric(t, "250.00"),
		PaymentMethodID:   &pmID,
		CategoryID:        &catID,
		IsInstallmentPlan: true,
	}
	score, best := scoreBestMatch("Laptop", 250.00, &catID, &pmID,
		[]db.FixedExpense{installment}, map[uuid.UUID][]string{})
	assert.Zero(t, score)
	assert.Nil(t, best, "an otherwise perfect match must still be skipped")
}

// ── DeleteInstallmentPlan ─────────────────────────────────────────────────────

func (f *installmentFixture) unsplitSvc(spawned []db.Transaction, deactivated *bool, deletedFor *uuid.UUID) *BudgetProfileService {
	return NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: f.periodID, BudgetProfileID: f.profileID, IsArchived: f.archived}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: f.profileID, UserID: f.userID}, nil
			},
		},
		&mockTransactionRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.Transaction, error) { return f.tx, nil },
			listByFixedExpense: func(_ context.Context, _ uuid.UUID) ([]db.Transaction, error) {
				return spawned, nil
			},
			deleteByFixedExpense: func(_ context.Context, id uuid.UUID) error {
				*deletedFor = id
				return nil
			},
			clearInstallmentPlan: func(_ context.Context, _ db.ClearTransactionInstallmentPlanParams) (db.Transaction, error) {
				out := f.tx
				out.IsExcluded = false
				out.InstallmentFixedExpenseID = nil
				return out, nil
			},
		},
		&mockFixedExpenseRepo{
			deactivate: func(_ context.Context, _ db.DeactivateFixedExpenseParams) error {
				*deactivated = true
				return nil
			},
		},
		&mockUserRepo{},
	)
}

func TestDeleteInstallmentPlan_RemovesPlanAndRestoresPurchase(t *testing.T) {
	f := newInstallmentFixture(t)
	f.tx.IsExcluded = true
	f.tx.InstallmentFixedExpenseID = &f.feID
	deactivated := false
	var deletedFor uuid.UUID

	tx, err := f.unsplitSvc(
		[]db.Transaction{{ID: uuid.New(), IsPaid: false}},
		&deactivated, &deletedFor,
	).DeleteInstallmentPlan(context.Background(), f.txID, f.periodID, f.userID)
	require.NoError(t, err)

	assert.False(t, tx.IsExcluded, "the purchase counts again")
	assert.Nil(t, tx.InstallmentFixedExpenseID)
	assert.Equal(t, f.feID, deletedFor, "the spawned payments go, or the purchase and its payments both count")
	assert.True(t, deactivated)
}

// Losing real recorded spend to an undo is worse than making the user unmark
// it first. It also stops this becoming a way to delete transactions out of an
// archived period, which nothing else permits.
func TestDeleteInstallmentPlan_RefusedOnceAPaymentIsPaid(t *testing.T) {
	f := newInstallmentFixture(t)
	f.tx.IsExcluded = true
	f.tx.InstallmentFixedExpenseID = &f.feID
	deactivated := false
	var deletedFor uuid.UUID

	_, err := f.unsplitSvc(
		[]db.Transaction{{ID: uuid.New(), IsPaid: false}, {ID: uuid.New(), IsPaid: true}},
		&deactivated, &deletedFor,
	).DeleteInstallmentPlan(context.Background(), f.txID, f.periodID, f.userID)
	require.Error(t, err)
	assert.False(t, deactivated, "nothing is touched when the undo is refused")
	assert.Equal(t, uuid.Nil, deletedFor)
}

// Undoing a split works on an archived period exactly as making one does.
func TestDeleteInstallmentPlan_AllowedOnArchivedPeriod(t *testing.T) {
	f := newInstallmentFixture(t)
	f.archived = true
	f.tx.IsExcluded = true
	f.tx.InstallmentFixedExpenseID = &f.feID
	deactivated := false
	var deletedFor uuid.UUID

	tx, err := f.unsplitSvc(nil, &deactivated, &deletedFor).
		DeleteInstallmentPlan(context.Background(), f.txID, f.periodID, f.userID)
	require.NoError(t, err)
	assert.False(t, tx.IsExcluded)
}

func TestDeleteInstallmentPlan_RejectsATransactionThatIsNotAPlan(t *testing.T) {
	f := newInstallmentFixture(t)
	deactivated := false
	var deletedFor uuid.UUID
	_, err := f.unsplitSvc(nil, &deactivated, &deletedFor).
		DeleteInstallmentPlan(context.Background(), f.txID, f.periodID, f.userID)
	require.Error(t, err)
	assert.False(t, deactivated)
}
