package handler

import (
	"context"

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
	result, svcErr := h.svc.CreateLinkToken(ctx, userID, profileID, connectionID)
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
	items, svcErr := h.svc.GetConnections(ctx, userID, profileID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	conns := make([]*v1.PlaidConnection, len(items))
	for i, item := range items {
		conns[i] = toProtoPlaidConnection(item)
	}
	return connect.NewResponse(&v1.GetPlaidConnectionsResponse{Connections: conns}), nil
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
