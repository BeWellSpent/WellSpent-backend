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
	list                          func(context.Context, db.ListTransactionsParams) ([]db.Transaction, error)
	listFixedRecurring            func(context.Context, uuid.UUID) ([]db.Transaction, error)
	getByID                       func(context.Context, uuid.UUID) (db.Transaction, error)
	create                        func(context.Context, db.CreateTransactionParams) (db.Transaction, error)
	update                        func(context.Context, db.UpdateTransactionParams) (db.Transaction, error)
	delete                        func(context.Context, db.DeleteTransactionParams) error
	getCategory                   func(context.Context, int32) (db.GetCategoryRow, error)
	listCategories                func(context.Context, uuid.UUID) ([]db.ListCategoriesRow, error)
	createCategory                func(context.Context, db.CreateCategoryParams) (db.CreateCategoryRow, error)
	updateCategory                func(context.Context, db.UpdateCategoryParams) (db.UpdateCategoryRow, error)
	updateSystemCategoryColor     func(context.Context, db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error)
	deleteCategoryAndReassign     func(context.Context, db.DeleteCategoryAndReassignParams) error
	listPaymentMethods            func(context.Context, uuid.UUID) ([]db.ListPaymentMethodsRow, error)
	createPaymentMethod           func(context.Context, db.CreatePaymentMethodParams) (db.PaymentMethod, error)
	updatePaymentMethod           func(context.Context, db.UpdatePaymentMethodParams) (db.PaymentMethod, error)
	getPaymentMethod              func(context.Context, uuid.UUID) (db.PaymentMethod, error)
	deletePaymentMethodAndReassign func(context.Context, db.DeletePaymentMethodAndReassignParams) error
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
	)

	err := svc.DeletePaymentMethod(context.Background(), uuid.New(), uuid.New(), uuid.New(), uuid.New())
	require.Error(t, err)
	var notFound *apperr.NotFoundError
	require.ErrorAs(t, err, &notFound)
}
