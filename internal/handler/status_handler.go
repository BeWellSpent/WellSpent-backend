package handler

import (
	"context"

	"connectrpc.com/connect"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/middleware"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"google.golang.org/protobuf/types/known/timestamppb"
)

type StatusHandler struct {
	svc *service.StatusBannerService
}

func NewStatusHandler(svc *service.StatusBannerService) *StatusHandler {
	if svc == nil {
		panic("NewStatusHandler: svc is required")
	}
	return &StatusHandler{svc: svc}
}

// Reading the active banner was retired from this service: it is now
// GET /rest/v1/status/banner, served by internal/rest. Everything left here
// is superuser-only, so this handler no longer has a public method.

func (h *StatusHandler) CreateStatusBanner(
	ctx context.Context,
	req *connect.Request[v1.CreateStatusBannerRequest],
) (*connect.Response[v1.CreateStatusBannerResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}

	params := service.CreateStatusBannerParams{
		Severity:  severityFromProto(req.Msg.Severity),
		MessageEn: req.Msg.MessageEn,
		MessageEs: req.Msg.MessageEs,
	}
	if ts := req.Msg.StartsAt; ts != nil {
		t := ts.AsTime()
		params.StartsAt = &t
	}
	if ts := req.Msg.EndsAt; ts != nil {
		params.EndsAt = ts.AsTime()
	}

	banner, svcErr := h.svc.Create(ctx, userID, params)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CreateStatusBannerResponse{
		Banner: toProtoStatusBanner(banner),
	}), nil
}

func (h *StatusHandler) ListStatusBanners(
	ctx context.Context,
	req *connect.Request[v1.ListStatusBannersRequest],
) (*connect.Response[v1.ListStatusBannersResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}

	banners, svcErr := h.svc.List(ctx, userID, req.Msg.Limit)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.StatusBanner, len(banners))
	for i, b := range banners {
		protos[i] = toProtoStatusBanner(b)
	}
	return connect.NewResponse(&v1.ListStatusBannersResponse{Banners: protos}), nil
}

func (h *StatusHandler) ExpireStatusBanner(
	ctx context.Context,
	req *connect.Request[v1.ExpireStatusBannerRequest],
) (*connect.Response[v1.ExpireStatusBannerResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	bannerID, parseErr := uuid.Parse(req.Msg.Id)
	if parseErr != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
	}

	banner, svcErr := h.svc.Expire(ctx, userID, bannerID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.ExpireStatusBannerResponse{
		Banner: toProtoStatusBanner(banner),
	}), nil
}

func (h *StatusHandler) currentUserID(ctx context.Context) (uuid.UUID, error) {
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

// severityFromProto maps the enum to its stored form. UNSPECIFIED maps to the
// empty string rather than a default severity, so the service rejects it — an
// operator who forgot to set one should be told, not silently given green.
func severityFromProto(s v1.StatusBannerSeverity) string {
	switch s {
	case v1.StatusBannerSeverity_STATUS_BANNER_SEVERITY_INFO:
		return "info"
	case v1.StatusBannerSeverity_STATUS_BANNER_SEVERITY_WARNING:
		return "warning"
	case v1.StatusBannerSeverity_STATUS_BANNER_SEVERITY_CRITICAL:
		return "critical"
	default:
		return ""
	}
}

func severityToProto(s string) v1.StatusBannerSeverity {
	switch s {
	case "info":
		return v1.StatusBannerSeverity_STATUS_BANNER_SEVERITY_INFO
	case "warning":
		return v1.StatusBannerSeverity_STATUS_BANNER_SEVERITY_WARNING
	case "critical":
		return v1.StatusBannerSeverity_STATUS_BANNER_SEVERITY_CRITICAL
	default:
		return v1.StatusBannerSeverity_STATUS_BANNER_SEVERITY_UNSPECIFIED
	}
}

func toProtoStatusBanner(b db.StatusBanner) *v1.StatusBanner {
	p := &v1.StatusBanner{
		Id:        b.ID.String(),
		Severity:  severityToProto(b.Severity),
		MessageEn: b.MessageEn,
		MessageEs: b.MessageEs,
	}
	if b.StartsAt.Valid {
		p.StartsAt = timestamppb.New(b.StartsAt.Time)
	}
	if b.EndsAt.Valid {
		p.EndsAt = timestamppb.New(b.EndsAt.Time)
	}
	if b.CreatedAt.Valid {
		p.CreatedAt = timestamppb.New(b.CreatedAt.Time)
	}
	return p
}
