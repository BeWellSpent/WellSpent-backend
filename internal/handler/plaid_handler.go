package handler

import (
	"context"
	"log"

	"connectrpc.com/connect"
	"github.com/google/uuid"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/middleware"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"google.golang.org/protobuf/types/known/timestamppb"
)

type PlaidHandler struct {
	svc *service.PlaidService
}

func NewPlaidHandler(svc *service.PlaidService) *PlaidHandler {
	if svc == nil {
		panic("NewPlaidHandler: svc is required")
	}
	return &PlaidHandler{svc: svc}
}

func (h *PlaidHandler) currentUserID(ctx context.Context) (uuid.UUID, error) {
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

func (h *PlaidHandler) CreateLinkToken(ctx context.Context, req *connect.Request[v1.CreateLinkTokenRequest]) (*connect.Response[v1.CreateLinkTokenResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	var connectionID *uuid.UUID
	if req.Msg.ConnectionId != "" {
		id, parseErr := uuid.Parse(req.Msg.ConnectionId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
		}
		connectionID = &id
	}
	result, svcErr := h.svc.CreateLinkToken(ctx, userID, profileID, connectionID, req.Msg.RedirectUri)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CreateLinkTokenResponse{
		LinkToken:  result.LinkToken,
		Expiration: result.Expiration,
	}), nil
}

func (h *PlaidHandler) ExchangePublicToken(ctx context.Context, req *connect.Request[v1.ExchangePublicTokenRequest]) (*connect.Response[v1.ExchangePublicTokenResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	item, svcErr := h.svc.ExchangePublicToken(ctx, userID, profileID, req.Msg.PublicToken)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.ExchangePublicTokenResponse{
		Connection: toProtoPlaidConnection(item),
	}), nil
}

func (h *PlaidHandler) GetPlaidConnections(ctx context.Context, req *connect.Request[v1.GetPlaidConnectionsRequest]) (*connect.Response[v1.GetPlaidConnectionsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	var profileID *uuid.UUID
	if req.Msg.BudgetProfileId != "" {
		id, parseErr := uuid.Parse(req.Msg.BudgetProfileId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
		}
		profileID = &id
	}
	views, svcErr := h.svc.GetConnections(ctx, userID, profileID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	conns := make([]*v1.PlaidConnection, len(views))
	for i, view := range views {
		conns[i] = toProtoPlaidConnectionView(view)
	}

	// Best-effort: the connection list is the useful payload, and failing the
	// whole call because a supplementary warning couldn't be computed would
	// trade a working screen for a broken one.
	warnings, warnErr := h.svc.ListSyncWarnings(ctx, userID)
	if warnErr != nil {
		log.Printf("plaid: list sync warnings for user %s: %v", userID, warnErr)
	}
	protoWarnings := make([]*v1.BudgetSyncWarning, len(warnings))
	for i, w := range warnings {
		protoWarnings[i] = &v1.BudgetSyncWarning{
			BudgetProfileId: w.ProfileID.String(),
			BudgetName:      w.BudgetName,
			MemberName:      w.MemberName,
			ConnectionCount: w.ConnectionCount,
			IsCurrentUser:   w.IsCurrentUser,
		}
	}

	return connect.NewResponse(&v1.GetPlaidConnectionsResponse{
		Connections: conns,
		Warnings:    protoWarnings,
	}), nil
}

func (h *PlaidHandler) DisconnectPlaid(ctx context.Context, req *connect.Request[v1.DisconnectPlaidRequest]) (*connect.Response[v1.DisconnectPlaidResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	connID, err := uuid.Parse(req.Msg.ConnectionId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.svc.Disconnect(ctx, userID, connID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DisconnectPlaidResponse{}), nil
}

func (h *PlaidHandler) RefreshPlaidAccounts(ctx context.Context, req *connect.Request[v1.RefreshPlaidAccountsRequest]) (*connect.Response[v1.RefreshPlaidAccountsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	connID, err := uuid.Parse(req.Msg.ConnectionId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	item, svcErr := h.svc.RefreshAccounts(ctx, userID, connID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.RefreshPlaidAccountsResponse{
		Connection: toProtoPlaidConnection(item),
	}), nil
}

func (h *PlaidHandler) ResyncPlaidConnection(ctx context.Context, req *connect.Request[v1.ResyncPlaidConnectionRequest]) (*connect.Response[v1.ResyncPlaidConnectionResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	connID, err := uuid.Parse(req.Msg.ConnectionId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	view, svcErr := h.svc.ResyncConnection(ctx, userID, connID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.ResyncPlaidConnectionResponse{
		Connection: toProtoPlaidConnectionView(view),
	}), nil
}

// toProtoPlaidConnectionView adds the per-caller fields that only make sense
// once a budget's connections from several members appear in one list.
func toProtoPlaidConnectionView(view service.ConnectionView) *v1.PlaidConnection {
	conn := toProtoPlaidConnection(view.Item)
	conn.OwnerName = view.OwnerName
	conn.IsOwner = view.IsOwner
	conn.SyncEnabled = view.SyncEnabled
	if view.ResyncAvailableAt != nil {
		conn.ResyncAvailableAt = timestamppb.New(*view.ResyncAvailableAt)
	}
	return conn
}

// toProtoPlaidConnection maps the stored row only. The owner/entitlement
// fields are left unset, since they depend on who is asking — the connect and
// refresh responses that use this directly are always about the caller's own
// connection, and both clients refetch the list rather than rendering it.
func toProtoPlaidConnection(item db.PlaidItem) *v1.PlaidConnection {
	conn := &v1.PlaidConnection{
		Id:              item.ID.String(),
		Status:          item.Status,
		BudgetProfileId: item.BudgetProfileID.String(),
	}
	if item.InstitutionID != nil {
		conn.InstitutionId = *item.InstitutionID
	}
	if item.InstitutionName != nil {
		conn.InstitutionName = *item.InstitutionName
	}
	if item.LastSyncedAt.Valid {
		conn.LastSyncedAt = timestamppb.New(item.LastSyncedAt.Time)
	}
	return conn
}
