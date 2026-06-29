package handler

import (
	"context"

	"connectrpc.com/connect"
	"github.com/google/uuid"
	v1 "github.com/mauro-afa91/spendsense/gen/spendsense/v1"
	"github.com/mauro-afa91/spendsense/internal/middleware"
	"github.com/mauro-afa91/spendsense/internal/service"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"google.golang.org/protobuf/types/known/timestamppb"
)


type UserHandler struct {
	svc *service.UserService
}

func NewUserHandler(svc *service.UserService) *UserHandler {
	return &UserHandler{svc: svc}
}

func (h *UserHandler) GetMe(ctx context.Context, _ *connect.Request[v1.GetMeRequest]) (*connect.Response[v1.GetMeResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	user, svcErr := h.svc.GetByID(ctx, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.GetMeResponse{User: toProtoUser(user)}), nil
}

func (h *UserHandler) UpdateMe(ctx context.Context, req *connect.Request[v1.UpdateMeRequest]) (*connect.Response[v1.UpdateMeResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	inp := service.UpdateUserInput{
		FilingStatus:        filingStatusToString(req.Msg.FilingStatus),
		TaxPaymentFrequency: taxPaymentFrequencyFromProto(req.Msg.TaxPaymentFrequency),
	}
	if req.Msg.FirstName != "" {
		inp.FirstName = &req.Msg.FirstName
	}
	if req.Msg.LastName != "" {
		inp.LastName = &req.Msg.LastName
	}
	if req.Msg.CountryCode != "" {
		inp.CountryCode = &req.Msg.CountryCode
	}
	if req.Msg.StateCode != "" {
		inp.StateCode = &req.Msg.StateCode
	}
	user, svcErr := h.svc.Update(ctx, userID, inp)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateMeResponse{User: toProtoUser(user)}), nil
}

func (h *UserHandler) ListCountries(ctx context.Context, _ *connect.Request[v1.ListCountriesRequest]) (*connect.Response[v1.ListCountriesResponse], error) {
	countries, featuresByCode, svcErr := h.svc.ListCountries(ctx)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.Country, len(countries))
	for i, c := range countries {
		pc := &v1.Country{Code: c.Code, Name: c.Name, IsEnabled: c.IsEnabled}
		for _, f := range featuresByCode[c.Code] {
			pc.Features = append(pc.Features, &v1.CountryFeature{Name: f.FeatureName, IsEnabled: f.IsEnabled})
		}
		protos[i] = pc
	}
	return connect.NewResponse(&v1.ListCountriesResponse{Countries: protos}), nil
}

func (h *UserHandler) ChangePassword(ctx context.Context, req *connect.Request[v1.ChangePasswordRequest]) (*connect.Response[v1.ChangePasswordResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	if svcErr := h.svc.ChangePassword(ctx, userID, req.Msg.CurrentPassword, req.Msg.NewPassword); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.ChangePasswordResponse{}), nil
}

func (h *UserHandler) DeleteMe(ctx context.Context, _ *connect.Request[v1.DeleteMeRequest]) (*connect.Response[v1.DeleteMeResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	if svcErr := h.svc.Delete(ctx, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteMeResponse{}), nil
}

func (h *UserHandler) currentUserID(ctx context.Context) (uuid.UUID, error) {
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

func toProtoUser(u db.User) *v1.User {
	return &v1.User{
		Id:                  u.ID.String(),
		Email:               u.Email,
		FirstName:           nullStr(u.FirstName),
		LastName:            nullStr(u.LastName),
		IsActive:            u.IsActive,
		IsVerified:          u.IsVerified,
		CreatedAt:           timestamppb.New(u.CreatedAt.Time),
		CountryCode:         nullStr(u.CountryCode),
		StateCode:           nullStr(u.StateCode),
		FilingStatus:        filingStatusFromString(u.FilingStatus),
		TaxPaymentFrequency: v1.TaxPaymentFrequency(u.TaxPaymentFrequency),
	}
}
