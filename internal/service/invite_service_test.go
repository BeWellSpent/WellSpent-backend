package service

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/config"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"go.uber.org/zap"
)

// ── Mock invite repo ──────────────────────────────────────────────────────────

type mockInviteRepo struct {
	create       func(context.Context, db.CreateInviteParams) (db.BudgetInvite, error)
	getByToken   func(context.Context, uuid.UUID) (db.GetInviteByTokenRow, error)
	getByID      func(context.Context, uuid.UUID) (db.BudgetInvite, error)
	listByProfile func(context.Context, uuid.UUID) ([]db.BudgetInvite, error)
	updateStatus func(context.Context, db.UpdateInviteStatusParams) (db.BudgetInvite, error)
}

func (m *mockInviteRepo) Create(ctx context.Context, arg db.CreateInviteParams) (db.BudgetInvite, error) {
	if m.create != nil {
		return m.create(ctx, arg)
	}
	return db.BudgetInvite{
		ID:              uuid.New(),
		BudgetProfileID: arg.BudgetProfileID,
		Email:           arg.Email,
		Role:            arg.Role,
		Token:           uuid.New(),
		Status:          "pending",
		InvitedBy:       arg.InvitedBy,
		ExpiresAt:       arg.ExpiresAt,
	}, nil
}
func (m *mockInviteRepo) GetByToken(ctx context.Context, token uuid.UUID) (db.GetInviteByTokenRow, error) {
	if m.getByToken != nil {
		return m.getByToken(ctx, token)
	}
	return db.GetInviteByTokenRow{}, apperr.NotFound("invite", token.String())
}
func (m *mockInviteRepo) GetByID(ctx context.Context, id uuid.UUID) (db.BudgetInvite, error) {
	if m.getByID != nil {
		return m.getByID(ctx, id)
	}
	return db.BudgetInvite{}, apperr.NotFound("invite", id.String())
}
func (m *mockInviteRepo) ListByProfile(ctx context.Context, profileID uuid.UUID) ([]db.BudgetInvite, error) {
	if m.listByProfile != nil {
		return m.listByProfile(ctx, profileID)
	}
	return nil, nil
}
func (m *mockInviteRepo) UpdateStatus(ctx context.Context, arg db.UpdateInviteStatusParams) (db.BudgetInvite, error) {
	if m.updateStatus != nil {
		return m.updateStatus(ctx, arg)
	}
	return db.BudgetInvite{ID: arg.ID, Status: arg.Status}, nil
}

// ── Helpers ───────────────────────────────────────────────────────────────────

func newInviteSvc(invRepo *mockInviteRepo, profRepo *mockBudgetProfileRepo) *InviteService {
	if profRepo == nil {
		profRepo = &mockBudgetProfileRepo{}
	}
	return NewInviteService(invRepo, profRepo, &mockUserRepo{}, &config.Config{}, zap.NewNop())
}

func futureTS() pgtype.Timestamptz {
	return pgtype.Timestamptz{Time: time.Now().UTC().Add(inviteTTL), Valid: true}
}

// ── Tests ─────────────────────────────────────────────────────────────────────

func TestInviteSend_Success(t *testing.T) {
	ownerID := uuid.New()
	profileID := uuid.New()
	inviteID := uuid.New()

	profRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: ownerID}, nil
		},
	}
	invRepo := &mockInviteRepo{
		create: func(_ context.Context, arg db.CreateInviteParams) (db.BudgetInvite, error) {
			assert.Equal(t, profileID, arg.BudgetProfileID)
			assert.Equal(t, "collaborator", arg.Role)
			assert.Equal(t, ownerID, arg.InvitedBy)
			return db.BudgetInvite{ID: inviteID, BudgetProfileID: profileID, Role: "collaborator", Status: "pending"}, nil
		},
	}

	svc := newInviteSvc(invRepo, profRepo)
	inv, err := svc.Send(context.Background(), profileID, ownerID, "test@example.com", "collaborator", 0)
	require.NoError(t, err)
	assert.Equal(t, inviteID, inv.ID)
	assert.Equal(t, "pending", inv.Status)
}

func TestInviteSend_ForbiddenNonAdmin(t *testing.T) {
	ownerID := uuid.New()
	callerID := uuid.New()
	profileID := uuid.New()

	profRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: ownerID}, nil
		},
		getPersonByUserID: func(_ context.Context, _, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
			return db.BudgetToProfileMapping{Role: "collaborator"}, nil
		},
	}

	svc := newInviteSvc(&mockInviteRepo{}, profRepo)
	_, err := svc.Send(context.Background(), profileID, callerID, "test@example.com", "collaborator", 0)
	require.Error(t, err)
	var forbiddenErr *apperr.ForbiddenError
	assert.True(t, errors.As(err, &forbiddenErr))
}

func TestInviteGetByToken_Success(t *testing.T) {
	token := uuid.New()
	profileID := uuid.New()

	invRepo := &mockInviteRepo{
		getByToken: func(_ context.Context, t uuid.UUID) (db.GetInviteByTokenRow, error) {
			return db.GetInviteByTokenRow{
				ID:              uuid.New(),
				BudgetProfileID: profileID,
				Token:           t,
				Status:          "pending",
				ExpiresAt:       futureTS(),
				BudgetName:      "Home Budget",
				InviterName:     "Alice",
			}, nil
		},
	}

	svc := newInviteSvc(invRepo, nil)
	row, err := svc.GetByToken(context.Background(), token)
	require.NoError(t, err)
	assert.Equal(t, "Home Budget", row.BudgetName)
}

func TestInviteGetByToken_Cancelled(t *testing.T) {
	token := uuid.New()
	invRepo := &mockInviteRepo{
		getByToken: func(_ context.Context, _ uuid.UUID) (db.GetInviteByTokenRow, error) {
			return db.GetInviteByTokenRow{Status: "cancelled", ExpiresAt: futureTS()}, nil
		},
	}
	svc := newInviteSvc(invRepo, nil)
	_, err := svc.GetByToken(context.Background(), token)
	require.Error(t, err)
}

func TestInviteGetByToken_Expired(t *testing.T) {
	token := uuid.New()
	invRepo := &mockInviteRepo{
		getByToken: func(_ context.Context, _ uuid.UUID) (db.GetInviteByTokenRow, error) {
			return db.GetInviteByTokenRow{
				ID:        uuid.New(),
				Status:    "pending",
				ExpiresAt: pgtype.Timestamptz{Time: time.Now().UTC().Add(-time.Hour), Valid: true},
			}, nil
		},
		updateStatus: func(_ context.Context, arg db.UpdateInviteStatusParams) (db.BudgetInvite, error) {
			assert.Equal(t, "expired", arg.Status)
			return db.BudgetInvite{ID: arg.ID, Status: "expired"}, nil
		},
	}
	svc := newInviteSvc(invRepo, nil)
	_, err := svc.GetByToken(context.Background(), token)
	require.Error(t, err)
}

func TestInviteAccept_CreatesNewPerson(t *testing.T) {
	token := uuid.New()
	profileID := uuid.New()
	callerID := uuid.New()
	firstName := "Bob"

	invRow := db.GetInviteByTokenRow{
		ID:              uuid.New(),
		BudgetProfileID: profileID,
		Token:           token,
		Status:          "pending",
		Role:            "collaborator",
		ExpiresAt:       futureTS(),
		BudgetName:      "Home Budget",
		InviterName:     "Alice",
	}

	profRepo := &mockBudgetProfileRepo{
		existsPersonForUser: func(_ context.Context, _, _ uuid.UUID) (bool, error) {
			return false, nil
		},
		addPerson: func(_ context.Context, arg db.AddBudgetPersonToProfileParams) (db.BudgetToProfileMapping, error) {
			assert.Equal(t, profileID, arg.BudgetProfileID)
			assert.Equal(t, "collaborator", arg.Role)
			return db.BudgetToProfileMapping{BudgetProfileID: profileID, Role: "collaborator"}, nil
		},
	}
	invRepo := &mockInviteRepo{
		getByToken: func(_ context.Context, _ uuid.UUID) (db.GetInviteByTokenRow, error) {
			return invRow, nil
		},
	}
	userRepo := &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
			return db.User{FirstName: &firstName, Email: "bob@example.com"}, nil
		},
	}

	svc := NewInviteService(invRepo, profRepo, userRepo, &config.Config{}, zap.NewNop())
	returnedProfileID, err := svc.Accept(context.Background(), token, callerID)
	require.NoError(t, err)
	assert.Equal(t, profileID, returnedProfileID)
}

func TestInviteAccept_LinksExistingPerson(t *testing.T) {
	token := uuid.New()
	profileID := uuid.New()
	callerID := uuid.New()
	personID := int64(42)

	invRow := db.GetInviteByTokenRow{
		ID:              uuid.New(),
		BudgetProfileID: profileID,
		Token:           token,
		Status:          "pending",
		Role:            "collaborator",
		ExpiresAt:       futureTS(),
		BudgetPersonID:  &personID,
	}

	profRepo := &mockBudgetProfileRepo{
		existsPersonForUser: func(_ context.Context, _, _ uuid.UUID) (bool, error) {
			return false, nil
		},
		linkPersonToUser: func(_ context.Context, arg db.LinkBudgetPersonToUserParams) (db.BudgetToProfileMapping, error) {
			assert.Equal(t, int32(personID), arg.ID)
			assert.Equal(t, callerID, arg.UserID)
			assert.Equal(t, "collaborator", arg.Role)
			return db.BudgetToProfileMapping{ID: int32(personID), UserID: &callerID, Role: "collaborator"}, nil
		},
	}
	invRepo := &mockInviteRepo{
		getByToken: func(_ context.Context, _ uuid.UUID) (db.GetInviteByTokenRow, error) {
			return invRow, nil
		},
	}

	svc := NewInviteService(invRepo, profRepo, &mockUserRepo{}, &config.Config{}, zap.NewNop())
	returnedProfileID, err := svc.Accept(context.Background(), token, callerID)
	require.NoError(t, err)
	assert.Equal(t, profileID, returnedProfileID)
}

func TestInviteCancel_Success(t *testing.T) {
	ownerID := uuid.New()
	inviteID := uuid.New()
	profileID := uuid.New()

	profRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: ownerID}, nil
		},
	}
	invRepo := &mockInviteRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetInvite, error) {
			return db.BudgetInvite{ID: inviteID, BudgetProfileID: profileID, Status: "pending"}, nil
		},
		updateStatus: func(_ context.Context, arg db.UpdateInviteStatusParams) (db.BudgetInvite, error) {
			assert.Equal(t, "cancelled", arg.Status)
			return db.BudgetInvite{ID: arg.ID, Status: "cancelled"}, nil
		},
	}

	svc := newInviteSvc(invRepo, profRepo)
	inv, err := svc.Cancel(context.Background(), inviteID, ownerID)
	require.NoError(t, err)
	assert.Equal(t, "cancelled", inv.Status)
}

func TestInviteCancel_AlreadyAccepted(t *testing.T) {
	ownerID := uuid.New()
	inviteID := uuid.New()
	profileID := uuid.New()

	profRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: ownerID}, nil
		},
	}
	invRepo := &mockInviteRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetInvite, error) {
			return db.BudgetInvite{ID: inviteID, BudgetProfileID: profileID, Status: "accepted"}, nil
		},
	}

	svc := newInviteSvc(invRepo, profRepo)
	_, err := svc.Cancel(context.Background(), inviteID, ownerID)
	require.Error(t, err)
	var invalidErr *apperr.ValidationError
	assert.True(t, errors.As(err, &invalidErr))
}
