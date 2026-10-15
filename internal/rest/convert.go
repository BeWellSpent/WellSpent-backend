package rest

import (
	"time"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/jackc/pgx/v5/pgtype"
	openapi_types "github.com/oapi-codegen/runtime/types"
)

// This file is internal/handler/convert.go's opposite number: it maps database
// rows onto the generated REST types, and nothing else. All business logic
// stays in internal/service, called identically by both transports.
//
// One thing worth noticing: severity, component and change_type need no mapping
// at all. They are text columns whose values ("info", "web", "added") are
// exactly the strings the OpenAPI contract enumerates, so a cast is the whole
// conversion. The protobuf side has to translate SCREAMING_SNAKE enum constants
// in both directions for the same data.

func timestamp(ts pgtype.Timestamptz) time.Time {
	if !ts.Valid {
		return time.Time{}
	}
	return ts.Time.UTC()
}

func toRESTCountries(rows []db.ListEnabledCountriesRow, featuresByCode map[string][]db.CountryFeature) restgen.CountriesResponse {
	countries := make([]restgen.Country, 0, len(rows))
	for _, row := range rows {
		features := make([]restgen.CountryFeature, 0, len(featuresByCode[row.Code]))
		for _, f := range featuresByCode[row.Code] {
			features = append(features, restgen.CountryFeature{
				Name:      f.FeatureName,
				IsEnabled: f.IsEnabled,
			})
		}
		countries = append(countries, restgen.Country{
			Code:      row.Code,
			Name:      row.Name,
			IsEnabled: row.IsEnabled,
			Features:  features,
		})
	}
	return restgen.CountriesResponse{Countries: countries}
}

func toRESTStatusBanner(banner db.StatusBanner) restgen.StatusBanner {
	return restgen.StatusBanner{
		Id:        banner.ID,
		Severity:  restgen.StatusBannerSeverity(banner.Severity),
		MessageEn: banner.MessageEn,
		MessageEs: banner.MessageEs,
		StartsAt:  timestamp(banner.StartsAt),
		EndsAt:    timestamp(banner.EndsAt),
		CreatedAt: timestamp(banner.CreatedAt),
	}
}

func toRESTInvite(inv db.BudgetInvite, budgetName, inviterName string) restgen.BudgetInvite {
	return restgen.BudgetInvite{
		Id:              inv.ID,
		BudgetProfileId: inv.BudgetProfileID,
		BudgetName:      budgetName,
		InviterName:     inviterName,
		Email:           openapi_types.Email(inv.Email),
		Role:            restgen.BudgetInviteRole(inv.Role),
		Status:          restgen.InviteStatus(inv.Status),
		ExpiresAt:       timestamp(inv.ExpiresAt),
		CreatedAt:       timestamp(inv.CreatedAt),
		BudgetPersonId:  inv.BudgetPersonID,
	}
}

func toRESTInviteFromRow(row db.GetInviteByTokenRow) restgen.BudgetInvite {
	return restgen.BudgetInvite{
		Id:              row.ID,
		BudgetProfileId: row.BudgetProfileID,
		BudgetName:      row.BudgetName,
		InviterName:     row.InviterName,
		Email:           openapi_types.Email(row.Email),
		Role:            restgen.BudgetInviteRole(row.Role),
		Status:          restgen.InviteStatus(row.Status),
		ExpiresAt:       timestamp(row.ExpiresAt),
		CreatedAt:       timestamp(row.CreatedAt),
		BudgetPersonId:  row.BudgetPersonID,
	}
}

func toRESTChangelog(releases []service.Release, serverVersion string) restgen.ChangelogResponse {
	out := make([]restgen.ChangelogRelease, 0, len(releases))
	for _, r := range releases {
		items := make([]restgen.ChangelogItem, 0, len(r.Items))
		for _, item := range r.Items {
			items = append(items, restgen.ChangelogItem{
				ChangeType: restgen.ChangeType(item.ChangeType),
				SummaryEn:  item.SummaryEn,
				SummaryEs:  item.SummaryEs,
			})
		}
		out = append(out, restgen.ChangelogRelease{
			Id:         r.ID,
			Component:  restgen.ChangelogComponent(r.Component),
			Version:    r.Version,
			ReleasedAt: timestamp(r.ReleasedAt),
			Items:      items,
			CreatedAt:  timestamp(r.CreatedAt),
		})
	}
	return restgen.ChangelogResponse{
		Releases:             out,
		CurrentServerVersion: serverVersion,
	}
}
