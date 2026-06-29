package service

import (
	"context"

	"github.com/google/uuid"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/repository"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type TransactionService struct {
	transactions repository.TransactionRepository
	profiles     repository.BudgetProfileRepository
}

func NewTransactionService(transactions repository.TransactionRepository, profiles repository.BudgetProfileRepository) *TransactionService {
	return &TransactionService{transactions: transactions, profiles: profiles}
}

func (s *TransactionService) assertPeriodOwner(ctx context.Context, periodID, userID uuid.UUID) error {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		return err
	}
	profile, err := s.profiles.GetByID(ctx, period.BudgetProfileID)
	if err != nil {
		return err
	}
	if profile.UserID != userID {
		return apperr.Forbidden("access denied")
	}
	return nil
}

func (s *TransactionService) GetByID(ctx context.Context, id uuid.UUID) (db.Transaction, error) {
	return s.transactions.GetByID(ctx, id)
}

func (s *TransactionService) List(ctx context.Context, arg db.ListTransactionsParams, userID uuid.UUID) ([]db.Transaction, error) {
	if err := s.assertPeriodOwner(ctx, arg.BudgetPeriodID, userID); err != nil {
		return nil, err
	}
	return s.transactions.List(ctx, arg)
}

func (s *TransactionService) Create(ctx context.Context, arg db.CreateTransactionParams, userID uuid.UUID) (db.Transaction, error) {
	if arg.BudgetPeriodID != nil {
		if err := s.assertPeriodOwner(ctx, *arg.BudgetPeriodID, userID); err != nil {
			return db.Transaction{}, err
		}
	}
	return s.transactions.Create(ctx, arg)
}

func (s *TransactionService) Update(ctx context.Context, arg db.UpdateTransactionParams, userID uuid.UUID) (db.Transaction, error) {
	tx, err := s.transactions.GetByID(ctx, arg.ID)
	if err != nil {
		return db.Transaction{}, err
	}
	if tx.BudgetPeriodID != nil {
		if err := s.assertPeriodOwner(ctx, *tx.BudgetPeriodID, userID); err != nil {
			return db.Transaction{}, err
		}
	}
	return s.transactions.Update(ctx, arg)
}

func (s *TransactionService) Delete(ctx context.Context, id, userID uuid.UUID) error {
	tx, err := s.transactions.GetByID(ctx, id)
	if err != nil {
		return err
	}
	if tx.BudgetPeriodID != nil {
		if err := s.assertPeriodOwner(ctx, *tx.BudgetPeriodID, userID); err != nil {
			return err
		}
	}
	return s.transactions.Delete(ctx, db.DeleteTransactionParams{ID: id, BudgetPeriodID: tx.BudgetPeriodID})
}

func (s *TransactionService) GetCategory(ctx context.Context, id int32) (db.GetCategoryRow, error) {
	return s.transactions.GetCategory(ctx, id)
}

func (s *TransactionService) ListCategories(ctx context.Context, userID uuid.UUID) ([]db.ListCategoriesRow, error) {
	return s.transactions.ListCategories(ctx, userID)
}

func (s *TransactionService) CreateCategory(ctx context.Context, arg db.CreateCategoryParams) (db.CreateCategoryRow, error) {
	return s.transactions.CreateCategory(ctx, arg)
}

func (s *TransactionService) UpdateCategory(ctx context.Context, arg db.UpdateCategoryParams) (db.UpdateCategoryRow, error) {
	cat, err := s.transactions.GetCategory(ctx, arg.ID)
	if err != nil {
		return db.UpdateCategoryRow{}, err
	}
	if cat.IsSystem {
		row, err := s.transactions.UpdateSystemCategoryColor(ctx, db.UpdateSystemCategoryColorParams{
			ID:    arg.ID,
			Color: arg.Color,
		})
		if err != nil {
			return db.UpdateCategoryRow{}, err
		}
		return db.UpdateCategoryRow{
			ID:       row.ID,
			Name:     row.Name,
			TypeID:   row.TypeID,
			IsSystem: row.IsSystem,
			UserID:   row.UserID,
			Color:    row.Color,
		}, nil
	}
	return s.transactions.UpdateCategory(ctx, arg)
}

func (s *TransactionService) DeleteCategory(ctx context.Context, id, replacementID int32, userID uuid.UUID) error {
	cat, err := s.transactions.GetCategory(ctx, id)
	if err != nil {
		return err
	}
	if cat.IsSystem {
		return apperr.Forbidden("system categories cannot be deleted")
	}
	if cat.UserID == nil || *cat.UserID != userID {
		return apperr.Forbidden("access denied")
	}
	replacement, err := s.transactions.GetCategory(ctx, replacementID)
	if err != nil {
		return err
	}
	if replacement.UserID != nil && *replacement.UserID != userID {
		return apperr.Forbidden("replacement category is not accessible")
	}
	return s.transactions.DeleteCategoryAndReassign(ctx, db.DeleteCategoryAndReassignParams{
		ID:            id,
		UserID:        userID,
		ReplacementID: &replacementID,
	})
}

func (s *TransactionService) ListPaymentMethods(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListPaymentMethodsRow, error) {
	return s.transactions.ListPaymentMethods(ctx, budgetProfileID)
}

func (s *TransactionService) CreatePaymentMethod(ctx context.Context, arg db.CreatePaymentMethodParams) (db.PaymentMethod, error) {
	return s.transactions.CreatePaymentMethod(ctx, arg)
}

func (s *TransactionService) UpdatePaymentMethod(ctx context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error) {
	return s.transactions.UpdatePaymentMethod(ctx, arg)
}

func (s *TransactionService) DeletePaymentMethod(ctx context.Context, id, replacementID, budgetProfileID, userID uuid.UUID) error {
	method, err := s.transactions.GetPaymentMethod(ctx, id)
	if err != nil {
		return err
	}
	if method.UserID == nil || *method.UserID != userID {
		return apperr.Forbidden("access denied")
	}
	replacement, err := s.transactions.GetPaymentMethod(ctx, replacementID)
	if err != nil {
		return err
	}
	if replacement.UserID == nil || *replacement.UserID != userID {
		return apperr.Forbidden("replacement payment method is not accessible")
	}
	return s.transactions.DeletePaymentMethodAndReassign(ctx, db.DeletePaymentMethodAndReassignParams{
		ID:              id,
		UserID:          userID,
		ReplacementID:   replacementID,
		BudgetProfileID: budgetProfileID,
	})
}
