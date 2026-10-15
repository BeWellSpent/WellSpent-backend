package rest

import (
	"encoding/json"
	"net/http"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
	"github.com/google/uuid"
)

// These five endpoints were added to the contract for the C#/.NET rewrite
// (see wellspent.yaml's "invites (C#/.NET rewrite)" section) — unlike the
// package doc's narrow global-and-cacheable rule, which predates that
// rewrite. Same shape as every other handler here regardless: no business
// logic, delegate to the same InviteService the retired/retiring Connect
// handler called.

// SendBudgetInvite invites a collaborator or viewer to a budget. Admin only.
func (s *server) SendBudgetInvite(w http.ResponseWriter, r *http.Request) {
	userID, ok := s.requireAuth(w, r)
	if !ok {
		return
	}
	callerID, err := uuid.Parse(userID)
	if err != nil {
		writeErrorCode(w, http.StatusUnauthorized, codeUnauthenticated, "a valid bearer token is required")
		return
	}

	var req restgen.SendBudgetInviteRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		writeErrorCode(w, http.StatusBadRequest, codeInvalidArgument, "invalid request body")
		return
	}

	var personID int64
	if req.BudgetPersonId != nil {
		personID = *req.BudgetPersonId
	}

	inv, err := s.deps.Invites.Send(r.Context(), req.BudgetProfileId, callerID, string(req.Email), string(req.Role), personID)
	if err != nil {
		writeError(w, r, s.deps.Logger, err)
		return
	}
	writeJSON(w, r, s.deps.Logger, cacheNone, "", restgen.BudgetInviteResponse{Invite: toRESTInvite(inv, "", "")})
}

// ListBudgetInvites lists pending and cancelled invites for a budget. Admin only.
func (s *server) ListBudgetInvites(w http.ResponseWriter, r *http.Request, params restgen.ListBudgetInvitesParams) {
	userID, ok := s.requireAuth(w, r)
	if !ok {
		return
	}
	callerID, err := uuid.Parse(userID)
	if err != nil {
		writeErrorCode(w, http.StatusUnauthorized, codeUnauthenticated, "a valid bearer token is required")
		return
	}

	invites, err := s.deps.Invites.List(r.Context(), params.BudgetProfileId, callerID)
	if err != nil {
		writeError(w, r, s.deps.Logger, err)
		return
	}
	out := make([]restgen.BudgetInvite, 0, len(invites))
	for _, inv := range invites {
		out = append(out, toRESTInvite(inv, "", ""))
	}
	writeJSON(w, r, s.deps.Logger, cacheNone, "", restgen.BudgetInviteListResponse{Invites: out})
}

// CancelBudgetInvite cancels a pending invite. Admin only.
func (s *server) CancelBudgetInvite(w http.ResponseWriter, r *http.Request, id uuid.UUID) {
	userID, ok := s.requireAuth(w, r)
	if !ok {
		return
	}
	callerID, err := uuid.Parse(userID)
	if err != nil {
		writeErrorCode(w, http.StatusUnauthorized, codeUnauthenticated, "a valid bearer token is required")
		return
	}

	if _, err := s.deps.Invites.Cancel(r.Context(), id, callerID); err != nil {
		writeError(w, r, s.deps.Logger, err)
		return
	}
	// No content schema on this response — same as the contract defines.
	w.WriteHeader(http.StatusOK)
}

// GetBudgetInvite serves invite details by token — the public preview page
// shown before the user authenticates. Public: the invitee doesn't have a
// session yet when they first open the link.
func (s *server) GetBudgetInvite(w http.ResponseWriter, r *http.Request, token uuid.UUID) {
	row, err := s.deps.Invites.GetByToken(r.Context(), token)
	if err != nil {
		writeError(w, r, s.deps.Logger, err)
		return
	}
	writeJSON(w, r, s.deps.Logger, cacheNone, "", restgen.BudgetInviteResponse{Invite: toRESTInviteFromRow(row)})
}

// AcceptBudgetInvite links the authenticated caller to the invite's budget.
// Always requires a bearer token — an invitee with no account registers first.
func (s *server) AcceptBudgetInvite(w http.ResponseWriter, r *http.Request, token uuid.UUID) {
	userID, ok := s.requireAuth(w, r)
	if !ok {
		return
	}
	callerID, err := uuid.Parse(userID)
	if err != nil {
		writeErrorCode(w, http.StatusUnauthorized, codeUnauthenticated, "a valid bearer token is required")
		return
	}

	profileID, err := s.deps.Invites.Accept(r.Context(), token, callerID)
	if err != nil {
		writeError(w, r, s.deps.Logger, err)
		return
	}
	writeJSON(w, r, s.deps.Logger, cacheNone, "", restgen.AcceptBudgetInviteResponse{BudgetProfileId: profileID})
}
