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
	list                func(context.Context, db.ListTransactionsParams) ([]db.Transaction, error)
	getByID             func(context.Context, uuid.UUID) (db.Transaction, error)
	create              func(context.Context, db.CreateTransactionParams) (db.Transaction, error)
	update              func(context.Context, db.UpdateTransactionParams) (db.Transaction, error)
	delete              func(context.Context, db.DeleteTransactionParams) error
	listCategories      func(context.Context, uuid.UUID) ([]db.Category, error)
	createCategory      func(context.Context, db.CreateCategoryParams) (db.Category, error)
	listPaymentMethods  func(context.Context, uuid.UUID) ([]db.ListPaymentMethodsRow, error)
	createPaymentMethod func(context.Context, db.CreatePaymentMethodParams) (db.PaymentMethod, error)
	updatePaymentMethod func(context.Context, db.UpdatePaymentMethodParams) (db.PaymentMethod, error)
}

func (m *mockTransactionRepo) List(ctx context.Context, arg db.ListTransactionsParams) ([]db.Transaction, error) {
	if m.list != nil {
		return m.list(ctx, arg)
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

func (m *mockTransactionRepo) ListCategories(ctx context.Context, userID uuid.UUID) ([]db.Category, error) {
	if m.listCategories != nil {
		return m.listCategories(ctx, userID)
	}
	return nil, nil
}

func (m *mockTransactionRepo) CreateCategory(ctx context.Context, arg db.CreateCategoryParams) (db.Category, error) {
	if m.createCategory != nil {
		return m.createCategory(ctx, arg)
	}
	return db.Category{}, nil
}

func (m *mockTransactionRepo) ListPaymentMethods(ctx context.Context, userID uuid.UUID) ([]db.ListPaymentMethodsRow, error) {
	if m.listPaymentMethods != nil {
		return m.listPaymentMethods(ctx, userID)
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

// ── Mock budget repo ──────────────────────────────────────────────────────────

type mockBudgetRepo struct {
	getByID func(context.Context, uuid.UUID) (db.Budget, error)
}

func (m *mockBudgetRepo) ListByUserID(ctx context.Context, userID uuid.UUID) ([]db.Budget, error) {
	return nil, nil
}

func (m *mockBudgetRepo) GetByID(ctx context.Context, id uuid.UUID) (db.Budget, error) {
	if m.getByID != nil {
		return m.getByID(ctx, id)
	}
	return db.Budget{}, apperr.NotFound("budget", id.String())
}

func (m *mockBudgetRepo) ExistsByNameAndUser(ctx context.Context, name string, userID uuid.UUID) (bool, error) {
	return false, nil
}

func (m *mockBudgetRepo) Create(ctx context.Context, arg db.CreateBudgetParams) (db.Budget, error) {
	return db.Budget{}, nil
}

func (m *mockBudgetRepo) Update(ctx context.Context, arg db.UpdateBudgetParams) (db.Budget, error) {
	return db.Budget{}, nil
}

func (m *mockBudgetRepo) Delete(ctx context.Context, id uuid.UUID) error {
	return nil
}

func (m *mockBudgetRepo) ListPeople(ctx context.Context, budgetID uuid.UUID) ([]db.BudgetToUserMapping, error) {
	return nil, nil
}

func (m *mockBudgetRepo) ExistsPerson(ctx context.Context, budgetID uuid.UUID, userName string) (bool, error) {
	return false, nil
}

func (m *mockBudgetRepo) AddPerson(ctx context.Context, arg db.AddBudgetPersonParams) (db.BudgetToUserMapping, error) {
	return db.BudgetToUserMapping{}, nil
}

func (m *mockBudgetRepo) RemovePerson(ctx context.Context, arg db.RemoveBudgetPersonParams) error {
	return nil
}

func (m *mockBudgetRepo) ListIncome(ctx context.Context, budgetID uuid.UUID) ([]db.IncomeToBudgetMapping, error) {
	return nil, nil
}

func (m *mockBudgetRepo) AddIncome(ctx context.Context, arg db.AddIncomeEntryParams) (db.IncomeToBudgetMapping, error) {
	return db.IncomeToBudgetMapping{}, nil
}

func (m *mockBudgetRepo) UpdateIncome(ctx context.Context, arg db.UpdateIncomeEntryParams) (db.IncomeToBudgetMapping, error) {
	return db.IncomeToBudgetMapping{}, nil
}

func (m *mockBudgetRepo) DeleteIncome(ctx context.Context, arg db.DeleteIncomeEntryParams) error {
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
		&mockBudgetRepo{},
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
		&mockBudgetRepo{},
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
