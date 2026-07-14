package service

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// ── Mock transaction repo ─────────────────────────────────────────────────────

type mockTransactionRepo struct {
	list                            func(context.Context, db.ListTransactionsParams) ([]db.Transaction, error)
	listFixedRecurring              func(context.Context, uuid.UUID) ([]db.Transaction, error)
	getByID                         func(context.Context, uuid.UUID) (db.Transaction, error)
	create                          func(context.Context, db.CreateTransactionParams) (db.Transaction, error)
	update                          func(context.Context, db.UpdateTransactionParams) (db.Transaction, error)
	delete                          func(context.Context, db.DeleteTransactionParams) error
	getCategory                     func(context.Context, int32) (db.GetCategoryRow, error)
	listCategories                  func(context.Context, uuid.UUID) ([]db.ListCategoriesRow, error)
	listCategoriesForBudget         func(context.Context, uuid.UUID, uuid.UUID) ([]db.ListCategoriesRow, error)
	createCategory                  func(context.Context, db.CreateCategoryParams) (db.CreateCategoryRow, error)
	updateCategory                  func(context.Context, db.UpdateCategoryParams) (db.UpdateCategoryRow, error)
	updateSystemCategoryColor       func(context.Context, db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error)
	deleteCategoryAndReassign       func(context.Context, db.DeleteCategoryAndReassignParams) error
	listPaymentMethods              func(context.Context, uuid.UUID) ([]db.ListPaymentMethodsRow, error)
	createPaymentMethod             func(context.Context, db.CreatePaymentMethodParams) (db.PaymentMethod, error)
	updatePaymentMethod             func(context.Context, db.UpdatePaymentMethodParams) (db.PaymentMethod, error)
	getPaymentMethod                func(context.Context, uuid.UUID) (db.PaymentMethod, error)
	deletePaymentMethodAndReassign  func(context.Context, db.DeletePaymentMethodAndReassignParams) error
	deleteSavingsSourceTransactions func(context.Context, db.DeleteSavingsSourceTransactionsParams) error
	markAsPaid                      func(context.Context, db.MarkTransactionAsPaidParams) (db.Transaction, error)
	unmarkAsPaid                    func(context.Context, db.UnmarkTransactionAsPaidParams) (db.Transaction, error)
}

// ── Mock fixed expense repo ───────────────────────────────────────────────────

type mockFixedExpenseRepo struct {
	create                     func(context.Context, db.CreateFixedExpenseParams) (db.FixedExpense, error)
	getByID                    func(context.Context, uuid.UUID) (db.FixedExpense, error)
	list                       func(context.Context, uuid.UUID) ([]db.FixedExpense, error)
	update                     func(context.Context, db.UpdateFixedExpenseParams) (db.FixedExpense, error)
	updatePlannedAmount        func(context.Context, db.UpdateFixedExpensePlannedAmountParams) error
	deactivate                 func(context.Context, db.DeactivateFixedExpenseParams) error
	getUnpaidTransaction       func(context.Context, db.GetUnpaidTransactionByFixedExpenseParams) (db.Transaction, error)
	deleteUnpaidTransactions   func(context.Context, db.DeleteUnpaidTransactionByFixedExpenseParams) error
	updateTransactionFromFixed func(context.Context, db.UpdateTransactionFromFixedExpenseParams) error
	hasTransactionInMonth      func(context.Context, db.FixedExpenseHasTransactionInMonthParams) (bool, error)
	hasTransactionOnDate       func(context.Context, db.FixedExpenseHasTransactionOnDateParams) (bool, error)
}

func (m *mockFixedExpenseRepo) Create(ctx context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
	if m.create != nil {
		return m.create(ctx, arg)
	}
	return db.FixedExpense{ID: uuid.New(), BudgetProfileID: arg.BudgetProfileID, Name: arg.Name}, nil
}
func (m *mockFixedExpenseRepo) GetByID(ctx context.Context, id uuid.UUID) (db.FixedExpense, error) {
	if m.getByID != nil {
		return m.getByID(ctx, id)
	}
	return db.FixedExpense{ID: id}, nil
}
func (m *mockFixedExpenseRepo) List(ctx context.Context, budgetProfileID uuid.UUID) ([]db.FixedExpense, error) {
	if m.list != nil {
		return m.list(ctx, budgetProfileID)
	}
	return nil, nil
}
func (m *mockFixedExpenseRepo) Update(ctx context.Context, arg db.UpdateFixedExpenseParams) (db.FixedExpense, error) {
	if m.update != nil {
		return m.update(ctx, arg)
	}
	return db.FixedExpense{ID: arg.ID, Name: arg.Name}, nil
}
func (m *mockFixedExpenseRepo) UpdatePlannedAmount(ctx context.Context, arg db.UpdateFixedExpensePlannedAmountParams) error {
	if m.updatePlannedAmount != nil {
		return m.updatePlannedAmount(ctx, arg)
	}
	return nil
}
func (m *mockFixedExpenseRepo) Deactivate(ctx context.Context, arg db.DeactivateFixedExpenseParams) error {
	if m.deactivate != nil {
		return m.deactivate(ctx, arg)
	}
	return nil
}
func (m *mockFixedExpenseRepo) GetUnpaidTransaction(ctx context.Context, arg db.GetUnpaidTransactionByFixedExpenseParams) (db.Transaction, error) {
	if m.getUnpaidTransaction != nil {
		return m.getUnpaidTransaction(ctx, arg)
	}
	return db.Transaction{}, nil
}
func (m *mockFixedExpenseRepo) DeleteUnpaidTransactions(ctx context.Context, arg db.DeleteUnpaidTransactionByFixedExpenseParams) error {
	if m.deleteUnpaidTransactions != nil {
		return m.deleteUnpaidTransactions(ctx, arg)
	}
	return nil
}
func (m *mockFixedExpenseRepo) UpdateTransactionFromFixedExpense(ctx context.Context, arg db.UpdateTransactionFromFixedExpenseParams) error {
	if m.updateTransactionFromFixed != nil {
		return m.updateTransactionFromFixed(ctx, arg)
	}
	return nil
}
func (m *mockFixedExpenseRepo) HasTransactionInMonth(ctx context.Context, arg db.FixedExpenseHasTransactionInMonthParams) (bool, error) {
	if m.hasTransactionInMonth != nil {
		return m.hasTransactionInMonth(ctx, arg)
	}
	return false, nil
}
func (m *mockFixedExpenseRepo) HasTransactionOnDate(ctx context.Context, arg db.FixedExpenseHasTransactionOnDateParams) (bool, error) {
	if m.hasTransactionOnDate != nil {
		return m.hasTransactionOnDate(ctx, arg)
	}
	return false, nil
}

// ── Mock expense allocation repo ──────────────────────────────────────────────

type mockExpenseAllocationRepo struct {
	list   func(context.Context, uuid.UUID) ([]db.ExpenseAllocation, error)
	upsert func(context.Context, db.UpsertExpenseAllocationParams) (db.ExpenseAllocation, error)
	del    func(context.Context, db.DeleteExpenseAllocationParams) error
}

func (m *mockExpenseAllocationRepo) List(ctx context.Context, profileID uuid.UUID) ([]db.ExpenseAllocation, error) {
	if m.list != nil {
		return m.list(ctx, profileID)
	}
	return nil, nil
}
func (m *mockExpenseAllocationRepo) Upsert(ctx context.Context, arg db.UpsertExpenseAllocationParams) (db.ExpenseAllocation, error) {
	if m.upsert != nil {
		return m.upsert(ctx, arg)
	}
	return db.ExpenseAllocation{}, nil
}
func (m *mockExpenseAllocationRepo) Delete(ctx context.Context, arg db.DeleteExpenseAllocationParams) error {
	if m.del != nil {
		return m.del(ctx, arg)
	}
	return nil
}

func (m *mockTransactionRepo) List(ctx context.Context, arg db.ListTransactionsParams) ([]db.Transaction, error) {
	if m.list != nil {
		return m.list(ctx, arg)
	}
	return nil, nil
}
func (m *mockTransactionRepo) ListFixedRecurring(ctx context.Context, id uuid.UUID) ([]db.Transaction, error) {
	if m.listFixedRecurring != nil {
		return m.listFixedRecurring(ctx, id)
	}
	return nil, nil
}
func (m *mockTransactionRepo) GetByID(ctx context.Context, id uuid.UUID) (db.Transaction, error) {
	if m.getByID != nil {
		return m.getByID(ctx, id)
	}
	return db.Transaction{}, apperr.NotFound("transaction", id.String())
}
func (m *mockTransactionRepo) Create(ctx context.Context, arg db.CreateTransactionParams) (db.Transaction, error) {
	if m.create != nil {
		return m.create(ctx, arg)
	}
	return db.Transaction{}, nil
}
func (m *mockTransactionRepo) Update(ctx context.Context, arg db.UpdateTransactionParams) (db.Transaction, error) {
	if m.update != nil {
		return m.update(ctx, arg)
	}
	return db.Transaction{}, nil
}
func (m *mockTransactionRepo) Delete(ctx context.Context, arg db.DeleteTransactionParams) error {
	if m.delete != nil {
		return m.delete(ctx, arg)
	}
	return nil
}
func (m *mockTransactionRepo) GetCategory(ctx context.Context, id int32) (db.GetCategoryRow, error) {
	if m.getCategory != nil {
		return m.getCategory(ctx, id)
	}
	return db.GetCategoryRow{}, apperr.NotFound("category", "0")
}
func (m *mockTransactionRepo) ListCategories(ctx context.Context, userID uuid.UUID) ([]db.ListCategoriesRow, error) {
	if m.listCategories != nil {
		return m.listCategories(ctx, userID)
	}
	return nil, nil
}
func (m *mockTransactionRepo) ListCategoriesForBudget(ctx context.Context, userID uuid.UUID, budgetProfileID uuid.UUID) ([]db.ListCategoriesRow, error) {
	if m.listCategoriesForBudget != nil {
		return m.listCategoriesForBudget(ctx, userID, budgetProfileID)
	}
	return nil, nil
}
func (m *mockTransactionRepo) CreateCategory(ctx context.Context, arg db.CreateCategoryParams) (db.CreateCategoryRow, error) {
	if m.createCategory != nil {
		return m.createCategory(ctx, arg)
	}
	return db.CreateCategoryRow{}, nil
}
func (m *mockTransactionRepo) UpdateCategory(ctx context.Context, arg db.UpdateCategoryParams) (db.UpdateCategoryRow, error) {
	if m.updateCategory != nil {
		return m.updateCategory(ctx, arg)
	}
	return db.UpdateCategoryRow{}, nil
}
func (m *mockTransactionRepo) UpdateSystemCategoryColor(ctx context.Context, arg db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error) {
	if m.updateSystemCategoryColor != nil {
		return m.updateSystemCategoryColor(ctx, arg)
	}
	return db.UpdateSystemCategoryColorRow{}, nil
}
func (m *mockTransactionRepo) DeleteCategoryAndReassign(ctx context.Context, arg db.DeleteCategoryAndReassignParams) error {
	if m.deleteCategoryAndReassign != nil {
		return m.deleteCategoryAndReassign(ctx, arg)
	}
	return nil
}
func (m *mockTransactionRepo) ListPaymentMethods(ctx context.Context, id uuid.UUID) ([]db.ListPaymentMethodsRow, error) {
	if m.listPaymentMethods != nil {
		return m.listPaymentMethods(ctx, id)
	}
	return nil, nil
}
func (m *mockTransactionRepo) CreatePaymentMethod(ctx context.Context, arg db.CreatePaymentMethodParams) (db.PaymentMethod, error) {
	if m.createPaymentMethod != nil {
		return m.createPaymentMethod(ctx, arg)
	}
	return db.PaymentMethod{}, nil
}
func (m *mockTransactionRepo) UpdatePaymentMethod(ctx context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error) {
	if m.updatePaymentMethod != nil {
		return m.updatePaymentMethod(ctx, arg)
	}
	return db.PaymentMethod{}, nil
}
func (m *mockTransactionRepo) GetPaymentMethod(ctx context.Context, id uuid.UUID) (db.PaymentMethod, error) {
	if m.getPaymentMethod != nil {
		return m.getPaymentMethod(ctx, id)
	}
	return db.PaymentMethod{}, nil
}
func (m *mockTransactionRepo) DeletePaymentMethodAndReassign(ctx context.Context, arg db.DeletePaymentMethodAndReassignParams) error {
	if m.deletePaymentMethodAndReassign != nil {
		return m.deletePaymentMethodAndReassign(ctx, arg)
	}
	return nil
}
func (m *mockTransactionRepo) DeleteSavingsSourceTransactions(ctx context.Context, arg db.DeleteSavingsSourceTransactionsParams) error {
	if m.deleteSavingsSourceTransactions != nil {
		return m.deleteSavingsSourceTransactions(ctx, arg)
	}
	return nil
}
func (m *mockTransactionRepo) MarkAsPaid(ctx context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
	if m.markAsPaid != nil {
		return m.markAsPaid(ctx, arg)
	}
	return db.Transaction{}, nil
}

func (m *mockTransactionRepo) UnmarkAsPaid(ctx context.Context, arg db.UnmarkTransactionAsPaidParams) (db.Transaction, error) {
	if m.unmarkAsPaid != nil {
		return m.unmarkAsPaid(ctx, arg)
	}
	return db.Transaction{}, nil
}

func (m *mockTransactionRepo) CreateTransactionFromPlaid(ctx context.Context, arg db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
	return db.Transaction{}, nil
}

func (m *mockTransactionRepo) ExistsTransactionByPlaidID(ctx context.Context, plaidTransactionID *string) (bool, error) {
	return false, nil
}

func (m *mockTransactionRepo) UpdateTransactionFromPlaid(ctx context.Context, arg db.UpdateTransactionFromPlaidParams) error {
	return nil
}

func (m *mockTransactionRepo) DeleteTransactionByPlaidID(ctx context.Context, plaidTransactionID *string) error {
	return nil
}

func (m *mockTransactionRepo) CreatePaymentMethodFromPlaid(ctx context.Context, arg db.CreatePaymentMethodFromPlaidParams) (db.PaymentMethod, error) {
	return db.PaymentMethod{}, nil
}

func (m *mockTransactionRepo) GetPaymentMethodByPlaidAccountID(ctx context.Context, plaidAccountID string) (db.PaymentMethod, error) {
	return db.PaymentMethod{}, apperr.NotFound("payment_method", plaidAccountID)
}

func (m *mockTransactionRepo) GetPaymentMethodByUserAndName(_ context.Context, _ uuid.UUID, _ string) (db.PaymentMethod, error) {
	return db.PaymentMethod{}, apperr.NotFound("payment_method", "name")
}

func (m *mockTransactionRepo) UpdatePaymentMethodPlaidAccountID(_ context.Context, _ uuid.UUID, _ string) error {
	return nil
}

// ── mockTransactionReviewRepo ─────────────────────────────────────────────────

type mockTransactionReviewRepo struct{}

func (m *mockTransactionReviewRepo) Create(_ context.Context, _, _, _ uuid.UUID, _ float64) (db.TransactionReview, error) {
	return db.TransactionReview{}, nil
}
func (m *mockTransactionReviewRepo) ListPending(_ context.Context, _ uuid.UUID) ([]db.ListPendingTransactionReviewsRow, error) {
	return nil, nil
}
func (m *mockTransactionReviewRepo) GetByID(_ context.Context, id uuid.UUID) (db.TransactionReview, error) {
	return db.TransactionReview{}, apperr.NotFound("transaction_review", id.String())
}
func (m *mockTransactionReviewRepo) UpdateStatus(_ context.Context, _ uuid.UUID, _ string) error {
	return nil
}
func (m *mockTransactionReviewRepo) GetConfirmedByFixedExpenseAndPeriod(_ context.Context, _, _ uuid.UUID) (db.TransactionReview, error) {
	return db.TransactionReview{}, apperr.NotFound("transaction_review", "")
}
func (m *mockTransactionReviewRepo) ResetByFixedExpenseAndPeriod(_ context.Context, _, _ uuid.UUID) error {
	return nil
}
func (m *mockTransactionReviewRepo) CreateAlias(_ context.Context, _ uuid.UUID, _ string) error {
	return nil
}
func (m *mockTransactionReviewRepo) DeleteAlias(_ context.Context, _ uuid.UUID, _ string) error {
	return nil
}
func (m *mockTransactionReviewRepo) ListAliases(_ context.Context, _ uuid.UUID) ([]string, error) {
	return nil, nil
}
func (m *mockTransactionReviewRepo) Upsert(_ context.Context, _, _, _ uuid.UUID, _ float64) (db.TransactionReview, error) {
	return db.TransactionReview{}, nil
}
func (m *mockTransactionReviewRepo) GetFixedExpenseByAlias(_ context.Context, _ string, _ uuid.UUID) (db.GetFixedExpenseByAliasRow, error) {
	return db.GetFixedExpenseByAliasRow{}, apperr.NotFound("fixed_expense_alias", "")
}

// ── UpdatePaymentMethod tests ─────────────────────────────────────────────────

func TestUpdatePaymentMethod_Success(t *testing.T) {
	typeID := int32(2) // CREDIT
	methodID := uuid.New()
	userID := uuid.New()
	expected := db.PaymentMethod{
		ID:            methodID,
		Name:          "Chase Visa",
		PaymentTypeID: &typeID,
		UserID:        &userID,
	}

	svc := NewTransactionService(
		&mockTransactionRepo{
			updatePaymentMethod: func(_ context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error) {
				assert.Equal(t, methodID, arg.ID)
				assert.Equal(t, userID, arg.UserID)
				assert.Equal(t, "Chase Visa", arg.Name)
				return expected, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	result, err := svc.UpdatePaymentMethod(context.Background(), db.UpdatePaymentMethodParams{
		ID:     methodID,
		Name:   "Chase Visa",
		UserID: userID,
	})

	require.NoError(t, err)
	assert.Equal(t, expected.ID, result.ID)
	assert.Equal(t, expected.Name, result.Name)
	assert.Equal(t, expected.PaymentTypeID, result.PaymentTypeID)
}

func TestUpdatePaymentMethod_NotFound_WhenUserDoesNotOwnMethod(t *testing.T) {
	svc := NewTransactionService(
		&mockTransactionRepo{
			updatePaymentMethod: func(_ context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error) {
				return db.PaymentMethod{}, apperr.NotFound("payment_method", arg.ID.String())
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.UpdatePaymentMethod(context.Background(), db.UpdatePaymentMethodParams{
		ID:     uuid.New(),
		Name:   "Renamed",
		UserID: uuid.New(),
	})

	require.Error(t, err)
	var notFound *apperr.NotFoundError
	require.ErrorAs(t, err, &notFound)
	assert.Equal(t, "payment_method", notFound.Resource)
}

// ── CreateCategory tests ──────────────────────────────────────────────────────

func TestCreateCategory_Success(t *testing.T) {
	userID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			createCategory: func(_ context.Context, arg db.CreateCategoryParams) (db.CreateCategoryRow, error) {
				assert.Equal(t, "Hobbies", arg.Name)
				assert.Equal(t, userID, arg.UserID)
				return db.CreateCategoryRow{ID: 42, Name: "Hobbies", IsSystem: false}, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	result, err := svc.CreateCategory(context.Background(), db.CreateCategoryParams{
		Name:   "Hobbies",
		UserID: userID,
	})

	require.NoError(t, err)
	assert.Equal(t, int32(42), result.ID)
	assert.Equal(t, "Hobbies", result.Name)
	assert.False(t, result.IsSystem)
}

// ── UpdateCategory tests ──────────────────────────────────────────────────────

func TestUpdateCategory_Success(t *testing.T) {
	userID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getCategory: func(_ context.Context, id int32) (db.GetCategoryRow, error) {
				return db.GetCategoryRow{ID: id, IsSystem: false}, nil
			},
			updateCategory: func(_ context.Context, arg db.UpdateCategoryParams) (db.UpdateCategoryRow, error) {
				assert.Equal(t, int32(10), arg.ID)
				assert.Equal(t, "Fun Money", arg.Name)
				assert.Equal(t, userID, arg.UserID)
				return db.UpdateCategoryRow{ID: 10, Name: "Fun Money", IsSystem: false}, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	result, err := svc.UpdateCategory(context.Background(), db.UpdateCategoryParams{
		ID:     10,
		Name:   "Fun Money",
		UserID: userID,
	})

	require.NoError(t, err)
	assert.Equal(t, "Fun Money", result.Name)
}

func TestUpdateCategory_NotFound(t *testing.T) {
	svc := NewTransactionService(
		&mockTransactionRepo{
			// getCategory returns NotFound (default) — service returns early
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.UpdateCategory(context.Background(), db.UpdateCategoryParams{
		ID:     99,
		Name:   "Ghost",
		UserID: uuid.New(),
	})

	require.Error(t, err)
	var notFound *apperr.NotFoundError
	require.ErrorAs(t, err, &notFound)
	assert.Equal(t, "category", notFound.Resource)
}

func TestUpdateCategory_SystemColor_Success(t *testing.T) {
	svc := NewTransactionService(
		&mockTransactionRepo{
			getCategory: func(_ context.Context, id int32) (db.GetCategoryRow, error) {
				return db.GetCategoryRow{ID: id, Name: "Food", IsSystem: true}, nil
			},
			updateSystemCategoryColor: func(_ context.Context, arg db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error) {
				assert.Equal(t, int32(5), arg.ID)
				assert.Equal(t, "#4caf50", arg.Color)
				return db.UpdateSystemCategoryColorRow{ID: 5, Name: "Food", IsSystem: true, Color: "#4caf50"}, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	result, err := svc.UpdateCategory(context.Background(), db.UpdateCategoryParams{
		ID:    5,
		Name:  "Food", // name is ignored for system categories
		Color: "#4caf50",
	})

	require.NoError(t, err)
	assert.Equal(t, "Food", result.Name)
	assert.Equal(t, "#4caf50", result.Color)
	assert.True(t, result.IsSystem)
}

func TestUpdateCategory_SystemColor_NotFound(t *testing.T) {
	svc := NewTransactionService(
		&mockTransactionRepo{
			getCategory: func(_ context.Context, id int32) (db.GetCategoryRow, error) {
				return db.GetCategoryRow{ID: id, IsSystem: true}, nil
			},
			updateSystemCategoryColor: func(_ context.Context, arg db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error) {
				return db.UpdateSystemCategoryColorRow{}, apperr.NotFound("category", "99")
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.UpdateCategory(context.Background(), db.UpdateCategoryParams{
		ID:    99,
		Color: "#ff0000",
	})

	require.Error(t, err)
	var notFound *apperr.NotFoundError
	require.ErrorAs(t, err, &notFound)
}

// ── DeleteCategory tests ──────────────────────────────────────────────────────

func TestDeleteCategory_Success(t *testing.T) {
	userID := uuid.New()
	catID := int32(5)
	replacementID := int32(1)

	svc := NewTransactionService(
		&mockTransactionRepo{
			getCategory: func(_ context.Context, id int32) (db.GetCategoryRow, error) {
				if id == catID {
					return db.GetCategoryRow{ID: catID, Name: "Old", IsSystem: false, UserID: &userID}, nil
				}
				return db.GetCategoryRow{ID: replacementID, Name: "Entertainment", IsSystem: true}, nil
			},
			deleteCategoryAndReassign: func(_ context.Context, arg db.DeleteCategoryAndReassignParams) error {
				assert.Equal(t, catID, arg.ID)
				assert.Equal(t, userID, arg.UserID)
				assert.Equal(t, &replacementID, arg.ReplacementID)
				return nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeleteCategory(context.Background(), catID, replacementID, userID)
	require.NoError(t, err)
}

func TestDeleteCategory_Forbidden_WhenSystemCategory(t *testing.T) {
	userID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getCategory: func(_ context.Context, id int32) (db.GetCategoryRow, error) {
				return db.GetCategoryRow{ID: id, Name: "Entertainment", IsSystem: true}, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeleteCategory(context.Background(), 1, 2, userID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestDeleteCategory_Forbidden_WhenNotOwner(t *testing.T) {
	userID := uuid.New()
	otherUserID := uuid.New()
	catID := int32(5)

	svc := NewTransactionService(
		&mockTransactionRepo{
			getCategory: func(_ context.Context, id int32) (db.GetCategoryRow, error) {
				return db.GetCategoryRow{ID: catID, Name: "Old", IsSystem: false, UserID: &otherUserID}, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeleteCategory(context.Background(), catID, 1, userID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestDeleteCategory_NotFound_WhenCategoryMissing(t *testing.T) {
	svc := NewTransactionService(
		&mockTransactionRepo{
			getCategory: func(_ context.Context, id int32) (db.GetCategoryRow, error) {
				return db.GetCategoryRow{}, apperr.NotFound("category", "99")
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeleteCategory(context.Background(), 99, 1, uuid.New())
	require.Error(t, err)
	var notFound *apperr.NotFoundError
	require.ErrorAs(t, err, &notFound)
}

// ── DeletePaymentMethod tests ─────────────────────────────────────────────────

func TestDeletePaymentMethod_Success(t *testing.T) {
	userID := uuid.New()
	methodID := uuid.New()
	replacementID := uuid.New()
	profileID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				return db.PaymentMethod{ID: id, Name: "method", UserID: &userID}, nil
			},
			deletePaymentMethodAndReassign: func(_ context.Context, arg db.DeletePaymentMethodAndReassignParams) error {
				assert.Equal(t, methodID, arg.ID)
				assert.Equal(t, userID, arg.UserID)
				assert.Equal(t, replacementID, arg.ReplacementID)
				assert.Equal(t, profileID, arg.BudgetProfileID)
				return nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeletePaymentMethod(context.Background(), methodID, replacementID, profileID, userID)
	require.NoError(t, err)
}

func TestDeletePaymentMethod_Forbidden_WhenNotOwner(t *testing.T) {
	userID := uuid.New()
	otherUserID := uuid.New()
	methodID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				return db.PaymentMethod{ID: id, Name: "method", UserID: &otherUserID}, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeletePaymentMethod(context.Background(), methodID, uuid.New(), uuid.New(), userID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestDeletePaymentMethod_Forbidden_WhenReplacementNotOwned(t *testing.T) {
	userID := uuid.New()
	otherUserID := uuid.New()
	methodID := uuid.New()
	replacementID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				if id == methodID {
					return db.PaymentMethod{ID: id, Name: "mine", UserID: &userID}, nil
				}
				return db.PaymentMethod{ID: id, Name: "other", UserID: &otherUserID}, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeletePaymentMethod(context.Background(), methodID, replacementID, uuid.New(), userID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestDeletePaymentMethod_NotFound_WhenMethodMissing(t *testing.T) {
	svc := NewTransactionService(
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				return db.PaymentMethod{}, apperr.NotFound("payment_method", id.String())
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeletePaymentMethod(context.Background(), uuid.New(), uuid.New(), uuid.New(), uuid.New())
	require.Error(t, err)
	var notFound *apperr.NotFoundError
	require.ErrorAs(t, err, &notFound)
}

// ── UnmarkTransactionAsPaid tests ─────────────────────────────────────────────

func TestUnmarkTransactionAsPaid_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	txID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			unmarkAsPaid: func(_ context.Context, arg db.UnmarkTransactionAsPaidParams) (db.Transaction, error) {
				assert.Equal(t, txID, arg.ID)
				assert.Equal(t, periodID, arg.BudgetPeriodID)
				return db.Transaction{ID: txID, IsPaid: false}, nil
			},
		},
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: id, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	tx, err := svc.UnmarkTransactionAsPaid(context.Background(), txID, periodID, userID)
	require.NoError(t, err)
	assert.Equal(t, txID, tx.ID)
	assert.False(t, tx.IsPaid)
}

func TestUnmarkTransactionAsPaid_Forbidden_WhenNotOwner(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{},
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: id, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: uuid.New()}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.UnmarkTransactionAsPaid(context.Background(), uuid.New(), periodID, uuid.New())
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

// ── Role-based transaction access tests ──────────────────────────────────────

func TestListTransactions_CollaboratorAllowed(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return []db.Transaction{{ID: uuid.New()}}, nil
			},
		},
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: id, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: uuid.New()}, nil // caller is not owner
			},
			getPersonByUserID: func(_ context.Context, _, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{Role: "collaborator"}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	txs, err := svc.List(context.Background(), db.ListTransactionsParams{BudgetPeriodID: periodID}, userID)
	require.NoError(t, err)
	assert.Len(t, txs, 1)
}

func TestListTransactions_ViewerAllowed(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return []db.Transaction{}, nil
			},
		},
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: id, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: uuid.New()}, nil
			},
			getPersonByUserID: func(_ context.Context, _, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{Role: "viewer"}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.List(context.Background(), db.ListTransactionsParams{BudgetPeriodID: periodID}, userID)
	require.NoError(t, err)
}

func TestCreateTransaction_CollaboratorAllowed(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			create: func(_ context.Context, _ db.CreateTransactionParams) (db.Transaction, error) {
				return db.Transaction{ID: uuid.New()}, nil
			},
		},
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: id, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: uuid.New()}, nil
			},
			getPersonByUserID: func(_ context.Context, _, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{Role: "collaborator"}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.Create(context.Background(), db.CreateTransactionParams{BudgetPeriodID: &periodID}, userID)
	require.NoError(t, err)
}

func TestCreateTransaction_ViewerForbidden(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{},
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: id, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: uuid.New()}, nil
			},
			getPersonByUserID: func(_ context.Context, _, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{Role: "viewer"}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.Create(context.Background(), db.CreateTransactionParams{BudgetPeriodID: &periodID}, userID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

// ── MarkTransactionForReview tests ────────────────────────────────────────────

func TestMarkTransactionForReview_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	txID := uuid.New()
	feID := uuid.New()
	typeID := int32(2) // Variable

	svc := NewTransactionService(
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				return db.Transaction{ID: id, TransactionTypeID: &typeID, BudgetPeriodID: &periodID}, nil
			},
		},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.FixedExpense, error) {
				return db.FixedExpense{ID: id, BudgetProfileID: profileID}, nil
			},
		},
		&mockTransactionReviewRepo{},
	)

	review, err := svc.MarkTransactionForReview(context.Background(), userID, txID, feID, profileID)
	require.NoError(t, err)
	assert.Equal(t, db.TransactionReview{}, review) // mock returns zero value
}

func TestMarkTransactionForReview_Forbidden_WhenViewer(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: uuid.New()}, nil
			},
			getPersonByUserID: func(_ context.Context, _, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{Role: "viewer"}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.MarkTransactionForReview(context.Background(), userID, uuid.New(), uuid.New(), profileID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestMarkTransactionForReview_Invalid_WhenFixed(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	typeID := int32(1) // Fixed

	svc := NewTransactionService(
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				return db.Transaction{ID: id, TransactionTypeID: &typeID, BudgetPeriodID: &periodID}, nil
			},
		},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.MarkTransactionForReview(context.Background(), userID, uuid.New(), uuid.New(), profileID)
	require.Error(t, err)
	var invalid *apperr.ValidationError
	require.ErrorAs(t, err, &invalid)
}

func TestMarkTransactionForReview_Forbidden_WhenFixedExpenseOtherBudget(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	typeID := int32(2)
	otherProfile := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				return db.Transaction{ID: id, TransactionTypeID: &typeID, BudgetPeriodID: &periodID}, nil
			},
		},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.FixedExpense, error) {
				return db.FixedExpense{ID: id, BudgetProfileID: otherProfile}, nil
			},
		},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.MarkTransactionForReview(context.Background(), userID, uuid.New(), uuid.New(), profileID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}
