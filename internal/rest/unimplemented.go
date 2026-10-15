package rest

import (
	"net/http"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
	openapi_types "github.com/oapi-codegen/runtime/types"
)

// Every one of these methods exists only because they're part of the shared
// ServerInterface the C#/.NET rewrite's contract additions generate — Go
// needs *something* satisfying the full interface to compile at all, since
// oapi-codegen's std-http-server mode has no partial-implementation fallback.
//
// None of them are meant to ever actually implement business logic here: Go
// is being decommissioned in favor of the C# backend (docs/features/
// refactor-backend-csharp.md), so every domain beyond the original four
// narrow global-cacheable endpoints (countries, status banner, changelog,
// ping) is served by the C# backend exclusively. A request that somehow
// reaches one of these on the Go backend gets a clear 501 rather than a
// compile failure or a silent 404.
func notImplemented(w http.ResponseWriter, r *http.Request) {
	writeErrorCode(w, http.StatusNotImplemented, codeNotImplemented, "not implemented on this backend; served by the C# backend")
}

func (s *server) SignInWithApple(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) ExchangeGoogleCode(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) GetGoogleAuthUrl(w http.ResponseWriter, r *http.Request, params restgen.GetGoogleAuthUrlParams) {
	notImplemented(w, r)
}

func (s *server) Login(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) Logout(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) RefreshToken(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) RegisterUser(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) ResendVerificationEmail(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) VerifyEmail(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) GetBudgetPeriod(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) GetExpenseSummary(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, params restgen.GetExpenseSummaryParams) {
	notImplemented(w, r)
}

func (s *server) ListIncomeEntries(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) UpdateIncomeEntry(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, entryId int64) {
	notImplemented(w, r)
}

func (s *server) ListBudgetProfiles(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) CreateBudgetProfile(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) DeleteBudgetProfile(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) GetBudgetProfile(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) UpdateBudgetProfile(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) SetBudgetAutoUpdatePlannedAmount(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) SetBudgetCarryoverEnabled(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ListExpenseAllocations(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) UpsertExpenseAllocation(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) DeleteExpenseAllocation(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, allocationId int32) {
	notImplemented(w, r)
}

func (s *server) ListFixedExpenses(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) CreateFixedExpense(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) DeleteFixedExpense(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, feId openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) UpdateFixedExpense(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, feId openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ListIncomeSources(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) AddIncomeSource(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) DeleteIncomeSource(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, sourceId int64) {
	notImplemented(w, r)
}

func (s *server) UpdateIncomeSource(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, sourceId int64) {
	notImplemented(w, r)
}

func (s *server) UpdateMyFocusedViewPreference(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) UpdateMyManualMatchReviewPreference(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) UpdateMyBudgetPreferences(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ListPaymentMethods(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ListBudgetPeople(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) AddBudgetPeople(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) RemoveBudgetPerson(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, personId int64, params restgen.RemoveBudgetPersonParams) {
	notImplemented(w, r)
}

func (s *server) UpdateBudgetPerson(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, personId int64) {
	notImplemented(w, r)
}

func (s *server) UpdateBudgetPersonRole(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, personId int64) {
	notImplemented(w, r)
}

func (s *server) ListBudgetPeriods(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) CreateBudgetPeriod(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ListSavingsSources(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) AddSavingsSource(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) DeleteSavingsSource(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, sourceId int64) {
	notImplemented(w, r)
}

func (s *server) UpdateSavingsSource(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, sourceId int64) {
	notImplemented(w, r)
}

func (s *server) ListTransactionReviews(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ListCategories(w http.ResponseWriter, r *http.Request, params restgen.ListCategoriesParams) {
	notImplemented(w, r)
}

func (s *server) CreateCategory(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) DeleteCategory(w http.ResponseWriter, r *http.Request, id int32, params restgen.DeleteCategoryParams) {
	notImplemented(w, r)
}

func (s *server) UpdateCategory(w http.ResponseWriter, r *http.Request, id int32) {
	notImplemented(w, r)
}

func (s *server) CreateChangelogRelease(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) ListNotifications(w http.ResponseWriter, r *http.Request, params restgen.ListNotificationsParams) {
	notImplemented(w, r)
}

func (s *server) ListAlertSubscriptions(w http.ResponseWriter, r *http.Request, params restgen.ListAlertSubscriptionsParams) {
	notImplemented(w, r)
}

func (s *server) UpsertAlertSubscription(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) DeleteAlertSubscription(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) RegisterDeviceToken(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) MarkNotificationsRead(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) GetUnreadCount(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) CreatePaymentMethod(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) DeletePaymentMethod(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, params restgen.DeletePaymentMethodParams) {
	notImplemented(w, r)
}

func (s *server) UpdatePaymentMethod(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) GetPlaidConnections(w http.ResponseWriter, r *http.Request, params restgen.GetPlaidConnectionsParams) {
	notImplemented(w, r)
}

func (s *server) ExchangePublicToken(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) DisconnectPlaid(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) RefreshPlaidAccounts(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ResyncPlaidConnection(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) CreateLinkToken(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) ListStatusBanners(w http.ResponseWriter, r *http.Request, params restgen.ListStatusBannersParams) {
	notImplemented(w, r)
}

func (s *server) CreateStatusBanner(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) ExpireStatusBanner(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ConfirmTransactionReview(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) DismissTransactionReview(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) ListTransactions(w http.ResponseWriter, r *http.Request, params restgen.ListTransactionsParams) {
	notImplemented(w, r)
}

func (s *server) CreateTransaction(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) DeleteTransaction(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) UpdateTransaction(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) SetTransactionExcluded(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) CreateFixedExpenseFromTransaction(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, params restgen.CreateFixedExpenseFromTransactionParams) {
	notImplemented(w, r)
}

func (s *server) MarkTransactionForReview(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) DeleteInstallmentPlan(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, params restgen.DeleteInstallmentPlanParams) {
	notImplemented(w, r)
}

func (s *server) CreateInstallmentPlan(w http.ResponseWriter, r *http.Request, id openapi_types.UUID, params restgen.CreateInstallmentPlanParams) {
	notImplemented(w, r)
}

func (s *server) MarkTransactionAsPaid(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) UnmarkTransactionAsPaid(w http.ResponseWriter, r *http.Request, id openapi_types.UUID) {
	notImplemented(w, r)
}

func (s *server) DeleteMe(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) GetMe(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) UpdateMe(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) ChangeEmail(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}

func (s *server) ChangePassword(w http.ResponseWriter, r *http.Request) {
	notImplemented(w, r)
}
