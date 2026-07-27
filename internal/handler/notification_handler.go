package handler

import (
	"context"
	"fmt"

	"connectrpc.com/connect"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/middleware"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"google.golang.org/protobuf/types/known/timestamppb"
)

type NotificationHandler struct {
	svc *service.NotificationService
}

func NewNotificationHandler(svc *service.NotificationService) *NotificationHandler {
	return &NotificationHandler{svc: svc}
}

func (h *NotificationHandler) ListNotifications(ctx context.Context, req *connect.Request[v1.ListNotificationsRequest]) (*connect.Response[v1.ListNotificationsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	var profileID *uuid.UUID
	if req.Msg.BudgetProfileId != "" {
		id, parseErr := uuid.Parse(req.Msg.BudgetProfileId)
		if parseErr == nil {
			profileID = &id
		}
	}
	limit := req.Msg.Limit
	if limit <= 0 {
		limit = 50
	}
	notifs, unreadCount, svcErr := h.svc.ListNotifications(ctx, userID, profileID, limit)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.Notification, len(notifs))
	for i, n := range notifs {
		protos[i] = toProtoNotification(n)
	}
	return connect.NewResponse(&v1.ListNotificationsResponse{
		Notifications: protos,
		UnreadCount:   unreadCount,
	}), nil
}

func (h *NotificationHandler) MarkNotificationsRead(ctx context.Context, req *connect.Request[v1.MarkNotificationsReadRequest]) (*connect.Response[v1.MarkNotificationsReadResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	ids := make([]uuid.UUID, 0, len(req.Msg.Ids))
	for _, raw := range req.Msg.Ids {
		id, parseErr := uuid.Parse(raw)
		if parseErr == nil {
			ids = append(ids, id)
		}
	}
	if svcErr := h.svc.MarkRead(ctx, userID, ids); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.MarkNotificationsReadResponse{}), nil
}

func (h *NotificationHandler) GetUnreadCount(ctx context.Context, _ *connect.Request[v1.GetUnreadCountRequest]) (*connect.Response[v1.GetUnreadCountResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	count, svcErr := h.svc.GetUnreadCount(ctx, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.GetUnreadCountResponse{Count: count}), nil
}

func (h *NotificationHandler) ListAlertSubscriptions(ctx context.Context, req *connect.Request[v1.ListAlertSubscriptionsRequest]) (*connect.Response[v1.ListAlertSubscriptionsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, parseErr := uuid.Parse(req.Msg.BudgetProfileId)
	if parseErr != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, fmt.Errorf("invalid budget_profile_id"))
	}
	subs, svcErr := h.svc.ListSubscriptions(ctx, userID, profileID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.AlertSubscription, len(subs))
	for i, s := range subs {
		protos[i] = toProtoAlertSubscription(s)
	}
	return connect.NewResponse(&v1.ListAlertSubscriptionsResponse{Subscriptions: protos}), nil
}

func (h *NotificationHandler) UpsertAlertSubscription(ctx context.Context, req *connect.Request[v1.UpsertAlertSubscriptionRequest]) (*connect.Response[v1.UpsertAlertSubscriptionResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, parseErr := uuid.Parse(req.Msg.BudgetProfileId)
	if parseErr != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, fmt.Errorf("invalid budget_profile_id"))
	}
	var catID *int32
	if req.Msg.CategoryId != 0 {
		cid := req.Msg.CategoryId
		catID = &cid
	}
	inp := service.UpsertSubscriptionInput{
		ProfileID:        profileID,
		AlertType:        req.Msg.AlertType,
		Channel:          req.Msg.Channel,
		ThresholdPct:     float64(req.Msg.ThresholdPct),
		ThresholdScope:   req.Msg.ThresholdScope,
		CategoryID:       catID,
		NotifyAllMembers: req.Msg.NotifyAllMembers,
	}
	sub, svcErr := h.svc.UpsertSubscription(ctx, userID, inp)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpsertAlertSubscriptionResponse{
		Subscription: toProtoAlertSubscription(sub),
	}), nil
}

func (h *NotificationHandler) DeleteAlertSubscription(ctx context.Context, req *connect.Request[v1.DeleteAlertSubscriptionRequest]) (*connect.Response[v1.DeleteAlertSubscriptionResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	subID, parseErr := uuid.Parse(req.Msg.Id)
	if parseErr != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, fmt.Errorf("invalid id"))
	}
	if svcErr := h.svc.DeleteSubscription(ctx, userID, subID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteAlertSubscriptionResponse{}), nil
}

// ─── Converters ───────────────────────────────────────────────────────────────

func toProtoNotification(n db.Notification) *v1.Notification {
	p := &v1.Notification{
		Id:        n.ID.String(),
		UserId:    n.UserID.String(),
		AlertType: n.AlertType,
		Title:     n.Title,
		Body:      n.Body,
		IsRead:    n.IsRead,
		CreatedAt: timestamppb.New(n.CreatedAt.Time),
	}
	if n.BudgetProfileID != nil {
		p.BudgetProfileId = n.BudgetProfileID.String()
	}
	return p
}

func (h *NotificationHandler) currentUserID(ctx context.Context) (uuid.UUID, error) {
	raw, ok := middleware.UserIDFromContext(ctx)
	if !ok {
		return uuid.UUID{}, connect.NewError(connect.CodeUnauthenticated, nil)
	}
	id, err := uuid.Parse(raw)
	if err != nil {
		return uuid.UUID{}, connect.NewError(connect.CodeUnauthenticated, nil)
	}
	return id, nil
}

func toProtoAlertSubscription(s db.AlertSubscription) *v1.AlertSubscription {
	p := &v1.AlertSubscription{
		Id:               s.ID.String(),
		UserId:           s.UserID.String(),
		BudgetProfileId:  s.BudgetProfileID.String(),
		AlertType:        s.AlertType,
		Channel:          s.Channel,
		NotifyAllMembers: s.NotifyAllMembers,
		CreatedAt:        timestamppb.New(s.CreatedAt.Time),
	}
	if f, err := s.ThresholdPct.Float64Value(); err == nil && f.Valid {
		p.ThresholdPct = float32(f.Float64)
	}
	if s.ThresholdScope != nil {
		p.ThresholdScope = *s.ThresholdScope
	}
	if s.CategoryID != nil {
		p.CategoryId = *s.CategoryID
	}
	return p
}
