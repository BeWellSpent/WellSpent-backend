package service

import (
	"context"
	"testing"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// ── Mock transaction repo ─────────────────────────────────────────────────────

type mockTransactionRepo struct {
	list                                func(context.Context, db.ListTransactionsParams) ([]db.Transaction, error)
	listFixedRecurring                  func(context.Context, uuid.UUID) ([]db.Transaction, error)
	getByID                             func(context.Context, uuid.UUID) (db.Transaction, error)
	create                              func(context.Context, db.CreateTransactionParams) (db.Transaction, error)
	update                              func(context.Context, db.UpdateTransactionParams) (db.Transaction, error)
	delete                              func(context.Context, db.DeleteTransactionParams) error
	getCategory                         func(context.Context, int32) (db.GetCategoryRow, error)
	listCategories                      func(context.Context, uuid.UUID) ([]db.ListCategoriesRow, error)
	listCategoriesForBudget             func(context.Context, uuid.UUID, uuid.UUID) ([]db.ListCategoriesRow, error)
	createCategory                      func(context.Context, db.CreateCategoryParams) (db.CreateCategoryRow, error)
	updateCategory                      func(context.Context, db.UpdateCategoryParams) (db.UpdateCategoryRow, error)
	updateSystemCategoryColor           func(context.Context, db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error)
	deleteCategoryAndReassign           func(context.Context, db.DeleteCategoryAndReassignParams) error
	listPaymentMethods                  func(context.Context, uuid.UUID) ([]db.ListPaymentMethodsRow, error)
	createPaymentMethod                 func(context.Context, db.CreatePaymentMethodParams) (db.PaymentMethod, error)
	updatePaymentMethod                 func(context.Context, db.UpdatePaymentMethodParams) (db.PaymentMethod, error)
	getPaymentMethod                    func(context.Context, uuid.UUID) (db.PaymentMethod, error)
	deletePaymentMethodAndReassign      func(context.Context, db.DeletePaymentMethodAndReassignParams) error
	deleteSavingsSourceTransactions     func(context.Context, db.DeleteSavingsSourceTransactionsParams) error
	markAsPaid                          func(context.Context, db.MarkTransactionAsPaidParams) (db.Transaction, error)
	unmarkAsPaid                        func(context.Context, db.UnmarkTransactionAsPaidParams) (db.Transaction, error)
	setExcluded                         func(context.Context, db.SetTransactionExcludedParams) (db.Transaction, error)
	createPaymentMethodFromPlaid        func(context.Context, db.CreatePaymentMethodFromPlaidParams) (db.PaymentMethod, error)
	getPaymentMethodByPlaidAccountID    func(context.Context, string) (db.PaymentMethod, error)
	getPaymentMethodByUserAndName       func(context.Context, uuid.UUID, string) (db.PaymentMethod, error)
	updatePaymentMethodPlaidAccountID   func(context.Context, uuid.UUID, string) error
	listActivePaymentMethodsByPlaidItem func(context.Context, uuid.UUID) ([]db.PaymentMethod, error)
	deactivatePaymentMethod             func(context.Context, uuid.UUID) error
	createTransactionFromPlaid          func(context.Context, db.CreateTransactionFromPlaidParams) (db.Transaction, error)
	existsTransactionByPlaidID          func(context.Context, *string) (bool, error)
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

func (m *mockTransactionRepo) SetExcluded(ctx context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
	if m.setExcluded != nil {
		return m.setExcluded(ctx, arg)
	}
	return db.Transaction{}, nil
}

func (m *mockTransactionRepo) CreateTransactionFromPlaid(ctx context.Context, arg db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
	if m.createTransactionFromPlaid != nil {
		return m.createTransactionFromPlaid(ctx, arg)
	}
	return db.Transaction{}, nil
}

func (m *mockTransactionRepo) ExistsTransactionByPlaidID(ctx context.Context, plaidTransactionID *string) (bool, error) {
	if m.existsTransactionByPlaidID != nil {
		return m.existsTransactionByPlaidID(ctx, plaidTransactionID)
	}
	return false, nil
}

func (m *mockTransactionRepo) UpdateTransactionFromPlaid(ctx context.Context, arg db.UpdateTransactionFromPlaidParams) error {
	return nil
}

func (m *mockTransactionRepo) DeleteTransactionByPlaidID(ctx context.Context, plaidTransactionID *string) error {
	return nil
}

func (m *mockTransactionRepo) CreatePaymentMethodFromPlaid(ctx context.Context, arg db.CreatePaymentMethodFromPlaidParams) (db.PaymentMethod, error) {
	if m.createPaymentMethodFromPlaid != nil {
		return m.createPaymentMethodFromPlaid(ctx, arg)
	}
	return db.PaymentMethod{}, nil
}

func (m *mockTransactionRepo) GetPaymentMethodByPlaidAccountID(ctx context.Context, plaidAccountID string) (db.PaymentMethod, error) {
	if m.getPaymentMethodByPlaidAccountID != nil {
		return m.getPaymentMethodByPlaidAccountID(ctx, plaidAccountID)
	}
	return db.PaymentMethod{}, apperr.NotFound("payment_method", plaidAccountID)
}

func (m *mockTransactionRepo) GetPaymentMethodByUserAndName(ctx context.Context, userID uuid.UUID, name string) (db.PaymentMethod, error) {
	if m.getPaymentMethodByUserAndName != nil {
		return m.getPaymentMethodByUserAndName(ctx, userID, name)
	}
	return db.PaymentMethod{}, apperr.NotFound("payment_method", "name")
}

func (m *mockTransactionRepo) UpdatePaymentMethodPlaidAccountID(ctx context.Context, id uuid.UUID, plaidAccountID string) error {
	if m.updatePaymentMethodPlaidAccountID != nil {
		return m.updatePaymentMethodPlaidAccountID(ctx, id, plaidAccountID)
	}
	return nil
}

func (m *mockTransactionRepo) ListActivePaymentMethodsByPlaidItem(ctx context.Context, plaidItemID uuid.UUID) ([]db.PaymentMethod, error) {
	if m.listActivePaymentMethodsByPlaidItem != nil {
		return m.listActivePaymentMethodsByPlaidItem(ctx, plaidItemID)
	}
	return nil, nil
}

func (m *mockTransactionRepo) DeactivatePaymentMethod(ctx context.Context, id uuid.UUID) error {
	if m.deactivatePaymentMethod != nil {
		return m.deactivatePaymentMethod(ctx, id)
	}
	return nil
}

func (m *mockTransactionRepo) ListSystemCategories(_ context.Context) (map[string]int32, error) {
	return map[string]int32{}, nil
}

// ── mockTransactionReviewRepo ─────────────────────────────────────────────────

type mockTransactionReviewRepo struct {
	create                  func(context.Context, uuid.UUID, uuid.UUID, uuid.UUID, float64) (db.TransactionReview, error)
	upsert                  func(context.Context, uuid.UUID, uuid.UUID, uuid.UUID, float64) (db.TransactionReview, error)
	list                    func(context.Context, uuid.UUID) ([]db.ListTransactionReviewsRow, error)
	getByID                 func(context.Context, uuid.UUID) (db.TransactionReview, error)
	updateStatus            func(context.Context, uuid.UUID, string) error
	getConfirmedByMatchedTx func(context.Context, uuid.UUID) (db.TransactionReview, error)
	resetByMatchedTx        func(context.Context, uuid.UUID) error
	createAlias             func(context.Context, uuid.UUID, string) error
	deleteAlias             func(context.Context, uuid.UUID, string) error
	listAliases             func(context.Context, uuid.UUID) ([]string, error)
	getFixedExpenseByAlias  func(context.Context, string, uuid.UUID) (db.GetFixedExpenseByAliasRow, error)
}

func (m *mockTransactionReviewRepo) Create(ctx context.Context, periodID, transactionID, matchedTransactionID uuid.UUID, score float64) (db.TransactionReview, error) {
	if m.create != nil {
		return m.create(ctx, periodID, transactionID, matchedTransactionID, score)
	}
	return db.TransactionReview{}, nil
}
func (m *mockTransactionReviewRepo) List(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListTransactionReviewsRow, error) {
	if m.list != nil {
		return m.list(ctx, budgetProfileID)
	}
	return nil, nil
}
func (m *mockTransactionReviewRepo) GetByID(ctx context.Context, id uuid.UUID) (db.TransactionReview, error) {
	if m.getByID != nil {
		return m.getByID(ctx, id)
	}
	return db.TransactionReview{}, apperr.NotFound("transaction_review", id.String())
}
func (m *mockTransactionReviewRepo) UpdateStatus(ctx context.Context, id uuid.UUID, status string) error {
	if m.updateStatus != nil {
		return m.updateStatus(ctx, id, status)
	}
	return nil
}
func (m *mockTransactionReviewRepo) GetConfirmedByMatchedTransaction(ctx context.Context, matchedTransactionID uuid.UUID) (db.TransactionReview, error) {
	if m.getConfirmedByMatchedTx != nil {
		return m.getConfirmedByMatchedTx(ctx, matchedTransactionID)
	}
	return db.TransactionReview{}, apperr.NotFound("transaction_review", "")
}
func (m *mockTransactionReviewRepo) ResetByMatchedTransaction(ctx context.Context, matchedTransactionID uuid.UUID) error {
	if m.resetByMatchedTx != nil {
		return m.resetByMatchedTx(ctx, matchedTransactionID)
	}
	return nil
}
func (m *mockTransactionReviewRepo) CreateAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error {
	if m.createAlias != nil {
		return m.createAlias(ctx, fixedExpenseID, alias)
	}
	return nil
}
func (m *mockTransactionReviewRepo) DeleteAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error {
	if m.deleteAlias != nil {
		return m.deleteAlias(ctx, fixedExpenseID, alias)
	}
	return nil
}
func (m *mockTransactionReviewRepo) ListAliases(ctx context.Context, fixedExpenseID uuid.UUID) ([]string, error) {
	if m.listAliases != nil {
		return m.listAliases(ctx, fixedExpenseID)
	}
	return nil, nil
}
func (m *mockTransactionReviewRepo) Upsert(ctx context.Context, periodID, transactionID, matchedTransactionID uuid.UUID, score float64) (db.TransactionReview, error) {
	if m.upsert != nil {
		return m.upsert(ctx, periodID, transactionID, matchedTransactionID, score)
	}
	return db.TransactionReview{}, nil
}
func (m *mockTransactionReviewRepo) GetFixedExpenseByAlias(ctx context.Context, alias string, budgetProfileID uuid.UUID) (db.GetFixedExpenseByAliasRow, error) {
	if m.getFixedExpenseByAlias != nil {
		return m.getFixedExpenseByAlias(ctx, alias, budgetProfileID)
	}
	return db.GetFixedExpenseByAliasRow{}, apperr.NotFound("fixed_expense_alias", "")
}

// ── UpdatePaymentMethod tests ─────────────────────────────────────────────────

func TestUpdatePaymentMethod_Success_OwnMethod(t *testing.T) {
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
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				return db.PaymentMethod{ID: id, UserID: &userID}, nil
			},
			updatePaymentMethod: func(_ context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error) {
				assert.Equal(t, methodID, arg.ID)
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
		ID:   methodID,
		Name: "Chase Visa",
	}, userID)

	require.NoError(t, err)
	assert.Equal(t, expected.ID, result.ID)
	assert.Equal(t, expected.Name, result.Name)
	assert.Equal(t, expected.PaymentTypeID, result.PaymentTypeID)
}

func TestUpdatePaymentMethod_Success_AdminUpdatesCollaboratorMethod(t *testing.T) {
	typeID := int32(2) // CREDIT
	methodID := uuid.New()
	adminID := uuid.New()
	collaboratorPersonID := int32(7)
	profileID := uuid.New()
	expected := db.PaymentMethod{
		ID:            methodID,
		Name:          "Updated Name",
		PaymentTypeID: &typeID,
	}

	svc := NewTransactionService(
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				return db.PaymentMethod{ID: id, BudgetPersonID: &collaboratorPersonID}, nil
			},
			updatePaymentMethod: func(_ context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error) {
				return expected, nil
			},
		},
		&mockBudgetProfileRepo{
			getPersonByID: func(_ context.Context, personID int32) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{ID: personID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: id, UserID: adminID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	result, err := svc.UpdatePaymentMethod(context.Background(), db.UpdatePaymentMethodParams{
		ID:   methodID,
		Name: "Updated Name",
	}, adminID)

	require.NoError(t, err)
	assert.Equal(t, expected.ID, result.ID)
}

func TestUpdatePaymentMethod_Forbidden_ViewerCannotUpdate(t *testing.T) {
	methodID := uuid.New()
	viewerID := uuid.New()
	personID := int32(5)
	profileID := uuid.New()
	ownerID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				return db.PaymentMethod{ID: id, BudgetPersonID: &personID}, nil
			},
		},
		&mockBudgetProfileRepo{
			getPersonByID: func(_ context.Context, pid int32) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{ID: pid, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: id, UserID: ownerID}, nil
			},
			getPersonByUserID: func(_ context.Context, profID, uid uuid.UUID) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{Role: "viewer"}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.UpdatePaymentMethod(context.Background(), db.UpdatePaymentMethodParams{
		ID:   methodID,
		Name: "Renamed",
	}, viewerID)

	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestUpdatePaymentMethod_Forbidden_WhenNotOwnerOfUnattributedMethod(t *testing.T) {
	methodID := uuid.New()
	ownerID := uuid.New()
	otherUserID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				return db.PaymentMethod{ID: id, UserID: &ownerID}, nil
			},
		},
		&mockBudgetProfileRepo{},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.UpdatePaymentMethod(context.Background(), db.UpdatePaymentMethodParams{
		ID:   methodID,
		Name: "Renamed",
	}, otherUserID)

	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestUpdatePaymentMethod_NotFound_WhenMethodMissing(t *testing.T) {
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

	_, err := svc.UpdatePaymentMethod(context.Background(), db.UpdatePaymentMethodParams{
		ID:   uuid.New(),
		Name: "Renamed",
	}, uuid.New())

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
				return db.PaymentMethod{ID: id, Name: "method"}, nil
			},
			deletePaymentMethodAndReassign: func(_ context.Context, arg db.DeletePaymentMethodAndReassignParams) error {
				assert.Equal(t, methodID, arg.ID)
				assert.Equal(t, replacementID, arg.ReplacementID)
				assert.Equal(t, profileID, arg.BudgetProfileID)
				return nil
			},
		},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: id, UserID: userID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeletePaymentMethod(context.Background(), methodID, replacementID, profileID, userID)
	require.NoError(t, err)
}

func TestDeletePaymentMethod_Forbidden_WhenViewer(t *testing.T) {
	viewerID := uuid.New()
	ownerID := uuid.New()
	profileID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: id, UserID: ownerID}, nil
			},
			getPersonByUserID: func(_ context.Context, profID, uid uuid.UUID) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{Role: "viewer"}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeletePaymentMethod(context.Background(), uuid.New(), uuid.New(), profileID, viewerID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestDeletePaymentMethod_NotFound_WhenMethodMissing(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				return db.PaymentMethod{}, apperr.NotFound("payment_method", id.String())
			},
		},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: id, UserID: userID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	err := svc.DeletePaymentMethod(context.Background(), uuid.New(), uuid.New(), profileID, userID)
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

func TestSetTransactionExcluded_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	txID := uuid.New()

	svc := NewTransactionService(
		&mockTransactionRepo{
			setExcluded: func(_ context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
				assert.Equal(t, txID, arg.ID)
				assert.Equal(t, periodID, arg.BudgetPeriodID)
				assert.True(t, arg.Excluded)
				return db.Transaction{ID: txID, IsExcluded: arg.Excluded}, nil
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

	tx, err := svc.SetTransactionExcluded(context.Background(), txID, periodID, true, userID)
	require.NoError(t, err)
	assert.Equal(t, txID, tx.ID)
	assert.True(t, tx.IsExcluded)
}

func TestSetTransactionExcluded_Forbidden_WhenNotOwner(t *testing.T) {
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

	_, err := svc.SetTransactionExcluded(context.Background(), uuid.New(), periodID, true, uuid.New())
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

// ── CreateTransaction review-queueing tests ───────────────────────────────────

func TestCreateTransaction_QueuesReview_WhenScoreOver80(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	feID := uuid.New()
	unpaidTxID := uuid.New()
	variableType := int32(2)
	txName := "Netflix"
	catID := int32(3)
	pmID := uuid.New()

	var upsertCalled bool

	feAmount := pgtype.Numeric{}
	_ = feAmount.Scan("15.99")
	txAmount := pgtype.Numeric{}
	_ = txAmount.Scan("15.99")

	svc := NewTransactionService(
		&mockTransactionRepo{
			create: func(_ context.Context, _ db.CreateTransactionParams) (db.Transaction, error) {
				return db.Transaction{
					ID:              uuid.New(),
					Name:            &txName,
					Amount:          txAmount,
					CategoryID:      &catID,
					PaymentMethodID: &pmID,
					BudgetPeriodID:  &periodID,
				}, nil
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
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				fe := db.FixedExpense{
					ID:              feID,
					Name:            "Netflix",
					PlannedAmount:   feAmount,
					CategoryID:      &catID,
					PaymentMethodID: &pmID,
				}
				return []db.FixedExpense{fe}, nil
			},
			getUnpaidTransaction: func(_ context.Context, _ db.GetUnpaidTransactionByFixedExpenseParams) (db.Transaction, error) {
				return db.Transaction{ID: unpaidTxID, BudgetPeriodID: &periodID}, nil
			},
		},
		&mockTransactionReviewRepo{
			upsert: func(_ context.Context, pID, tID, mID uuid.UUID, score float64) (db.TransactionReview, error) {
				assert.Equal(t, periodID, pID)
				assert.Equal(t, unpaidTxID, mID)
				assert.GreaterOrEqual(t, score, 80.0)
				upsertCalled = true
				return db.TransactionReview{}, nil
			},
		},
	)

	_, err := svc.Create(context.Background(), db.CreateTransactionParams{
		BudgetPeriodID:    &periodID,
		TransactionTypeID: &variableType,
	}, userID)
	require.NoError(t, err)
	assert.True(t, upsertCalled, "expected Upsert to be called for high-scoring match")
}

func TestCreateTransaction_NoReview_WhenScoreUnder80(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	variableType := int32(2)
	txName := "Coffee Shop"

	var upsertCalled bool

	feAmount := pgtype.Numeric{}
	_ = feAmount.Scan("200.00")
	txAmount := pgtype.Numeric{}
	_ = txAmount.Scan("5.00")

	svc := NewTransactionService(
		&mockTransactionRepo{
			create: func(_ context.Context, _ db.CreateTransactionParams) (db.Transaction, error) {
				return db.Transaction{
					ID:             uuid.New(),
					Name:           &txName,
					Amount:         txAmount,
					BudgetPeriodID: &periodID,
				}, nil
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
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				fe := db.FixedExpense{ID: uuid.New(), Name: "Rent", PlannedAmount: feAmount}
				return []db.FixedExpense{fe}, nil
			},
		},
		&mockTransactionReviewRepo{
			upsert: func(_ context.Context, _, _, _ uuid.UUID, _ float64) (db.TransactionReview, error) {
				upsertCalled = true
				return db.TransactionReview{}, nil
			},
		},
	)

	_, err := svc.Create(context.Background(), db.CreateTransactionParams{
		BudgetPeriodID:    &periodID,
		TransactionTypeID: &variableType,
	}, userID)
	require.NoError(t, err)
	assert.False(t, upsertCalled, "expected Upsert NOT to be called for low-scoring match")
}

func TestCreateTransaction_NoReview_ForFixedType(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	fixedType := int32(1)

	var upsertCalled bool

	svc := NewTransactionService(
		&mockTransactionRepo{
			create: func(_ context.Context, _ db.CreateTransactionParams) (db.Transaction, error) {
				return db.Transaction{ID: uuid.New(), BudgetPeriodID: &periodID}, nil
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
		&mockTransactionReviewRepo{
			upsert: func(_ context.Context, _, _, _ uuid.UUID, _ float64) (db.TransactionReview, error) {
				upsertCalled = true
				return db.TransactionReview{}, nil
			},
		},
	)

	_, err := svc.Create(context.Background(), db.CreateTransactionParams{
		BudgetPeriodID:    &periodID,
		TransactionTypeID: &fixedType,
	}, userID)
	require.NoError(t, err)
	assert.False(t, upsertCalled, "expected Upsert NOT to be called for fixed transactions")
}

// ── MarkTransactionForReview tests ────────────────────────────────────────────

func TestMarkTransactionForReview_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	txID := uuid.New()
	matchedTxID := uuid.New()
	variableType := int32(2)
	fixedType := int32(1)

	svc := NewTransactionService(
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				if id == matchedTxID {
					return db.Transaction{ID: id, TransactionTypeID: &fixedType, BudgetPeriodID: &periodID}, nil
				}
				return db.Transaction{ID: id, TransactionTypeID: &variableType, BudgetPeriodID: &periodID}, nil
			},
		},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: id, BudgetProfileID: profileID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	review, err := svc.MarkTransactionForReview(context.Background(), userID, txID, matchedTxID, profileID)
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

func TestMarkTransactionForReview_Forbidden_WhenMatchedTransactionOtherPeriod(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	otherPeriodID := uuid.New()
	matchedTxID := uuid.New()
	variableType := int32(2)
	fixedType := int32(1)

	svc := NewTransactionService(
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				if id == matchedTxID {
					return db.Transaction{ID: id, TransactionTypeID: &fixedType, BudgetPeriodID: &otherPeriodID}, nil
				}
				return db.Transaction{ID: id, TransactionTypeID: &variableType, BudgetPeriodID: &periodID}, nil
			},
		},
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: id, BudgetProfileID: profileID}, nil
			},
		},
		&mockExpenseAllocationRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
	)

	_, err := svc.MarkTransactionForReview(context.Background(), userID, uuid.New(), matchedTxID, profileID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

// ── ConfirmTransactionReview tests ────────────────────────────────────────────

func TestConfirmTransactionReview_FixedExpenseMatch_MarksPaidAndSavesAlias(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	reviewID := uuid.New()
	importedTxID := uuid.New()
	matchedTxID := uuid.New()
	feID := uuid.New()
	importedName := "NETFLIX.COM"

	var markedPaidID uuid.UUID
	var aliasFEID uuid.UUID
	var aliasText string
	var confirmedStatus string
	var excludedID uuid.UUID
	var excludedFlag bool

	svc := NewTransactionService(
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				if id == matchedTxID {
					return db.Transaction{ID: matchedTxID, IsPaid: false, BudgetPeriodID: &periodID, FixedExpenseID: &feID}, nil
				}
				return db.Transaction{ID: importedTxID, Name: &importedName}, nil
			},
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				markedPaidID = arg.ID
				return db.Transaction{ID: arg.ID}, nil
			},
			setExcluded: func(_ context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
				excludedID = arg.ID
				excludedFlag = arg.Excluded
				return db.Transaction{ID: arg.ID, IsExcluded: arg.Excluded}, nil
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
		&mockTransactionReviewRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.TransactionReview, error) {
				return db.TransactionReview{ID: id, BudgetPeriodID: periodID, TransactionID: importedTxID, MatchedTransactionID: matchedTxID}, nil
			},
			createAlias: func(_ context.Context, fixedExpenseID uuid.UUID, alias string) error {
				aliasFEID = fixedExpenseID
				aliasText = alias
				return nil
			},
			updateStatus: func(_ context.Context, _ uuid.UUID, status string) error {
				confirmedStatus = status
				return nil
			},
		},
	)

	err := svc.ConfirmTransactionReview(context.Background(), userID, reviewID, profileID)
	require.NoError(t, err)
	assert.Equal(t, matchedTxID, markedPaidID, "should mark the matched transaction paid, not the imported one")
	assert.Equal(t, feID, aliasFEID)
	assert.Equal(t, importedName, aliasText)
	assert.Equal(t, "confirmed", confirmedStatus)
	assert.Equal(t, importedTxID, excludedID, "should exclude the imported transaction from totals, not the matched one")
	assert.True(t, excludedFlag, "the imported transaction stays visible but excluded, same as Income — not hidden from ListTransactions")
}

func TestConfirmTransactionReview_SavingsMatch_MarksPaidWithoutAlias(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	reviewID := uuid.New()
	importedTxID := uuid.New()
	matchedTxID := uuid.New() // savings-derived: no FixedExpenseID

	var markedPaidID uuid.UUID
	aliasCalled := false

	svc := NewTransactionService(
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				if id == matchedTxID {
					return db.Transaction{ID: matchedTxID, IsPaid: false, BudgetPeriodID: &periodID}, nil
				}
				return db.Transaction{ID: importedTxID}, nil
			},
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				markedPaidID = arg.ID
				return db.Transaction{ID: arg.ID}, nil
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
		&mockTransactionReviewRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.TransactionReview, error) {
				return db.TransactionReview{ID: id, BudgetPeriodID: periodID, TransactionID: importedTxID, MatchedTransactionID: matchedTxID}, nil
			},
			createAlias: func(_ context.Context, _ uuid.UUID, _ string) error {
				aliasCalled = true
				return nil
			},
		},
	)

	err := svc.ConfirmTransactionReview(context.Background(), userID, reviewID, profileID)
	require.NoError(t, err)
	assert.Equal(t, matchedTxID, markedPaidID)
	assert.False(t, aliasCalled, "savings-derived matches have no FixedExpense template to alias against")
}

func TestConfirmTransactionReview_AlreadyPaid_SkipsMarkAsPaid(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	reviewID := uuid.New()
	matchedTxID := uuid.New()

	markAsPaidCalled := false

	svc := NewTransactionService(
		&mockTransactionRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				if id == matchedTxID {
					return db.Transaction{ID: matchedTxID, IsPaid: true, BudgetPeriodID: &periodID}, nil
				}
				return db.Transaction{ID: id}, nil
			},
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				markAsPaidCalled = true
				return db.Transaction{ID: arg.ID}, nil
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
		&mockTransactionReviewRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.TransactionReview, error) {
				return db.TransactionReview{ID: id, BudgetPeriodID: periodID, MatchedTransactionID: matchedTxID}, nil
			},
		},
	)

	err := svc.ConfirmTransactionReview(context.Background(), userID, reviewID, profileID)
	require.NoError(t, err)
	assert.False(t, markAsPaidCalled, "an already-paid match target should not be re-marked")
}

func TestUnmarkTransactionAsPaid_ResetsConfirmedReview(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	txID := uuid.New()
	importedTxID := uuid.New()
	feID := uuid.New()
	importedName := "NETFLIX.COM"

	var deletedAliasFEID uuid.UUID
	var deletedAliasText string
	var resetTxID uuid.UUID
	var unexcludedID uuid.UUID
	unexcludedFlag := true // sentinel so a missing call is visible as "true" in the assertion below

	svc := NewTransactionService(
		&mockTransactionRepo{
			unmarkAsPaid: func(_ context.Context, arg db.UnmarkTransactionAsPaidParams) (db.Transaction, error) {
				return db.Transaction{ID: arg.ID, IsPaid: false, FixedExpenseID: &feID}, nil
			},
			getByID: func(_ context.Context, id uuid.UUID) (db.Transaction, error) {
				return db.Transaction{ID: id, Name: &importedName}, nil
			},
			setExcluded: func(_ context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
				unexcludedID = arg.ID
				unexcludedFlag = arg.Excluded
				return db.Transaction{ID: arg.ID, IsExcluded: arg.Excluded}, nil
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
		&mockTransactionReviewRepo{
			getConfirmedByMatchedTx: func(_ context.Context, matchedTransactionID uuid.UUID) (db.TransactionReview, error) {
				return db.TransactionReview{TransactionID: importedTxID, MatchedTransactionID: matchedTransactionID}, nil
			},
			deleteAlias: func(_ context.Context, fixedExpenseID uuid.UUID, alias string) error {
				deletedAliasFEID = fixedExpenseID
				deletedAliasText = alias
				return nil
			},
			resetByMatchedTx: func(_ context.Context, matchedTransactionID uuid.UUID) error {
				resetTxID = matchedTransactionID
				return nil
			},
		},
	)

	_, err := svc.UnmarkTransactionAsPaid(context.Background(), txID, periodID, userID)
	require.NoError(t, err)
	assert.Equal(t, txID, resetTxID)
	assert.Equal(t, feID, deletedAliasFEID)
	assert.Equal(t, importedName, deletedAliasText)
	assert.Equal(t, importedTxID, unexcludedID, "should un-exclude the imported transaction, not the fixed one being unmarked")
	assert.False(t, unexcludedFlag, "unmarking paid should restore the imported transaction to a normal, non-excluded row")
}
