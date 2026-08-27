package handler

import (
	"context"
	"strings"

	"connectrpc.com/connect"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/middleware"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"google.golang.org/protobuf/types/known/timestamppb"
)

type UserHandler struct {
	svc *service.UserService
}

func NewUserHandler(svc *service.UserService) *UserHandler {
	if svc == nil {
		panic("NewUserHandler: svc is required")
	}
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
	inp.Language = req.Msg.Language
	inp.Currency = req.Msg.Currency
	user, svcErr := h.svc.Update(ctx, userID, inp)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateMeResponse{User: toProtoUser(user)}), nil
}

// ListCountries was retired: it is now GET /rest/v1/countries, served by
// internal/rest. The service method it called (UserService.ListCountries) is
// unchanged and shared — only the transport-facing wrapper moved.

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

func (h *UserHandler) ChangeEmail(ctx context.Context, req *connect.Request[v1.ChangeEmailRequest]) (*connect.Response[v1.ChangeEmailResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	user, svcErr := h.svc.ChangeEmail(ctx, userID, req.Msg.NewEmail)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.ChangeEmailResponse{User: toProtoUser(user)}), nil
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
		IsVerified:          isVerificationSatisfied(u),
		CreatedAt:           timestamppb.New(u.CreatedAt.Time),
		CountryCode:         nullStr(u.CountryCode),
		StateCode:           nullStr(u.StateCode),
		FilingStatus:        filingStatusFromString(u.FilingStatus),
		TaxPaymentFrequency: v1.TaxPaymentFrequency(u.TaxPaymentFrequency),
		Language:            u.Language,
		Currency:            u.Currency,
		Plan:                accountPlanFromString(u.Plan),

		HasApplePrivateEmail: isApplePrivateEmail(u.Email),
	}
}

// accountTypeTest marks an account exempt from the email-verification gate,
// for QA and automated tests that have no real inbox to receive a link at.
// Set by hand only — see migration 000045.
const accountTypeTest = "test"

// isVerificationSatisfied answers "may this account use the app", which is
// what User.is_verified means to a client — both clients gate on it and
// nothing else reads it.
//
// A test account therefore reports verified on the wire while its stored
// is_verified stays false. That split is deliberate: the database keeps the
// truth about whether an address was ever proven, and the exemption stays
// visible as its own column rather than being laundered into the
// verification record.
func isVerificationSatisfied(u db.User) bool {
	return u.IsVerified || u.AccountType == accountTypeTest
}

// applePrivateRelayDomain is the domain Apple issues "Hide My Email" aliases
// under. It's Apple-controlled, so no real user address can live there.
const applePrivateRelayDomain = "@privaterelay.appleid.com"

// isApplePrivateEmail reports whether an address is an Apple private-relay
// alias. Such an address is machine-generated and meaningless to the user, so
// clients show "Signed with Apple" rather than displaying it.
//
// Derived from the address rather than persisted from Apple's own
// `is_private_email` claim: that claim is only present at sign-in, and
// deriving here keeps the rule in one place for every client instead of each
// re-implementing the suffix check.
func isApplePrivateEmail(email string) bool {
	return strings.HasSuffix(strings.ToLower(strings.TrimSpace(email)), applePrivateRelayDomain)
}

func accountPlanFromString(plan string) v1.AccountPlan {
	switch plan {
	case "pro":
		return v1.AccountPlan_ACCOUNT_PLAN_PRO
	case "lifetime":
		return v1.AccountPlan_ACCOUNT_PLAN_LIFETIME
	default:
		return v1.AccountPlan_ACCOUNT_PLAN_FREE
	}
}
