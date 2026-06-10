package repository

import (
	"context"
	"errors"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type BudgetRepository interface {
	ListByUserID(ctx context.Context, userID uuid.UUID) ([]db.Budget, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.Budget, error)
	ExistsByNameAndUser(ctx context.Context, name string, userID uuid.UUID) (bool, error)
	Create(ctx context.Context, arg db.CreateBudgetParams) (db.Budget, error)
	Update(ctx context.Context, arg db.UpdateBudgetParams) (db.Budget, error)
	Delete(ctx context.Context, id uuid.UUID) error

	ListPeople(ctx context.Context, budgetID uuid.UUID) ([]db.BudgetToUserMapping, error)
	ExistsPerson(ctx context.Context, budgetID uuid.UUID, userName string) (bool, error)
	AddPerson(ctx context.Context, arg db.AddBudgetPersonParams) (db.BudgetToUserMapping, error)
	RemovePerson(ctx context.Context, arg db.RemoveBudgetPersonParams) error

	ListIncome(ctx context.Context, budgetID uuid.UUID) ([]db.IncomeToBudgetMapping, error)
	AddIncome(ctx context.Context, arg db.AddIncomeEntryParams) (db.IncomeToBudgetMapping, error)
	UpdateIncome(ctx context.Context, arg db.UpdateIncomeEntryParams) (db.IncomeToBudgetMapping, error)
	DeleteIncome(ctx context.Context, arg db.DeleteIncomeEntryParams) error
}

type budgetRepository struct {
	q *db.Queries
}

func NewBudgetRepository(q *db.Queries) BudgetRepository {
	return &budgetRepository{q: q}
}

func (r *budgetRepository) ListByUserID(ctx context.Context, userID uuid.UUID) ([]db.Budget, error) {
	return r.q.ListBudgetsByUser(ctx, userID)
}

func (r *budgetRepository) GetByID(ctx context.Context, id uuid.UUID) (db.Budget, error) {
	b, err := r.q.GetBudgetByID(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Budget{}, apperr.NotFound("budget", id.String())
	}
	return b, err
}

func (r *budgetRepository) ExistsByNameAndUser(ctx context.Context, name string, userID uuid.UUID) (bool, error) {
	return r.q.ExistsBudgetByNameAndUser(ctx, name, userID)
}

func (r *budgetRepository) Create(ctx context.Context, arg db.CreateBudgetParams) (db.Budget, error) {
	return r.q.CreateBudget(ctx, arg)
}

func (r *budgetRepository) Update(ctx context.Context, arg db.UpdateBudgetParams) (db.Budget, error) {
	return r.q.UpdateBudget(ctx, arg)
}

func (r *budgetRepository) Delete(ctx context.Context, id uuid.UUID) error {
	return r.q.DeleteBudget(ctx, id)
}

func (r *budgetRepository) ListPeople(ctx context.Context, budgetID uuid.UUID) ([]db.BudgetToUserMapping, error) {
	return r.q.ListBudgetPeople(ctx, budgetID)
}

func (r *budgetRepository) ExistsPerson(ctx context.Context, budgetID uuid.UUID, userName string) (bool, error) {
	return r.q.ExistsBudgetPerson(ctx, budgetID, userName)
}

func (r *budgetRepository) AddPerson(ctx context.Context, arg db.AddBudgetPersonParams) (db.BudgetToUserMapping, error) {
	return r.q.AddBudgetPerson(ctx, arg)
}

func (r *budgetRepository) RemovePerson(ctx context.Context, arg db.RemoveBudgetPersonParams) error {
	return r.q.RemoveBudgetPerson(ctx, arg)
}

func (r *budgetRepository) ListIncome(ctx context.Context, budgetID uuid.UUID) ([]db.IncomeToBudgetMapping, error) {
	return r.q.ListIncomeEntries(ctx, budgetID)
}

func (r *budgetRepository) AddIncome(ctx context.Context, arg db.AddIncomeEntryParams) (db.IncomeToBudgetMapping, error) {
	return r.q.AddIncomeEntry(ctx, arg)
}

func (r *budgetRepository) UpdateIncome(ctx context.Context, arg db.UpdateIncomeEntryParams) (db.IncomeToBudgetMapping, error) {
	return r.q.UpdateIncomeEntry(ctx, arg)
}

func (r *budgetRepository) DeleteIncome(ctx context.Context, arg db.DeleteIncomeEntryParams) error {
	return r.q.DeleteIncomeEntry(ctx, arg)
}
