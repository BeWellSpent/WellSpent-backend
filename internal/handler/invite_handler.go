package handler

import (
	"context"
	"strings"

	"connectrpc.com/connect"
	"github.com/google/uuid"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/middleware"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"google.golang.org/protobuf/types/known/timestamppb"
)

type InviteHandler struct {
	invites *service.InviteService
}

func NewInviteHandler(invites *service.InviteService) *InviteHandler {
	if invites == nil {
		panic("NewInviteHandler: invites is required")
	}
	return &InviteHandler{invites: invites}
}

func (h *InviteHandler) currentUserID(ctx context.Context) (uuid.UUID, error) {
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

func (h *InviteHandler) SendBudgetInvite(ctx context.Context, req *connect.Request[v1.SendBudgetInviteRequest]) (*connect.Response[v1.SendBudgetInviteResponse], error) {
	callerID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	role := budgetRoleToString(req.Msg.Role)
	if role == "unspecified" || role == "admin" {
		return nil, connect.NewError(connect.CodeInvalidArgument, nil)
	}
	inv, svcErr := h.invites.Send(ctx, profileID, callerID, req.Msg.Email, role, req.Msg.BudgetPersonId)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.SendBudgetInviteResponse{Invite: toProtoInvite(inv, "", "")}), nil
}

func (h *InviteHandler) ListBudgetInvites(ctx context.Context, req *connect.Request[v1.ListBudgetInvitesRequest]) (*connect.Response[v1.ListBudgetInvitesResponse], error) {
	callerID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	invites, svcErr := h.invites.List(ctx, profileID, callerID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	var out []*v1.BudgetInvite
	for _, inv := range invites {
		out = append(out, toProtoInvite(inv, "", ""))
	}
	return connect.NewResponse(&v1.ListBudgetInvitesResponse{Invites: out}), nil
}

func (h *InviteHandler) CancelBudgetInvite(ctx context.Context, req *connect.Request[v1.CancelBudgetInviteRequest]) (*connect.Response[v1.CancelBudgetInviteResponse], error) {
	callerID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	inviteID, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	_, svcErr := h.invites.Cancel(ctx, inviteID, callerID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CancelBudgetInviteResponse{}), nil
}

func (h *InviteHandler) GetBudgetInvite(ctx context.Context, req *connect.Request[v1.GetBudgetInviteRequest]) (*connect.Response[v1.GetBudgetInviteResponse], error) {
	token, err := uuid.Parse(req.Msg.Token)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	row, svcErr := h.invites.GetByToken(ctx, token)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	inv := toProtoInviteFromRow(row)
	return connect.NewResponse(&v1.GetBudgetInviteResponse{Invite: inv}), nil
}

func (h *InviteHandler) AcceptBudgetInvite(ctx context.Context, req *connect.Request[v1.AcceptBudgetInviteRequest]) (*connect.Response[v1.AcceptBudgetInviteResponse], error) {
	callerID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	token, err := uuid.Parse(req.Msg.Token)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	profileID, svcErr := h.invites.Accept(ctx, token, callerID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.AcceptBudgetInviteResponse{BudgetProfileId: profileID.String()}), nil
}

// ── Helpers ───────────────────────────────────────────────────────────────────

func budgetRoleToString(r v1.BudgetRole) string {
	switch r {
	case v1.BudgetRole_BUDGET_ROLE_ADMIN:
		return "admin"
	case v1.BudgetRole_BUDGET_ROLE_COLLABORATOR:
		return "collaborator"
	case v1.BudgetRole_BUDGET_ROLE_VIEWER:
		return "viewer"
	default:
		return "unspecified"
	}
}

func stringToBudgetRole(s string) v1.BudgetRole {
	switch strings.ToLower(s) {
	case "admin":
		return v1.BudgetRole_BUDGET_ROLE_ADMIN
	case "collaborator":
		return v1.BudgetRole_BUDGET_ROLE_COLLABORATOR
	case "viewer":
		return v1.BudgetRole_BUDGET_ROLE_VIEWER
	default:
		return v1.BudgetRole_BUDGET_ROLE_UNSPECIFIED
	}
}

func inviteStatusToProto(s string) v1.InviteStatus {
	switch s {
	case "pending":
		return v1.InviteStatus_INVITE_STATUS_PENDING
	case "accepted":
		return v1.InviteStatus_INVITE_STATUS_ACCEPTED
	case "cancelled":
		return v1.InviteStatus_INVITE_STATUS_CANCELLED
	case "expired":
		return v1.InviteStatus_INVITE_STATUS_EXPIRED
	default:
		return v1.InviteStatus_INVITE_STATUS_UNSPECIFIED
	}
}

func toProtoInvite(inv db.BudgetInvite, budgetName, inviterName string) *v1.BudgetInvite {
	var personID int64
	if inv.BudgetPersonID != nil {
		personID = *inv.BudgetPersonID
	}
	return &v1.BudgetInvite{
		Id:              inv.ID.String(),
		BudgetProfileId: inv.BudgetProfileID.String(),
		BudgetName:      budgetName,
		InviterName:     inviterName,
		Email:           inv.Email,
		Role:            stringToBudgetRole(inv.Role),
		Status:          inviteStatusToProto(inv.Status),
		ExpiresAt:       timestamppb.New(inv.ExpiresAt.Time),
		CreatedAt:       timestamppb.New(inv.CreatedAt.Time),
		BudgetPersonId:  personID,
	}
}

func toProtoInviteFromRow(row db.GetInviteByTokenRow) *v1.BudgetInvite {
	var personID int64
	if row.BudgetPersonID != nil {
		personID = *row.BudgetPersonID
	}
	return &v1.BudgetInvite{
		Id:              row.ID.String(),
		BudgetProfileId: row.BudgetProfileID.String(),
		BudgetName:      row.BudgetName,
		InviterName:     row.InviterName,
		Email:           row.Email,
		Role:            stringToBudgetRole(row.Role),
		Status:          inviteStatusToProto(row.Status),
		ExpiresAt:       timestamppb.New(row.ExpiresAt.Time),
		CreatedAt:       timestamppb.New(row.CreatedAt.Time),
		BudgetPersonId:  personID,
	}
}
