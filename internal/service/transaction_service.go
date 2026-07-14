package service

import (
	"context"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

type TransactionService struct {
	transactions  repository.TransactionRepository
	profiles      repository.BudgetProfileRepository
	allocations   repository.ExpenseAllocationRepository
	fixedExpenses repository.FixedExpenseRepository
	reviews       repository.TransactionReviewRepository
}

func NewTransactionService(transactions repository.TransactionRepository, profiles repository.BudgetProfileRepository, allocations repository.ExpenseAllocationRepository, fixedExpenses repository.FixedExpenseRepository, reviews repository.TransactionReviewRepository) *TransactionService {
	return &TransactionService{transactions: transactions, profiles: profiles, allocations: allocations, fixedExpenses: fixedExpenses, reviews: reviews}
}

// getUserRoleForPeriod returns the caller's effective role for the budget profile
// that owns the given period. Profile owners are always "admin".
func (s *TransactionService) getUserRoleForPeriod(ctx context.Context, periodID, userID uuid.UUID) (string, error) {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		return "", err
	}
	profile, err := s.profiles.GetByID(ctx, period.BudgetProfileID)
	if err != nil {
		return "", err
	}
	if profile.UserID == userID {
		return "admin", nil
	}
	person, err := s.profiles.GetPersonByUserID(ctx, profile.ID, userID)
	if err != nil {
		return "", apperr.Forbidden("access denied")
	}
	return person.Role, nil
}

func (s *TransactionService) assertPeriodMember(ctx context.Context, periodID, userID uuid.UUID) error {
	_, err := s.getUserRoleForPeriod(ctx, periodID, userID)
	return err
}

func (s *TransactionService) assertPeriodCollaborator(ctx context.Context, periodID, userID uuid.UUID) error {
	role, err := s.getUserRoleForPeriod(ctx, periodID, userID)
	if err != nil {
		return err
	}
	if role != "admin" && role != "collaborator" {
		return apperr.Forbidden("access denied")
	}
	return nil
}

func (s *TransactionService) getUserRoleForProfile(ctx context.Context, profileID, userID uuid.UUID) (string, error) {
	profile, err := s.profiles.GetByID(ctx, profileID)
	if err != nil {
		return "", err
	}
	if profile.UserID == userID {
		return "admin", nil
	}
	person, err := s.profiles.GetPersonByUserID(ctx, profileID, userID)
	if err != nil {
		return "", apperr.Forbidden("access denied")
	}
	return person.Role, nil
}

func (s *TransactionService) assertProfileMember(ctx context.Context, profileID, userID uuid.UUID) error {
	_, err := s.getUserRoleForProfile(ctx, profileID, userID)
	return err
}

func (s *TransactionService) assertProfileCollaborator(ctx context.Context, profileID, userID uuid.UUID) error {
	role, err := s.getUserRoleForProfile(ctx, profileID, userID)
	if err != nil {
		return err
	}
	if role != "admin" && role != "collaborator" {
		return apperr.Forbidden("access denied")
	}
	return nil
}

func (s *TransactionService) GetByID(ctx context.Context, id uuid.UUID) (db.Transaction, error) {
	return s.transactions.GetByID(ctx, id)
}

func (s *TransactionService) List(ctx context.Context, arg db.ListTransactionsParams, userID uuid.UUID) ([]db.Transaction, error) {
	if err := s.assertPeriodMember(ctx, arg.BudgetPeriodID, userID); err != nil {
		return nil, err
	}
	return s.transactions.List(ctx, arg)
}

func (s *TransactionService) Create(ctx context.Context, arg db.CreateTransactionParams, userID uuid.UUID) (db.Transaction, error) {
	if arg.BudgetPeriodID != nil {
		if err := s.assertPeriodCollaborator(ctx, *arg.BudgetPeriodID, userID); err != nil {
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
		if err := s.assertPeriodCollaborator(ctx, *tx.BudgetPeriodID, userID); err != nil {
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
		if err := s.assertPeriodCollaborator(ctx, *tx.BudgetPeriodID, userID); err != nil {
			return err
		}
	}
	return s.transactions.Delete(ctx, db.DeleteTransactionParams{ID: id, BudgetPeriodID: tx.BudgetPeriodID})
}

func (s *TransactionService) GetCategory(ctx context.Context, id int32) (db.GetCategoryRow, error) {
	return s.transactions.GetCategory(ctx, id)
}

func (s *TransactionService) ListCategories(ctx context.Context, userID uuid.UUID, budgetProfileID *uuid.UUID) ([]db.ListCategoriesRow, error) {
	if budgetProfileID != nil {
		return s.transactions.ListCategoriesForBudget(ctx, userID, *budgetProfileID)
	}
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

// MarkTransactionAsPaid confirms payment for a fixed transaction, updating its
// actual amount and date. If the paid amount differs from planned, also updates
// the fixed expense template so future periods carry the corrected planned cost.
func (s *TransactionService) MarkTransactionAsPaid(ctx context.Context, id uuid.UUID, periodID uuid.UUID, paidAmount pgtype.Numeric, paidDate pgtype.Date, userID uuid.UUID) (db.Transaction, error) {
	if err := s.assertPeriodCollaborator(ctx, periodID, userID); err != nil {
		return db.Transaction{}, err
	}

	tx, err := s.transactions.MarkAsPaid(ctx, db.MarkTransactionAsPaidParams{
		ID:             id,
		BudgetPeriodID: periodID,
		Amount:         paidAmount,
		PaidDate:       paidDate,
	})
	if err != nil {
		return db.Transaction{}, err
	}

	// Keep the fixed expense template in sync when the paid amount differs from planned.
	if tx.FixedExpenseID != nil {
		_ = s.fixedExpenses.UpdatePlannedAmount(ctx, db.UpdateFixedExpensePlannedAmountParams{
			ID:            *tx.FixedExpenseID,
			PlannedAmount: paidAmount,
		})
	}

	return tx, nil
}

func (s *TransactionService) UnmarkTransactionAsPaid(ctx context.Context, id uuid.UUID, periodID uuid.UUID, userID uuid.UUID) (db.Transaction, error) {
	if err := s.assertPeriodCollaborator(ctx, periodID, userID); err != nil {
		return db.Transaction{}, err
	}
	tx, err := s.transactions.UnmarkAsPaid(ctx, db.UnmarkTransactionAsPaidParams{
		ID:             id,
		BudgetPeriodID: periodID,
	})
	if err != nil {
		return db.Transaction{}, err
	}

	// If this transaction was a confirmed review's match target, undo the
	// confirmation so the duplicate variable transaction reappears. Applies to
	// any Fixed-type transaction — fixed-expense-spawned or savings-derived.
	review, rErr := s.reviews.GetConfirmedByMatchedTransaction(ctx, tx.ID)
	if rErr == nil {
		if varTx, txErr := s.transactions.GetByID(ctx, review.TransactionID); txErr == nil && varTx.Name != nil && tx.FixedExpenseID != nil {
			_ = s.reviews.DeleteAlias(ctx, *tx.FixedExpenseID, *varTx.Name)
		}
		_ = s.reviews.ResetByMatchedTransaction(ctx, tx.ID)
	}

	return tx, nil
}

func (s *TransactionService) DeletePaymentMethod(ctx context.Context, id, replacementID, budgetProfileID, userID uuid.UUID) error {
	method, err := s.transactions.GetPaymentMethod(ctx, id)
	if err != nil {
		return err
	}
	if !s.paymentMethodInBudget(ctx, method, budgetProfileID, userID) {
		return apperr.Forbidden("access denied")
	}
	replacement, err := s.transactions.GetPaymentMethod(ctx, replacementID)
	if err != nil {
		return err
	}
	if !s.paymentMethodInBudget(ctx, replacement, budgetProfileID, userID) {
		return apperr.Forbidden("replacement payment method is not accessible")
	}
	return s.transactions.DeletePaymentMethodAndReassign(ctx, db.DeletePaymentMethodAndReassignParams{
		ID:              id,
		UserID:          userID,
		ReplacementID:   replacementID,
		BudgetProfileID: budgetProfileID,
	})
}

// paymentMethodInBudget returns true when pm belongs to the given budget profile
// (via its budget_person_id) or, for unattributed methods, when the requester
// owns it directly. Used to authorise cross-collaborator delete/update operations.
func (s *TransactionService) paymentMethodInBudget(ctx context.Context, pm db.PaymentMethod, budgetProfileID uuid.UUID, userID uuid.UUID) bool {
	if pm.BudgetPersonID != nil {
		_, err := s.profiles.GetPerson(ctx, *pm.BudgetPersonID, budgetProfileID)
		return err == nil
	}
	return pm.UserID != nil && *pm.UserID == userID
}

// ── Transaction review ────────────────────────────────────────────────────────

func (s *TransactionService) ListTransactionReviews(ctx context.Context, userID, profileID uuid.UUID) ([]db.ListPendingTransactionReviewsRow, error) {
	if err := s.assertProfileMember(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.reviews.ListPending(ctx, profileID)
}

// MarkTransactionForReview flags a variable transaction as a likely duplicate
// of matchedTransactionID — any Fixed-type transaction in the same period,
// whether spawned from a FixedExpense template or a SavingsSource. Matching
// against the transaction directly (rather than a FixedExpense template)
// means any Fixed-type transaction can be a match target with no separate
// savings-specific path.
func (s *TransactionService) MarkTransactionForReview(ctx context.Context, userID, txID, matchedTransactionID, profileID uuid.UUID) (db.TransactionReview, error) {
	if err := s.assertProfileCollaborator(ctx, profileID, userID); err != nil {
		return db.TransactionReview{}, err
	}
	tx, err := s.transactions.GetByID(ctx, txID)
	if err != nil {
		return db.TransactionReview{}, err
	}
	if tx.TransactionTypeID == nil || *tx.TransactionTypeID != 2 {
		return db.TransactionReview{}, apperr.Invalid("only variable transactions can be flagged for review")
	}
	if tx.BudgetPeriodID == nil {
		return db.TransactionReview{}, apperr.Invalid("transaction has no budget period")
	}
	period, err := s.profiles.GetPeriodByID(ctx, *tx.BudgetPeriodID)
	if err != nil || period.BudgetProfileID != profileID {
		return db.TransactionReview{}, apperr.Forbidden("transaction belongs to a different budget")
	}
	matched, err := s.transactions.GetByID(ctx, matchedTransactionID)
	if err != nil {
		return db.TransactionReview{}, err
	}
	if matched.TransactionTypeID == nil || *matched.TransactionTypeID != 1 {
		return db.TransactionReview{}, apperr.Invalid("can only match against a fixed transaction")
	}
	if matched.BudgetPeriodID == nil || *matched.BudgetPeriodID != *tx.BudgetPeriodID {
		return db.TransactionReview{}, apperr.Forbidden("matched transaction belongs to a different budget")
	}
	return s.reviews.Upsert(ctx, *tx.BudgetPeriodID, txID, matchedTransactionID, 100.0)
}

func (s *TransactionService) ConfirmTransactionReview(ctx context.Context, userID, reviewID, budgetProfileID uuid.UUID) error {
	review, err := s.reviews.GetByID(ctx, reviewID)
	if err != nil {
		return err
	}
	if err := s.assertPeriodMember(ctx, review.BudgetPeriodID, userID); err != nil {
		return err
	}

	matchedTx, mErr := s.transactions.GetByID(ctx, review.MatchedTransactionID)
	if mErr == nil {
		// Save alias so future Plaid imports of the same merchant name
		// auto-confirm — only meaningful when the match target was spawned
		// from a FixedExpense template; savings-derived transactions have no
		// template to alias against.
		if matchedTx.FixedExpenseID != nil {
			if importedTx, txErr := s.transactions.GetByID(ctx, review.TransactionID); txErr == nil && importedTx.Name != nil {
				_ = s.reviews.CreateAlias(ctx, *matchedTx.FixedExpenseID, *importedTx.Name)
			}
		}

		// Mark the matched transaction paid if it isn't already.
		if !matchedTx.IsPaid && matchedTx.BudgetPeriodID != nil {
			_, _ = s.transactions.MarkAsPaid(ctx, db.MarkTransactionAsPaidParams{
				ID:             matchedTx.ID,
				BudgetPeriodID: *matchedTx.BudgetPeriodID,
				Amount:         matchedTx.PlannedAmount,
				PaidDate:       matchedTx.Date,
			})
		}
	}

	// Mark the review confirmed — the variable transaction stays in the DB but
	// ListTransactions filters it out while status = 'confirmed'.
	return s.reviews.UpdateStatus(ctx, reviewID, "confirmed")
}

func (s *TransactionService) DismissTransactionReview(ctx context.Context, userID, reviewID uuid.UUID) error {
	review, err := s.reviews.GetByID(ctx, reviewID)
	if err != nil {
		return err
	}
	if err := s.assertPeriodMember(ctx, review.BudgetPeriodID, userID); err != nil {
		return err
	}
	return s.reviews.UpdateStatus(ctx, reviewID, "dismissed")
}
