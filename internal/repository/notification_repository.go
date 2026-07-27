package repository

import (
	"context"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
)

type NotificationRepository interface {
	// Subscriptions
	ListSubscriptions(ctx context.Context, userID, profileID uuid.UUID) ([]db.AlertSubscription, error)
	GetSubscription(ctx context.Context, id, userID uuid.UUID) (db.AlertSubscription, error)
	UpsertSubscription(ctx context.Context, arg db.UpsertAlertSubscriptionParams) (db.AlertSubscription, error)
	DeleteSubscription(ctx context.Context, id, userID uuid.UUID) error
	GetBudgetSubscribers(ctx context.Context, profileID uuid.UUID, alertType string) ([]db.AlertSubscription, error)

	// Notifications
	Create(ctx context.Context, arg db.CreateNotificationParams) (db.Notification, error)
	List(ctx context.Context, userID uuid.UUID, profileID *uuid.UUID, limit int32) ([]db.Notification, error)
	MarkRead(ctx context.Context, userID uuid.UUID, ids []uuid.UUID) error
	GetUnreadCount(ctx context.Context, userID uuid.UUID) (int32, error)
}

type notificationRepository struct {
	q *db.Queries
}

func NewNotificationRepository(q *db.Queries) NotificationRepository {
	return &notificationRepository{q: q}
}

func (r *notificationRepository) ListSubscriptions(ctx context.Context, userID, profileID uuid.UUID) ([]db.AlertSubscription, error) {
	return r.q.ListAlertSubscriptions(ctx, db.ListAlertSubscriptionsParams{
		UserID:          userID,
		BudgetProfileID: profileID,
	})
}

func (r *notificationRepository) GetSubscription(ctx context.Context, id, userID uuid.UUID) (db.AlertSubscription, error) {
	s, err := r.q.GetAlertSubscription(ctx, db.GetAlertSubscriptionParams{ID: id, UserID: userID})
	if err != nil {
		if err == pgx.ErrNoRows {
			return db.AlertSubscription{}, apperr.NotFound("alert_subscription", id.String())
		}
		return db.AlertSubscription{}, err
	}
	return s, nil
}

func (r *notificationRepository) UpsertSubscription(ctx context.Context, arg db.UpsertAlertSubscriptionParams) (db.AlertSubscription, error) {
	return r.q.UpsertAlertSubscription(ctx, arg)
}

func (r *notificationRepository) DeleteSubscription(ctx context.Context, id, userID uuid.UUID) error {
	return r.q.DeleteAlertSubscription(ctx, db.DeleteAlertSubscriptionParams{ID: id, UserID: userID})
}

func (r *notificationRepository) GetBudgetSubscribers(ctx context.Context, profileID uuid.UUID, alertType string) ([]db.AlertSubscription, error) {
	return r.q.GetBudgetAlertSubscribers(ctx, db.GetBudgetAlertSubscribersParams{
		BudgetProfileID: profileID,
		AlertType:       alertType,
	})
}

func (r *notificationRepository) Create(ctx context.Context, arg db.CreateNotificationParams) (db.Notification, error) {
	return r.q.CreateNotification(ctx, arg)
}

func (r *notificationRepository) List(ctx context.Context, userID uuid.UUID, profileID *uuid.UUID, limit int32) ([]db.Notification, error) {
	// ListNotifications uses a nullable uuid filter; pass uuid.Nil when no filter is set.
	filterID := uuid.Nil
	if profileID != nil {
		filterID = *profileID
	}
	if limit <= 0 {
		limit = 50
	}
	return r.q.ListNotifications(ctx, db.ListNotificationsParams{
		UserID:          userID,
		BudgetProfileID: filterID,
		LimitVal:        limit,
	})
}

func (r *notificationRepository) MarkRead(ctx context.Context, userID uuid.UUID, ids []uuid.UUID) error {
	return r.q.MarkNotificationsRead(ctx, db.MarkNotificationsReadParams{
		UserID: userID,
		Ids:    ids,
	})
}

func (r *notificationRepository) GetUnreadCount(ctx context.Context, userID uuid.UUID) (int32, error) {
	return r.q.GetUnreadCount(ctx, userID)
}

