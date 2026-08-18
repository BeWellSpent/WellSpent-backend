package service

import (
	"context"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
)

type ExpenseAllocationService struct {
	allocations repository.ExpenseAllocationRepository
	profiles    repository.BudgetProfileRepository
}

func NewExpenseAllocationService(
	allocations repository.ExpenseAllocationRepository,
	profiles repository.BudgetProfileRepository,
) *ExpenseAllocationService {
	if allocations == nil {
		panic("NewExpenseAllocationService: allocations is required")
	}
	if profiles == nil {
		panic("NewExpenseAllocationService: profiles is required")
	}
	return &ExpenseAllocationService{allocations: allocations, profiles: profiles}
}

func (s *ExpenseAllocationService) getUserRole(ctx context.Context, profileID, userID uuid.UUID) (string, error) {
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

func (s *ExpenseAllocationService) assertMember(ctx context.Context, profileID, userID uuid.UUID) error {
	_, err := s.getUserRole(ctx, profileID, userID)
	return err
}

func (s *ExpenseAllocationService) assertCollaborator(ctx context.Context, profileID, userID uuid.UUID) error {
	role, err := s.getUserRole(ctx, profileID, userID)
	if err != nil {
		return err
	}
	if role != "admin" && role != "collaborator" {
		return apperr.Forbidden("access denied")
	}
	return nil
}

func (s *ExpenseAllocationService) List(ctx context.Context, profileID, userID uuid.UUID) ([]db.ExpenseAllocation, error) {
	if err := s.assertMember(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.allocations.List(ctx, profileID)
}

func (s *ExpenseAllocationService) Upsert(ctx context.Context, profileID, userID uuid.UUID, categoryID int32, personID *int32, amount pgtype.Numeric) (db.ExpenseAllocation, error) {
	if err := s.assertCollaborator(ctx, profileID, userID); err != nil {
		return db.ExpenseAllocation{}, err
	}
	return s.allocations.Upsert(ctx, db.UpsertExpenseAllocationParams{
		BudgetProfileID: profileID,
		CategoryID:      categoryID,
		BudgetPersonID:  personID,
		PlannedAmount:   amount,
	})
}

func (s *ExpenseAllocationService) Delete(ctx context.Context, id int32, profileID, userID uuid.UUID) error {
	if err := s.assertCollaborator(ctx, profileID, userID); err != nil {
		return err
	}
	return s.allocations.Delete(ctx, db.DeleteExpenseAllocationParams{
		ID:              id,
		BudgetProfileID: profileID,
	})
}
