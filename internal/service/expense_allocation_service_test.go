package service

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// ── Mock ExpenseAllocationRepository ─────────────────────────────────────────

type mockAllocationRepo struct {
	list   func(context.Context, uuid.UUID) ([]db.ExpenseAllocation, error)
	upsert func(context.Context, db.UpsertExpenseAllocationParams) (db.ExpenseAllocation, error)
	delete func(context.Context, db.DeleteExpenseAllocationParams) error
}

func (m *mockAllocationRepo) List(ctx context.Context, profileID uuid.UUID) ([]db.ExpenseAllocation, error) {
	if m.list != nil {
		return m.list(ctx, profileID)
	}
	return nil, nil
}
func (m *mockAllocationRepo) Upsert(ctx context.Context, arg db.UpsertExpenseAllocationParams) (db.ExpenseAllocation, error) {
	if m.upsert != nil {
		return m.upsert(ctx, arg)
	}
	return db.ExpenseAllocation{}, nil
}
func (m *mockAllocationRepo) Delete(ctx context.Context, arg db.DeleteExpenseAllocationParams) error {
	if m.delete != nil {
		return m.delete(ctx, arg)
	}
	return nil
}

// ── helpers ───────────────────────────────────────────────────────────────────

func profileOwnerRepo(profileID, ownerID uuid.UUID) *mockBudgetProfileRepo {
	return &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			if id == profileID {
				return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
			}
			return db.BudgetProfile{}, apperr.NotFound("budget_profile", id.String())
		},
		getPersonByUserID: func(_ context.Context, _ uuid.UUID, uid uuid.UUID) (db.BudgetToProfileMapping, error) {
			return db.BudgetToProfileMapping{}, apperr.NotFound("budget_person", uid.String())
		},
	}
}

func profileRepoWithMember(profileID, ownerID, memberID uuid.UUID, role string) *mockBudgetProfileRepo {
	return &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			if id == profileID {
				return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
			}
			return db.BudgetProfile{}, apperr.NotFound("budget_profile", id.String())
		},
		getPersonByUserID: func(_ context.Context, _ uuid.UUID, uid uuid.UUID) (db.BudgetToProfileMapping, error) {
			if uid == memberID {
				return db.BudgetToProfileMapping{Role: role}, nil
			}
			return db.BudgetToProfileMapping{}, apperr.NotFound("budget_person", uid.String())
		},
	}
}

// ── List ──────────────────────────────────────────────────────────────────────

func TestExpenseAllocation_List_Success(t *testing.T) {
	profileID := uuid.New()
	userID := uuid.New()
	expected := []db.ExpenseAllocation{{ID: 1, BudgetProfileID: profileID, CategoryID: 5}}

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{
			list: func(_ context.Context, id uuid.UUID) ([]db.ExpenseAllocation, error) {
				assert.Equal(t, profileID, id)
				return expected, nil
			},
		},
		profileOwnerRepo(profileID, userID),
	)

	got, err := svc.List(context.Background(), profileID, userID)
	require.NoError(t, err)
	assert.Equal(t, expected, got)
}

func TestExpenseAllocation_List_WrongUser(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	otherID := uuid.New()

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{},
		profileOwnerRepo(profileID, ownerID),
	)

	_, err := svc.List(context.Background(), profileID, otherID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

// ── Upsert ────────────────────────────────────────────────────────────────────

func TestExpenseAllocation_Upsert_Success(t *testing.T) {
	profileID := uuid.New()
	userID := uuid.New()
	personID := int32(7)
	amount := pgtype.Numeric{Valid: true}
	expected := db.ExpenseAllocation{ID: 1, BudgetProfileID: profileID, CategoryID: 3, BudgetPersonID: &personID}

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{
			upsert: func(_ context.Context, arg db.UpsertExpenseAllocationParams) (db.ExpenseAllocation, error) {
				assert.Equal(t, profileID, arg.BudgetProfileID)
				assert.Equal(t, int32(3), arg.CategoryID)
				assert.Equal(t, &personID, arg.BudgetPersonID)
				return expected, nil
			},
		},
		profileOwnerRepo(profileID, userID),
	)

	got, err := svc.Upsert(context.Background(), profileID, userID, 3, &personID, amount)
	require.NoError(t, err)
	assert.Equal(t, expected, got)
}

func TestExpenseAllocation_Upsert_WrongUser(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	otherID := uuid.New()

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{},
		profileOwnerRepo(profileID, ownerID),
	)

	_, err := svc.Upsert(context.Background(), profileID, otherID, 1, nil, pgtype.Numeric{Valid: true})
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

// ── Delete ────────────────────────────────────────────────────────────────────

func TestExpenseAllocation_Delete_Success(t *testing.T) {
	profileID := uuid.New()
	userID := uuid.New()
	var deletedID int32

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{
			delete: func(_ context.Context, arg db.DeleteExpenseAllocationParams) error {
				deletedID = arg.ID
				assert.Equal(t, profileID, arg.BudgetProfileID)
				return nil
			},
		},
		profileOwnerRepo(profileID, userID),
	)

	err := svc.Delete(context.Background(), 42, profileID, userID)
	require.NoError(t, err)
	assert.Equal(t, int32(42), deletedID)
}

func TestExpenseAllocation_Delete_WrongUser(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	otherID := uuid.New()

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{},
		profileOwnerRepo(profileID, ownerID),
	)

	err := svc.Delete(context.Background(), 1, profileID, otherID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

// ── Role-based access ─────────────────────────────────────────────────────────

func TestExpenseAllocation_List_CollaboratorAllowed(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	collaboratorID := uuid.New()
	expected := []db.ExpenseAllocation{{ID: 1, BudgetProfileID: profileID, CategoryID: 5}}

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) { return expected, nil },
		},
		profileRepoWithMember(profileID, ownerID, collaboratorID, "collaborator"),
	)

	got, err := svc.List(context.Background(), profileID, collaboratorID)
	require.NoError(t, err)
	assert.Equal(t, expected, got)
}

func TestExpenseAllocation_List_ViewerAllowed(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	viewerID := uuid.New()
	expected := []db.ExpenseAllocation{{ID: 2, BudgetProfileID: profileID, CategoryID: 3}}

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) { return expected, nil },
		},
		profileRepoWithMember(profileID, ownerID, viewerID, "viewer"),
	)

	got, err := svc.List(context.Background(), profileID, viewerID)
	require.NoError(t, err)
	assert.Equal(t, expected, got)
}

func TestExpenseAllocation_Upsert_CollaboratorAllowed(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	collaboratorID := uuid.New()

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{},
		profileRepoWithMember(profileID, ownerID, collaboratorID, "collaborator"),
	)

	_, err := svc.Upsert(context.Background(), profileID, collaboratorID, 1, nil, pgtype.Numeric{Valid: true})
	require.NoError(t, err)
}

func TestExpenseAllocation_Upsert_ViewerForbidden(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	viewerID := uuid.New()

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{},
		profileRepoWithMember(profileID, ownerID, viewerID, "viewer"),
	)

	_, err := svc.Upsert(context.Background(), profileID, viewerID, 1, nil, pgtype.Numeric{Valid: true})
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestExpenseAllocation_Delete_CollaboratorAllowed(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	collaboratorID := uuid.New()

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{},
		profileRepoWithMember(profileID, ownerID, collaboratorID, "collaborator"),
	)

	err := svc.Delete(context.Background(), 1, profileID, collaboratorID)
	require.NoError(t, err)
}

func TestExpenseAllocation_Delete_ViewerForbidden(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	viewerID := uuid.New()

	svc := NewExpenseAllocationService(
		&mockAllocationRepo{},
		profileRepoWithMember(profileID, ownerID, viewerID, "viewer"),
	)

	err := svc.Delete(context.Background(), 1, profileID, viewerID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}
