package service

import (
	"context"
	"fmt"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/repository"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type BudgetService struct {
	budgets repository.BudgetRepository
}

func NewBudgetService(budgets repository.BudgetRepository) *BudgetService {
	return &BudgetService{budgets: budgets}
}

func (s *BudgetService) List(ctx context.Context, userID uuid.UUID) ([]db.Budget, error) {
	return s.budgets.ListByUserID(ctx, userID)
}

func (s *BudgetService) Get(ctx context.Context, id, userID uuid.UUID) (db.Budget, error) {
	budget, err := s.budgets.GetByID(ctx, id)
	if err != nil {
		return db.Budget{}, err
	}
	if budget.UserID != userID {
		return db.Budget{}, apperr.Forbidden("access denied")
	}
	return budget, nil
}

func (s *BudgetService) Create(ctx context.Context, userID uuid.UUID, name string) (db.Budget, error) {
	exists, err := s.budgets.ExistsByNameAndUser(ctx, name, userID)
	if err != nil {
		return db.Budget{}, fmt.Errorf("budget: check exists: %w", err)
	}
	if exists {
		return db.Budget{}, apperr.Duplicate("budget", "name", name)
	}
	return s.budgets.Create(ctx, db.CreateBudgetParams{UserID: userID, Name: name})
}

func (s *BudgetService) Update(ctx context.Context, id, userID uuid.UUID, name string, active bool) (db.Budget, error) {
	if _, err := s.Get(ctx, id, userID); err != nil {
		return db.Budget{}, err
	}
	return s.budgets.Update(ctx, db.UpdateBudgetParams{ID: id, Name: name, Active: active})
}

func (s *BudgetService) Delete(ctx context.Context, id, userID uuid.UUID) error {
	if _, err := s.Get(ctx, id, userID); err != nil {
		return err
	}
	return s.budgets.Delete(ctx, id)
}

// ── People ────────────────────────────────────────────────────────────────────

type PersonInput struct {
	UserName string
	UserID   *uuid.UUID
}

func (s *BudgetService) AddPeople(ctx context.Context, budgetID, userID uuid.UUID, people []PersonInput) ([]db.BudgetToUserMapping, error) {
	if _, err := s.Get(ctx, budgetID, userID); err != nil {
		return nil, err
	}
	var results []db.BudgetToUserMapping
	for _, p := range people {
		exists, err := s.budgets.ExistsPerson(ctx, budgetID, p.UserName)
		if err != nil {
			return nil, err
		}
		if exists {
			return nil, apperr.Duplicate("person", "name", p.UserName)
		}
		m, err := s.budgets.AddPerson(ctx, db.AddBudgetPersonParams{
			BudgetID: budgetID,
			UserName: &p.UserName,
			UserID:   p.UserID,
		})
		if err != nil {
			return nil, err
		}
		results = append(results, m)
	}
	return results, nil
}

func (s *BudgetService) ListPeople(ctx context.Context, budgetID, userID uuid.UUID) ([]db.BudgetToUserMapping, error) {
	if _, err := s.Get(ctx, budgetID, userID); err != nil {
		return nil, err
	}
	return s.budgets.ListPeople(ctx, budgetID)
}

func (s *BudgetService) RemovePerson(ctx context.Context, budgetID uuid.UUID, personID int32, userID uuid.UUID) error {
	if _, err := s.Get(ctx, budgetID, userID); err != nil {
		return err
	}
	return s.budgets.RemovePerson(ctx, db.RemoveBudgetPersonParams{
		ID:       personID,
		BudgetID: budgetID,
	})
}

// ── Income ────────────────────────────────────────────────────────────────────

type IncomeInput struct {
	Name      string
	Amount    pgtype.Numeric
	Recurring bool
	UserID    *uuid.UUID
}

func (s *BudgetService) AddIncome(ctx context.Context, budgetID, ownerID uuid.UUID, entries []IncomeInput) ([]db.IncomeToBudgetMapping, error) {
	if _, err := s.Get(ctx, budgetID, ownerID); err != nil {
		return nil, err
	}
	var results []db.IncomeToBudgetMapping
	for _, e := range entries {
		m, err := s.budgets.AddIncome(ctx, db.AddIncomeEntryParams{
			BudgetID:  budgetID,
			UserID:    e.UserID,
			Name:      &e.Name,
			Amount:    e.Amount,
			Recurring: e.Recurring,
		})
		if err != nil {
			return nil, err
		}
		results = append(results, m)
	}
	return results, nil
}

func (s *BudgetService) ListIncome(ctx context.Context, budgetID, userID uuid.UUID) ([]db.IncomeToBudgetMapping, error) {
	if _, err := s.Get(ctx, budgetID, userID); err != nil {
		return nil, err
	}
	return s.budgets.ListIncome(ctx, budgetID)
}

func (s *BudgetService) UpdateIncome(ctx context.Context, incomeID int32, budgetID, userID uuid.UUID, name string, amount pgtype.Numeric, recurring bool) (db.IncomeToBudgetMapping, error) {
	if _, err := s.Get(ctx, budgetID, userID); err != nil {
		return db.IncomeToBudgetMapping{}, err
	}
	return s.budgets.UpdateIncome(ctx, db.UpdateIncomeEntryParams{
		ID:        incomeID,
		BudgetID:  budgetID,
		Name:      &name,
		Amount:    amount,
		Recurring: recurring,
	})
}

func (s *BudgetService) DeleteIncome(ctx context.Context, incomeID int32, budgetID, userID uuid.UUID) error {
	if _, err := s.Get(ctx, budgetID, userID); err != nil {
		return err
	}
	return s.budgets.DeleteIncome(ctx, db.DeleteIncomeEntryParams{
		ID:       incomeID,
		BudgetID: budgetID,
	})
}
