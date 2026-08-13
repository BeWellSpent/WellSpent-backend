package service

import (
	"context"
	"fmt"
	"log"
	"time"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/crypto"
	plaidclient "github.com/BeWellSpent/wellspent-backend/internal/plaid"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
)

type PlaidService struct {
	plaid         plaidclient.Client
	items         repository.PlaidRepository
	budgets       repository.BudgetProfileRepository
	users         repository.UserRepository
	transactions  repository.TransactionRepository
	fixedExpenses repository.FixedExpenseRepository
	reviews       repository.TransactionReviewRepository
	encryptionKey string
	notifs        *NotificationService
}

func (s *PlaidService) WithNotifications(ns *NotificationService) *PlaidService {
	s.notifs = ns
	return s
}

func NewPlaidService(
	plaid plaidclient.Client,
	items repository.PlaidRepository,
	budgets repository.BudgetProfileRepository,
	users repository.UserRepository,
	transactions repository.TransactionRepository,
	fixedExpenses repository.FixedExpenseRepository,
	reviews repository.TransactionReviewRepository,
	encryptionKey string,
) *PlaidService {
	if plaid == nil {
		panic("NewPlaidService: plaid is required")
	}
	if items == nil {
		panic("NewPlaidService: items is required")
	}
	if budgets == nil {
		panic("NewPlaidService: budgets is required")
	}
	if users == nil {
		panic("NewPlaidService: users is required")
	}
	if transactions == nil {
		panic("NewPlaidService: transactions is required")
	}
	if fixedExpenses == nil {
		panic("NewPlaidService: fixedExpenses is required")
	}
	if reviews == nil {
		panic("NewPlaidService: reviews is required")
	}
	if encryptionKey == "" {
		panic("NewPlaidService: encryptionKey is required")
	}
	return &PlaidService{
		plaid:         plaid,
		items:         items,
		budgets:       budgets,
		users:         users,
		transactions:  transactions,
		fixedExpenses: fixedExpenses,
		reviews:       reviews,
		encryptionKey: encryptionKey,
	}
}

// requireUS returns Forbidden if the user is not a US resident.
func (s *PlaidService) requireUS(ctx context.Context, userID uuid.UUID) error {
	user, err := s.users.GetByID(ctx, userID)
	if err != nil {
		return err
	}
	cc := ""
	if user.CountryCode != nil {
		cc = *user.CountryCode
	}
	if cc != "US" {
		return apperr.Forbidden("Plaid is only available for US users")
	}
	return nil
}

// requireProOrLifetime returns Invalid if the calling user is on the free
// plan — Plaid bank sync is a Pro/Lifetime feature. Checked at link time
// (CreateLinkToken/ExchangePublicToken) so a free-tier user gets a clear
// error instead of successfully linking an item that SyncItem will then
// silently no-op on forever (see the plan check in plaid_sync.go).
func (s *PlaidService) requireProOrLifetime(ctx context.Context, userID uuid.UUID) error {
	user, err := s.users.GetByID(ctx, userID)
	if err != nil {
		return err
	}
	if user.Plan == "free" {
		return apperr.Invalid("free tier: Plaid bank sync requires a Pro subscription")
	}
	return nil
}

// requireProfileOwnerOrMember returns Forbidden if the user does not own or belong to the profile.
func (s *PlaidService) requireProfileOwnerOrMember(ctx context.Context, profileID, userID uuid.UUID) error {
	profile, err := s.budgets.GetByID(ctx, profileID)
	if err != nil {
		return err
	}
	if profile.UserID == userID {
		return nil
	}
	ok, err := s.budgets.ExistsPersonForUser(ctx, profileID, userID)
	if err != nil {
		return err
	}
	if !ok {
		return apperr.Forbidden("access denied")
	}
	return nil
}

type CreateLinkTokenResult struct {
	LinkToken  string
	Expiration string
}

// CreateLinkToken creates a Link token. If connectionID is non-nil, it
// requests update mode (account selection) for that existing connection
// instead of a fresh connect flow — the caller must own the connection.
func (s *PlaidService) CreateLinkToken(ctx context.Context, userID, profileID uuid.UUID, connectionID *uuid.UUID, redirectURI string) (CreateLinkTokenResult, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return CreateLinkTokenResult{}, err
	}
	if err := s.requireProOrLifetime(ctx, userID); err != nil {
		return CreateLinkTokenResult{}, err
	}
	if err := s.requireProfileOwnerOrMember(ctx, profileID, userID); err != nil {
		return CreateLinkTokenResult{}, err
	}

	updateAccessToken := ""
	if connectionID != nil {
		item, err := s.items.GetByID(ctx, *connectionID)
		if err != nil {
			return CreateLinkTokenResult{}, err
		}
		if item.UserID != userID {
			return CreateLinkTokenResult{}, apperr.Forbidden("access denied")
		}
		decrypted, err := crypto.Decrypt(item.AccessToken, s.encryptionKey)
		if err != nil {
			return CreateLinkTokenResult{}, fmt.Errorf("plaid: decrypt access token: %w", err)
		}
		updateAccessToken = decrypted
	}

	tok, exp, err := s.plaid.CreateLinkToken(ctx, userID.String(), updateAccessToken, redirectURI)
	if err != nil {
		return CreateLinkTokenResult{}, fmt.Errorf("plaid: create link token: %w", err)
	}
	return CreateLinkTokenResult{LinkToken: tok, Expiration: exp}, nil
}

func (s *PlaidService) ExchangePublicToken(ctx context.Context, userID, profileID uuid.UUID, publicToken string) (db.PlaidItem, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return db.PlaidItem{}, err
	}
	if err := s.requireProOrLifetime(ctx, userID); err != nil {
		return db.PlaidItem{}, err
	}
	if err := s.requireProfileOwnerOrMember(ctx, profileID, userID); err != nil {
		return db.PlaidItem{}, err
	}

	accessToken, itemID, err := s.plaid.ExchangePublicToken(ctx, publicToken)
	if err != nil {
		return db.PlaidItem{}, err
	}

	encryptedToken, err := crypto.Encrypt(accessToken, s.encryptionKey)
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: encrypt access token: %w", err)
	}

	// Fetch linked accounts and institution info.
	accounts, institutionID, err := s.plaid.GetAccounts(ctx, accessToken)
	if err != nil {
		// Non-fatal: store the item anyway; payment methods can be created later.
		accounts = nil
		institutionID = ""
	}

	// Resolve institution display name.
	var instIDPtr, instNamePtr *string
	if institutionID != "" {
		instIDPtr = &institutionID
		if name, nameErr := s.plaid.GetInstitutionName(ctx, institutionID); nameErr == nil && name != "" {
			instNamePtr = &name
		}
	}

	item, err := s.items.Create(ctx, db.CreatePlaidItemParams{
		UserID:          userID,
		BudgetProfileID: profileID,
		AccessToken:     encryptedToken,
		ItemID:          itemID,
		InstitutionID:   instIDPtr,
		InstitutionName: instNamePtr,
	})
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: store item: %w", err)
	}

	// Create one payment method per linked account, attributed to this user's
	// budget person row. Non-fatal: item is already stored above.
	pmCreated := s.createMissingPaymentMethods(ctx, item, userID, accounts)
	instName := ""
	if instNamePtr != nil {
		instName = *instNamePtr
	}
	log.Printf("plaid: item %s connected — institution=%q user=%s %d payment method(s) created", item.ItemID, instName, userID, pmCreated)

	// Trigger an immediate sync so transactions appear right after connecting.
	go func() {
		if err := s.SyncItem(context.Background(), item); err != nil {
			log.Printf("plaid: initial sync for item %s: %v", item.ID, err)
		}
	}()

	return item, nil
}

// SyncWarning reports that some connections on a budget the caller belongs to
// will never sync, because the member who linked them is on the free plan.
//
// Entitlement is per connection owner rather than per budget — deliberately,
// so a free account can't join a paid budget and get sync for nothing — which
// means a paid budget can quietly contain connections that are skipped on
// every run. Without this the clients have no way to show that: they only
// ever fetch the caller's own connections.
type SyncWarning struct {
	ProfileID       uuid.UUID
	BudgetName      string
	MemberName      string
	ConnectionCount int32
	IsCurrentUser   bool
}

// ListSyncWarnings returns one entry per (budget, member) whose connections
// the sync job skips. Empty for anyone unaffected, which is the common case.
func (s *PlaidService) ListSyncWarnings(ctx context.Context, userID uuid.UUID) ([]SyncWarning, error) {
	rows, err := s.items.ListUnsyncableForUser(ctx, userID)
	if err != nil {
		return nil, err
	}
	warnings := make([]SyncWarning, 0, len(rows))
	for _, row := range rows {
		warnings = append(warnings, SyncWarning{
			ProfileID:       row.BudgetProfileID,
			BudgetName:      row.BudgetName,
			MemberName:      row.MemberName,
			ConnectionCount: row.ConnectionCount,
			IsCurrentUser:   row.MemberUserID == userID,
		})
	}
	return warnings, nil
}

// ConnectionView is a connection plus the context a client needs to render
// and act on it: who linked it, whether the caller may touch it, and whether
// the sync job is actually picking it up.
//
// The budget-scoped list returns every member's connections, so none of this
// can be inferred client-side the way it could when a user only ever saw
// their own.
type ConnectionView struct {
	Item        db.PlaidItem
	OwnerName   string
	IsOwner     bool
	SyncEnabled bool
	// ResyncAvailableAt is nil when a manual resync is allowed right now.
	ResyncAvailableAt *time.Time
}

// manualResyncCooldown is how long a connection's owner must wait between
// manual resyncs of it.
//
// A resync replays the item's entire transaction history from Plaid, making
// it both the most expensive call this service makes and the one most likely
// to be hammered — it's reached for precisely when someone is waiting on a
// transaction that hasn't landed, which is exactly when repeating it feels
// productive and isn't.
const manualResyncCooldown = 24 * time.Hour

// resyncAvailableAt returns when the next manual resync is allowed, or nil if
// one is allowed now.
func resyncAvailableAt(item db.PlaidItem, now time.Time) *time.Time {
	if !item.LastManualResyncAt.Valid {
		return nil
	}
	next := item.LastManualResyncAt.Time.Add(manualResyncCooldown)
	if !now.Before(next) {
		return nil
	}
	return &next
}

func (s *PlaidService) GetConnections(ctx context.Context, userID uuid.UUID, profileID *uuid.UUID) ([]ConnectionView, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return nil, err
	}
	now := time.Now()

	if profileID != nil {
		if err := s.requireProfileOwnerOrMember(ctx, *profileID, userID); err != nil {
			return nil, err
		}
		rows, err := s.items.ListActiveWithOwnerByBudgetProfile(ctx, *profileID)
		if err != nil {
			return nil, err
		}
		views := make([]ConnectionView, 0, len(rows))
		for _, row := range rows {
			item := db.PlaidItem{
				ID:                 row.ID,
				UserID:             row.UserID,
				BudgetProfileID:    row.BudgetProfileID,
				AccessToken:        row.AccessToken,
				ItemID:             row.ItemID,
				InstitutionID:      row.InstitutionID,
				InstitutionName:    row.InstitutionName,
				Status:             row.Status,
				Cursor:             row.Cursor,
				LastSyncedAt:       row.LastSyncedAt,
				CreatedAt:          row.CreatedAt,
				LastManualResyncAt: row.LastManualResyncAt,
			}
			isOwner := row.UserID == userID
			view := ConnectionView{
				Item:        item,
				OwnerName:   row.OwnerName,
				IsOwner:     isOwner,
				SyncEnabled: row.OwnerPlan != "free",
			}
			// Only the owner can act on a resync, so telling anyone else when
			// one becomes available is noise on a button they don't have.
			if isOwner {
				view.ResyncAvailableAt = resyncAvailableAt(item, now)
			}
			views = append(views, view)
		}
		return views, nil
	}

	items, err := s.items.ListByUser(ctx, userID)
	if err != nil {
		return nil, err
	}
	caller, err := s.users.GetByID(ctx, userID)
	if err != nil {
		return nil, err
	}
	ownerName := userDisplayName(caller)
	views := make([]ConnectionView, 0, len(items))
	for _, item := range items {
		views = append(views, ConnectionView{
			Item:              item,
			OwnerName:         ownerName,
			IsOwner:           true,
			SyncEnabled:       caller.Plan != "free",
			ResyncAvailableAt: resyncAvailableAt(item, now),
		})
	}
	return views, nil
}

// ResyncConnection clears a connection's sync cursor so the next sync replays
// the item's full history from Plaid, then starts that sync immediately.
//
// The immediate sync is the point. Clearing the cursor alone would only make
// the connection eligible for the scheduled job, which runs Mon/Wed/Fri — so
// a button that did just that would appear to do nothing for up to three
// days, on the one screen where a user has gone looking because something
// already isn't arriving.
func (s *PlaidService) ResyncConnection(ctx context.Context, userID, connectionID uuid.UUID) (ConnectionView, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return ConnectionView{}, err
	}
	item, err := s.items.GetByID(ctx, connectionID)
	if err != nil {
		return ConnectionView{}, err
	}
	if item.UserID != userID {
		return ConnectionView{}, apperr.Forbidden("only the member who linked this connection can resync it")
	}
	if item.Status == "disconnected" {
		return ConnectionView{}, apperr.Invalid("this connection is disconnected — reconnect it to import transactions again")
	}

	owner, err := s.users.GetByID(ctx, userID)
	if err != nil {
		return ConnectionView{}, err
	}
	// Pointless rather than merely unentitled: the sync job skips free-tier
	// owners on every run, so a resync would clear the cursor and import
	// nothing, losing the user's place for no benefit.
	if owner.Plan == "free" {
		return ConnectionView{}, apperr.Invalid("free tier: Plaid bank sync requires a Pro subscription")
	}
	if next := resyncAvailableAt(item, time.Now()); next != nil {
		return ConnectionView{}, apperr.Invalid(fmt.Sprintf(
			"this connection was already resynced recently — the next one is available after %s",
			next.UTC().Format(time.RFC3339),
		))
	}

	reset, err := s.items.ResetCursor(ctx, connectionID)
	if err != nil {
		return ConnectionView{}, fmt.Errorf("plaid: reset cursor: %w", err)
	}
	log.Printf("plaid: item %s resync requested by user %s — cursor cleared, full history will be replayed", reset.ID, userID)

	// Detached from the request context for the same reason the post-connect
	// sync is: replaying a full history outlives the RPC that asked for it.
	go func() {
		if syncErr := s.SyncItem(context.Background(), reset); syncErr != nil {
			log.Printf("plaid: resync for item %s: %v", reset.ID, syncErr)
		}
	}()

	return ConnectionView{
		OwnerName:         userDisplayName(owner),
		Item:              reset,
		IsOwner:           true,
		SyncEnabled:       true,
		ResyncAvailableAt: resyncAvailableAt(reset, time.Now()),
	}, nil
}

func (s *PlaidService) Disconnect(ctx context.Context, userID, connectionID uuid.UUID) error {
	if err := s.requireUS(ctx, userID); err != nil {
		return err
	}
	item, err := s.items.GetByID(ctx, connectionID)
	if err != nil {
		return err
	}
	if item.UserID != userID {
		return apperr.Forbidden("access denied")
	}

	// Best-effort: notify Plaid that the item is being removed.
	if decrypted, err := crypto.Decrypt(item.AccessToken, s.encryptionKey); err == nil {
		_ = s.plaid.RemoveItem(ctx, decrypted)
	}

	_, err = s.items.UpdateStatus(ctx, db.UpdatePlaidItemStatusParams{
		ID:     connectionID,
		Status: "disconnected",
	})
	return err
}

// createMissingPaymentMethods creates a payment method for any account not
// already represented (by plaid_account_id, or by a name-match fallback
// for accounts Plaid re-IDs on reconnect). Non-fatal per-account — the
// caller's item/connection is already persisted regardless of outcome here.
// Returns the number of methods actually created.
func (s *PlaidService) createMissingPaymentMethods(ctx context.Context, item db.PlaidItem, userID uuid.UUID, accounts []plaidclient.Account) int {
	if len(accounts) == 0 {
		return 0
	}
	person, err := s.budgets.GetPersonByUserID(ctx, item.BudgetProfileID, userID)
	if err != nil {
		return 0
	}
	personID := int32(person.ID)

	created := 0
	for _, acct := range accounts {
		name := plaidclient.PlaidAccountName(acct.Name, acct.Mask)
		plaidAcctID := acct.PlaidAccountID

		// Exact match by plaid_account_id — same connection or stable ID.
		if _, existsErr := s.transactions.GetPaymentMethodByPlaidAccountID(ctx, plaidAcctID); existsErr == nil {
			continue
		}
		// Name-based fallback — Plaid issues new account_ids on reconnect.
		// If a method with the same name exists, update its plaid_account_id
		// so future reconnects dedup correctly, then skip creation.
		if existing, existsErr := s.transactions.GetPaymentMethodByUserAndName(ctx, userID, name); existsErr == nil {
			_ = s.transactions.UpdatePaymentMethodPlaidAccountID(ctx, existing.ID, plaidAcctID)
			continue
		}

		typeID := plaidclient.PlaidPaymentTypeID(acct.Type, acct.Subtype)
		if _, pmErr := s.transactions.CreatePaymentMethodFromPlaid(ctx, db.CreatePaymentMethodFromPlaidParams{
			Name:           name,
			PaymentTypeID:  &typeID,
			UserID:         &userID,
			BudgetPersonID: &personID,
			PlaidAccountID: &plaidAcctID,
			PlaidItemID:    &item.ID,
		}); pmErr == nil {
			log.Printf("plaid: created payment method %q (account %s)", name, plaidAcctID)
			created++
		}
	}
	return created
}

// RefreshAccounts re-fetches a connection's current account list from Plaid
// and reconciles payment_methods: creates one for any newly-added account,
// and deactivates any existing payment method for this connection whose
// account is no longer present. Called after a Link update-mode session
// completes (e.g. account selection) — update mode doesn't return a
// public_token, so there's nothing to exchange, only accounts to re-sync.
func (s *PlaidService) RefreshAccounts(ctx context.Context, userID, connectionID uuid.UUID) (db.PlaidItem, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return db.PlaidItem{}, err
	}
	item, err := s.items.GetByID(ctx, connectionID)
	if err != nil {
		return db.PlaidItem{}, err
	}
	if item.UserID != userID {
		return db.PlaidItem{}, apperr.Forbidden("access denied")
	}

	accessToken, err := crypto.Decrypt(item.AccessToken, s.encryptionKey)
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: decrypt access token: %w", err)
	}

	accounts, _, err := s.plaid.GetAccounts(ctx, accessToken)
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: get accounts: %w", err)
	}

	created := s.createMissingPaymentMethods(ctx, item, userID, accounts)

	stillPresent := make(map[string]bool, len(accounts))
	for _, acct := range accounts {
		stillPresent[acct.PlaidAccountID] = true
	}
	deactivated := 0
	existingMethods, err := s.transactions.ListActivePaymentMethodsByPlaidItem(ctx, item.ID)
	if err == nil {
		for _, pm := range existingMethods {
			if pm.PlaidAccountID == nil || stillPresent[*pm.PlaidAccountID] {
				continue
			}
			if deactivateErr := s.transactions.DeactivatePaymentMethod(ctx, pm.ID); deactivateErr == nil {
				log.Printf("plaid: refresh — deactivated payment method %q (account removed)", pm.Name)
				deactivated++
			}
		}
	}

	log.Printf("plaid: item %s refreshed — %d payment method(s) created, %d deactivated", item.ItemID, created, deactivated)
	return item, nil
}
