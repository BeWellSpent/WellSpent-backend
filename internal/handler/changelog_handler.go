package handler

import (
	"context"

	"connectrpc.com/connect"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/middleware"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"google.golang.org/protobuf/types/known/timestamppb"
)

type ChangelogHandler struct {
	svc *service.ChangelogService
}

func NewChangelogHandler(svc *service.ChangelogService) *ChangelogHandler {
	if svc == nil {
		panic("NewChangelogHandler: svc is required")
	}
	return &ChangelogHandler{svc: svc}
}

// The reader-facing list was retired from this service: it is now
// GET /rest/v1/changelog, served by internal/rest.

func (h *ChangelogHandler) CreateChangelogRelease(
	ctx context.Context,
	req *connect.Request[v1.CreateChangelogReleaseRequest],
) (*connect.Response[v1.CreateChangelogReleaseResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}

	items := make([]service.CreateItemParams, 0, len(req.Msg.Items))
	for _, it := range req.Msg.Items {
		items = append(items, service.CreateItemParams{
			ChangeType: changeTypeFromProto(it.ChangeType),
			SummaryEn:  it.SummaryEn,
			SummaryEs:  it.SummaryEs,
		})
	}

	params := service.CreateReleaseParams{
		Component: componentFromProto(req.Msg.Component),
		Version:   req.Msg.Version,
		Items:     items,
	}
	if req.Msg.ReleasedAt != nil {
		ts := pgtype.Timestamptz{Time: req.Msg.ReleasedAt.AsTime(), Valid: true}
		params.ReleasedAt = &ts
	}

	release, err := h.svc.CreateRelease(ctx, userID, params)
	if err != nil {
		return nil, toConnectError(err)
	}
	return connect.NewResponse(&v1.CreateChangelogReleaseResponse{
		Release: toProtoChangelogRelease(release),
	}), nil
}

func (h *ChangelogHandler) currentUserID(ctx context.Context) (uuid.UUID, error) {
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

// componentFromProto maps the enum to its stored form. UNSPECIFIED maps to the
// empty string, which the service rejects on write and the handler drops on
// read.
func componentFromProto(c v1.ChangelogComponent) string {
	switch c {
	case v1.ChangelogComponent_CHANGELOG_COMPONENT_WEB:
		return service.ComponentWeb
	case v1.ChangelogComponent_CHANGELOG_COMPONENT_IOS:
		return service.ComponentIOS
	case v1.ChangelogComponent_CHANGELOG_COMPONENT_SERVER:
		return service.ComponentServer
	default:
		return ""
	}
}

func componentToProto(c string) v1.ChangelogComponent {
	switch c {
	case service.ComponentWeb:
		return v1.ChangelogComponent_CHANGELOG_COMPONENT_WEB
	case service.ComponentIOS:
		return v1.ChangelogComponent_CHANGELOG_COMPONENT_IOS
	case service.ComponentServer:
		return v1.ChangelogComponent_CHANGELOG_COMPONENT_SERVER
	default:
		return v1.ChangelogComponent_CHANGELOG_COMPONENT_UNSPECIFIED
	}
}

func changeTypeFromProto(t v1.ChangeType) string {
	switch t {
	case v1.ChangeType_CHANGE_TYPE_ADDED:
		return service.ChangeTypeAdded
	case v1.ChangeType_CHANGE_TYPE_FIXED:
		return service.ChangeTypeFixed
	case v1.ChangeType_CHANGE_TYPE_CHANGED:
		return service.ChangeTypeChanged
	default:
		return ""
	}
}

func changeTypeToProto(t string) v1.ChangeType {
	switch t {
	case service.ChangeTypeAdded:
		return v1.ChangeType_CHANGE_TYPE_ADDED
	case service.ChangeTypeFixed:
		return v1.ChangeType_CHANGE_TYPE_FIXED
	case service.ChangeTypeChanged:
		return v1.ChangeType_CHANGE_TYPE_CHANGED
	default:
		return v1.ChangeType_CHANGE_TYPE_UNSPECIFIED
	}
}

func toProtoChangelogRelease(r service.Release) *v1.ChangelogRelease {
	p := &v1.ChangelogRelease{
		Id:        r.ID.String(),
		Component: componentToProto(r.Component),
		Version:   r.Version,
		Items:     make([]*v1.ChangelogItem, 0, len(r.Items)),
	}
	if r.ReleasedAt.Valid {
		p.ReleasedAt = timestamppb.New(r.ReleasedAt.Time)
	}
	if r.CreatedAt.Valid {
		p.CreatedAt = timestamppb.New(r.CreatedAt.Time)
	}
	for _, it := range r.Items {
		p.Items = append(p.Items, toProtoChangelogItem(it))
	}
	return p
}

func toProtoChangelogItem(i db.ChangelogItem) *v1.ChangelogItem {
	return &v1.ChangelogItem{
		ChangeType: changeTypeToProto(i.ChangeType),
		SummaryEn:  i.SummaryEn,
		SummaryEs:  i.SummaryEs,
	}
}
