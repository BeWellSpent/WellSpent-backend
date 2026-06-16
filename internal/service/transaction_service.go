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
	budgets      repository.BudgetRepository
}

func NewTransactionService(transactions repository.TransactionRepository, budgets repository.BudgetRepository) *TransactionService {
	return &TransactionService{transactions: transactions, budgets: budgets}
}

func (s *TransactionService) assertBudgetOwner(ctx context.Context, budgetID, userID uuid.UUID) error {
	budget, err := s.budgets.GetByID(ctx, budgetID)
	if err != nil {
		return err
	}
	if budget.UserID != userID {
		return apperr.Forbidden("access denied")
	}
	return nil
}

func (s *TransactionService) GetByID(ctx context.Context, id uuid.UUID) (db.Transaction, error) {
	return s.transactions.GetByID(ctx, id)
}

func (s *TransactionService) List(ctx context.Context, arg db.ListTransactionsParams, userID uuid.UUID) ([]db.Transaction, error) {
	if err := s.assertBudgetOwner(ctx, arg.BudgetID, userID); err != nil {
		return nil, err
	}
	return s.transactions.List(ctx, arg)
}

func (s *TransactionService) Create(ctx context.Context, arg db.CreateTransactionParams, userID uuid.UUID) (db.Transaction, error) {
	if arg.BudgetID != nil {
		if err := s.assertBudgetOwner(ctx, *arg.BudgetID, userID); err != nil {
			return db.Transaction{}, err
		}
	}
	return s.transactions.Create(ctx, arg)
}

func (s *TransactionService) Update(ctx context.Context, arg db.UpdateTransactionParams, budgetID, userID uuid.UUID) (db.Transaction, error) {
	if err := s.assertBudgetOwner(ctx, budgetID, userID); err != nil {
		return db.Transaction{}, err
	}
	return s.transactions.Update(ctx, arg)
}

func (s *TransactionService) Delete(ctx context.Context, id, budgetID, userID uuid.UUID) error {
	if err := s.assertBudgetOwner(ctx, budgetID, userID); err != nil {
		return err
	}
	return s.transactions.Delete(ctx, db.DeleteTransactionParams{ID: id, BudgetID: &budgetID})
}

func (s *TransactionService) ListCategories(ctx context.Context, userID uuid.UUID) ([]db.Category, error) {
	return s.transactions.ListCategories(ctx, userID)
}

func (s *TransactionService) ListPaymentMethods(ctx context.Context, userID uuid.UUID) ([]db.ListPaymentMethodsRow, error) {
	return s.transactions.ListPaymentMethods(ctx, userID)
}

func (s *TransactionService) CreatePaymentMethod(ctx context.Context, arg db.CreatePaymentMethodParams) (db.PaymentMethod, error) {
	return s.transactions.CreatePaymentMethod(ctx, arg)
}

func (s *TransactionService) UpdatePaymentMethod(ctx context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error) {
	return s.transactions.UpdatePaymentMethod(ctx, arg)
}
