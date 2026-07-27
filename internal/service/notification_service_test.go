package service

import (
	"context"
	"testing"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"go.uber.org/zap"
)

// Ensure mockNotifRepo satisfies the interface (compile-time check).
var _ repository.NotificationRepository = (*mockNotifRepo)(nil)

// ─── Notification repo mock ───────────────────────────────────────────────────

type mockNotifRepo struct {
	listSubscriptions    func(context.Context, uuid.UUID, uuid.UUID) ([]db.AlertSubscription, error)
	getSubscription      func(context.Context, uuid.UUID, uuid.UUID) (db.AlertSubscription, error)
	upsertSubscription   func(context.Context, db.UpsertAlertSubscriptionParams) (db.AlertSubscription, error)
	deleteSubscription   func(context.Context, uuid.UUID, uuid.UUID) error
	getBudgetSubscribers func(context.Context, uuid.UUID, string) ([]db.AlertSubscription, error)
	create               func(context.Context, db.CreateNotificationParams) (db.Notification, error)
	list                 func(context.Context, uuid.UUID, *uuid.UUID, int32) ([]db.Notification, error)
	markRead             func(context.Context, uuid.UUID, []uuid.UUID) error
	getUnreadCount       func(context.Context, uuid.UUID) (int32, error)
}

func (m *mockNotifRepo) ListSubscriptions(ctx context.Context, userID, profileID uuid.UUID) ([]db.AlertSubscription, error) {
	if m.listSubscriptions != nil {
		return m.listSubscriptions(ctx, userID, profileID)
	}
	return nil, nil
}
func (m *mockNotifRepo) GetSubscription(ctx context.Context, id, userID uuid.UUID) (db.AlertSubscription, error) {
	if m.getSubscription != nil {
		return m.getSubscription(ctx, id, userID)
	}
	return db.AlertSubscription{}, nil
}
func (m *mockNotifRepo) UpsertSubscription(ctx context.Context, arg db.UpsertAlertSubscriptionParams) (db.AlertSubscription, error) {
	if m.upsertSubscription != nil {
		return m.upsertSubscription(ctx, arg)
	}
	return db.AlertSubscription{ID: uuid.New()}, nil
}
func (m *mockNotifRepo) DeleteSubscription(ctx context.Context, id, userID uuid.UUID) error {
	if m.deleteSubscription != nil {
		return m.deleteSubscription(ctx, id, userID)
	}
	return nil
}
func (m *mockNotifRepo) GetBudgetSubscribers(ctx context.Context, profileID uuid.UUID, alertType string) ([]db.AlertSubscription, error) {
	if m.getBudgetSubscribers != nil {
		return m.getBudgetSubscribers(ctx, profileID, alertType)
	}
	return nil, nil
}
func (m *mockNotifRepo) Create(ctx context.Context, arg db.CreateNotificationParams) (db.Notification, error) {
	if m.create != nil {
		return m.create(ctx, arg)
	}
	return db.Notification{ID: uuid.New()}, nil
}
func (m *mockNotifRepo) List(ctx context.Context, userID uuid.UUID, profileID *uuid.UUID, limit int32) ([]db.Notification, error) {
	if m.list != nil {
		return m.list(ctx, userID, profileID, limit)
	}
	return nil, nil
}
func (m *mockNotifRepo) MarkRead(ctx context.Context, userID uuid.UUID, ids []uuid.UUID) error {
	if m.markRead != nil {
		return m.markRead(ctx, userID, ids)
	}
	return nil
}
func (m *mockNotifRepo) GetUnreadCount(ctx context.Context, userID uuid.UUID) (int32, error) {
	if m.getUnreadCount != nil {
		return m.getUnreadCount(ctx, userID)
	}
	return 0, nil
}

// ─── Helper ───────────────────────────────────────────────────────────────────

func newTestNotifSvc(notifRepo repository.NotificationRepository, profileRepo repository.BudgetProfileRepository) *NotificationService {
	return newTestNotifSvcWithUser(notifRepo, profileRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
			return db.User{Plan: "pro"}, nil
		},
	})
}

func newTestNotifSvcWithUser(notifRepo repository.NotificationRepository, profileRepo repository.BudgetProfileRepository, userRepo *mockUserRepo) *NotificationService {
	return NewNotificationService(
		notifRepo,
		&mockTransactionRepo{},
		profileRepo,
		&mockExpenseAllocationRepo{},
		userRepo,
		&config.Config{},
		zap.NewNop(),
	)
}

// ─── Tests ────────────────────────────────────────────────────────────────────

func TestNotificationService_ListSubscriptions_Success(t *testing.T) {
	profileID := uuid.New()
	userID := uuid.New()
	expected := []db.AlertSubscription{{ID: uuid.New(), AlertType: "new_transaction"}}

	notifRepo := &mockNotifRepo{
		listSubscriptions: func(_ context.Context, u, p uuid.UUID) ([]db.AlertSubscription, error) {
			assert.Equal(t, userID, u)
			assert.Equal(t, profileID, p)
			return expected, nil
		},
	}
	profileRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: userID}, nil
		},
	}
	svc := newTestNotifSvc(notifRepo, profileRepo)

	subs, err := svc.ListSubscriptions(context.Background(), userID, profileID)
	require.NoError(t, err)
	assert.Len(t, subs, 1)
}

func TestNotificationService_UpsertSubscription_Success(t *testing.T) {
	profileID := uuid.New()
	userID := uuid.New()
	subID := uuid.New()

	notifRepo := &mockNotifRepo{
		upsertSubscription: func(_ context.Context, arg db.UpsertAlertSubscriptionParams) (db.AlertSubscription, error) {
			assert.Equal(t, userID, arg.UserID)
			assert.Equal(t, profileID, arg.BudgetProfileID)
			assert.Equal(t, "new_transaction", arg.AlertType)
			assert.Equal(t, "in_app", arg.Channel)
			return db.AlertSubscription{ID: subID, AlertType: "new_transaction"}, nil
		},
	}
	profileRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: userID}, nil
		},
	}
	svc := newTestNotifSvc(notifRepo, profileRepo)

	sub, err := svc.UpsertSubscription(context.Background(), userID, UpsertSubscriptionInput{
		ProfileID: profileID,
		AlertType: "new_transaction",
		Channel:   "in_app",
	})
	require.NoError(t, err)
	assert.Equal(t, subID, sub.ID)
}

func TestNotificationService_DeleteSubscription_Success(t *testing.T) {
	subID := uuid.New()
	userID := uuid.New()
	deleted := false

	notifRepo := &mockNotifRepo{
		deleteSubscription: func(_ context.Context, id, u uuid.UUID) error {
			assert.Equal(t, subID, id)
			assert.Equal(t, userID, u)
			deleted = true
			return nil
		},
	}
	svc := newTestNotifSvc(notifRepo, &mockBudgetProfileRepo{})

	err := svc.DeleteSubscription(context.Background(), userID, subID)
	require.NoError(t, err)
	assert.True(t, deleted)
}

func TestNotificationService_HandleNewTransaction_NotifiesOtherMembers(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	callerID := uuid.New()
	memberID := uuid.New()
	txName := "Grocery Run"

	created := []db.Notification{}
	notifRepo := &mockNotifRepo{
		getBudgetSubscribers: func(_ context.Context, pid uuid.UUID, alertType string) ([]db.AlertSubscription, error) {
			if alertType == "new_transaction" {
				return []db.AlertSubscription{
					{ID: uuid.New(), UserID: memberID, AlertType: "new_transaction", Channel: "in_app"},
					{ID: uuid.New(), UserID: callerID, AlertType: "new_transaction", Channel: "in_app"}, // caller — must be skipped
				}, nil
			}
			return nil, nil
		},
		create: func(_ context.Context, arg db.CreateNotificationParams) (db.Notification, error) {
			created = append(created, db.Notification{UserID: arg.UserID})
			return db.Notification{ID: uuid.New()}, nil
		},
	}
	profileRepo := &mockBudgetProfileRepo{
		getPeriodByID: func(_ context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
			return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
		},
	}
	svc := newTestNotifSvc(notifRepo, profileRepo)

	tx := db.Transaction{Name: &txName}
	svc.HandleNewTransaction(context.Background(), tx, periodID, callerID)

	// Only the other member should receive a notification, not the caller.
	require.Len(t, created, 1)
	assert.Equal(t, memberID, created[0].UserID)
}

func TestNotificationService_HandlePeriodCreated_NotifiesSubscribers(t *testing.T) {
	profileID := uuid.New()
	subscriberID := uuid.New()

	created := []db.Notification{}
	notifRepo := &mockNotifRepo{
		getBudgetSubscribers: func(_ context.Context, _ uuid.UUID, alertType string) ([]db.AlertSubscription, error) {
			if alertType == "period_created" {
				return []db.AlertSubscription{
					{ID: uuid.New(), UserID: subscriberID, AlertType: "period_created", Channel: "in_app"},
				}, nil
			}
			return nil, nil
		},
		create: func(_ context.Context, arg db.CreateNotificationParams) (db.Notification, error) {
			created = append(created, db.Notification{UserID: arg.UserID})
			return db.Notification{ID: uuid.New()}, nil
		},
	}
	profileRepo := &mockBudgetProfileRepo{}
	svc := newTestNotifSvc(notifRepo, profileRepo)

	period := db.BudgetPeriod{
		ID:              uuid.New(),
		BudgetProfileID: profileID,
		StartDate:       pgtype.Date{Valid: true},
		EndDate:         pgtype.Date{Valid: true},
	}
	svc.HandlePeriodCreated(context.Background(), period)

	require.Len(t, created, 1)
	assert.Equal(t, subscriberID, created[0].UserID)
}

func TestNotificationService_GetUnreadCount_Success(t *testing.T) {
	userID := uuid.New()

	notifRepo := &mockNotifRepo{
		getUnreadCount: func(_ context.Context, id uuid.UUID) (int32, error) {
			assert.Equal(t, userID, id)
			return 7, nil
		},
	}
	svc := newTestNotifSvc(notifRepo, &mockBudgetProfileRepo{})

	count, err := svc.GetUnreadCount(context.Background(), userID)
	require.NoError(t, err)
	assert.Equal(t, int32(7), count)
}

func TestUpsertSubscription_FreeTier_BlocksNewTransaction(t *testing.T) {
	profileID := uuid.New()
	userID := uuid.New()

	profileRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: userID}, nil
		},
	}
	userRepo := &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
			return db.User{Plan: "free"}, nil
		},
	}
	svc := newTestNotifSvcWithUser(&mockNotifRepo{}, profileRepo, userRepo)

	_, err := svc.UpsertSubscription(context.Background(), userID, UpsertSubscriptionInput{
		ProfileID: profileID,
		AlertType: "new_transaction",
		Channel:   "in_app",
	})
	require.Error(t, err)
	var valErr *apperr.ValidationError
	require.ErrorAs(t, err, &valErr)
}

func TestUpsertSubscription_FreeTier_BlocksThirdSubscription(t *testing.T) {
	profileID := uuid.New()
	userID := uuid.New()

	profileRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: userID}, nil
		},
	}
	userRepo := &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
			return db.User{Plan: "free"}, nil
		},
	}
	notifRepo := &mockNotifRepo{
		listSubscriptions: func(_ context.Context, _, _ uuid.UUID) ([]db.AlertSubscription, error) {
			return []db.AlertSubscription{
				{ID: uuid.New(), AlertType: "spending_threshold"},
				{ID: uuid.New(), AlertType: "period_created"},
			}, nil
		},
	}
	svc := newTestNotifSvcWithUser(notifRepo, profileRepo, userRepo)

	_, err := svc.UpsertSubscription(context.Background(), userID, UpsertSubscriptionInput{
		ProfileID: profileID,
		AlertType: "review_pending",
		Channel:   "in_app",
	})
	require.Error(t, err)
	var valErr *apperr.ValidationError
	require.ErrorAs(t, err, &valErr)
}

func TestUpsertSubscription_FreeTier_AllowsUpdateExisting(t *testing.T) {
	profileID := uuid.New()
	userID := uuid.New()
	subID := uuid.New()

	profileRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: userID}, nil
		},
	}
	userRepo := &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
			return db.User{Plan: "free"}, nil
		},
	}
	notifRepo := &mockNotifRepo{
		listSubscriptions: func(_ context.Context, _, _ uuid.UUID) ([]db.AlertSubscription, error) {
			return []db.AlertSubscription{
				{ID: uuid.New(), AlertType: "spending_threshold"},
				{ID: uuid.New(), AlertType: "period_created"},
			}, nil
		},
		upsertSubscription: func(_ context.Context, _ db.UpsertAlertSubscriptionParams) (db.AlertSubscription, error) {
			return db.AlertSubscription{ID: subID, AlertType: "spending_threshold"}, nil
		},
	}
	svc := newTestNotifSvcWithUser(notifRepo, profileRepo, userRepo)

	// Updating an existing subscription type should succeed even at the 2-sub limit.
	sub, err := svc.UpsertSubscription(context.Background(), userID, UpsertSubscriptionInput{
		ProfileID:    profileID,
		AlertType:    "spending_threshold",
		Channel:      "email",
		ThresholdPct: 80,
	})
	require.NoError(t, err)
	assert.Equal(t, subID, sub.ID)
}
