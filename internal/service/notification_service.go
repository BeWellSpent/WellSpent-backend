package service

import (
	"context"
	"fmt"
	"strings"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	resend "github.com/resend/resend-go/v2"
	apns2 "github.com/sideshow/apns2"
	"github.com/sideshow/apns2/payload"
	apnstoken "github.com/sideshow/apns2/token"
	"go.uber.org/zap"
)

type NotificationService struct {
	notifs      repository.NotificationRepository
	transactions repository.TransactionRepository
	profiles    repository.BudgetProfileRepository
	allocations repository.ExpenseAllocationRepository
	users       repository.UserRepository
	cfg         *config.Config
	log         *zap.Logger
}

func NewNotificationService(
	notifs repository.NotificationRepository,
	transactions repository.TransactionRepository,
	profiles repository.BudgetProfileRepository,
	allocations repository.ExpenseAllocationRepository,
	users repository.UserRepository,
	cfg *config.Config,
	log *zap.Logger,
) *NotificationService {
	if notifs == nil {
		panic("NewNotificationService: notifs is required")
	}
	if transactions == nil {
		panic("NewNotificationService: transactions is required")
	}
	if profiles == nil {
		panic("NewNotificationService: profiles is required")
	}
	if allocations == nil {
		panic("NewNotificationService: allocations is required")
	}
	if users == nil {
		panic("NewNotificationService: users is required")
	}
	if cfg == nil {
		panic("NewNotificationService: cfg is required")
	}
	if log == nil {
		panic("NewNotificationService: log is required")
	}
	return &NotificationService{
		notifs:       notifs,
		transactions: transactions,
		profiles:     profiles,
		allocations:  allocations,
		users:        users,
		cfg:          cfg,
		log:          log,
	}
}

// ─── Notification queries ─────────────────────────────────────────────────────

func (s *NotificationService) ListNotifications(ctx context.Context, userID uuid.UUID, profileID *uuid.UUID, limit int32) ([]db.Notification, int32, error) {
	notifs, err := s.notifs.List(ctx, userID, profileID, limit)
	if err != nil {
		return nil, 0, err
	}
	count, err := s.notifs.GetUnreadCount(ctx, userID)
	if err != nil {
		return nil, 0, err
	}
	return notifs, count, nil
}

func (s *NotificationService) MarkRead(ctx context.Context, userID uuid.UUID, ids []uuid.UUID) error {
	return s.notifs.MarkRead(ctx, userID, ids)
}

func (s *NotificationService) GetUnreadCount(ctx context.Context, userID uuid.UUID) (int32, error) {
	return s.notifs.GetUnreadCount(ctx, userID)
}

// ─── Subscription management ──────────────────────────────────────────────────

type UpsertSubscriptionInput struct {
	ProfileID        uuid.UUID
	AlertType        string
	Channel          string
	ThresholdPct     float64
	ThresholdScope   string
	CategoryID       *int32
	NotifyAllMembers bool
}

func (s *NotificationService) ListSubscriptions(ctx context.Context, userID, profileID uuid.UUID) ([]db.AlertSubscription, error) {
	return s.notifs.ListSubscriptions(ctx, userID, profileID)
}

func (s *NotificationService) UpsertSubscription(ctx context.Context, userID uuid.UUID, inp UpsertSubscriptionInput) (db.AlertSubscription, error) {
	// Caller must be a member of the budget.
	profile, err := s.profiles.GetByID(ctx, inp.ProfileID)
	if err != nil {
		return db.AlertSubscription{}, err
	}
	if profile.UserID != userID {
		if _, err := s.profiles.GetPersonByUserID(ctx, inp.ProfileID, userID); err != nil {
			return db.AlertSubscription{}, err
		}
	}
	// Free tier: new_transaction alerts and more than 2 subscriptions per budget require Pro.
	caller, err := s.users.GetByID(ctx, userID)
	if err != nil {
		return db.AlertSubscription{}, err
	}
	if caller.Plan == "free" {
		if inp.AlertType == "new_transaction" {
			return db.AlertSubscription{}, apperr.Invalid("free tier: new_transaction alerts require a Pro subscription")
		}
		existing, listErr := s.notifs.ListSubscriptions(ctx, userID, inp.ProfileID)
		if listErr == nil && len(existing) >= 2 {
			isUpdate := false
			for _, sub := range existing {
				if sub.AlertType == inp.AlertType {
					isUpdate = true
					break
				}
			}
			if !isUpdate {
				return db.AlertSubscription{}, apperr.Invalid("free tier: budget alerts are limited to 2; upgrade to Pro for unlimited")
			}
		}
	}

	var thresholdPct pgtype.Numeric
	if inp.AlertType == "spending_threshold" {
		if err := thresholdPct.Scan(fmt.Sprintf("%g", inp.ThresholdPct)); err != nil {
			thresholdPct = pgtype.Numeric{}
		}
	}
	var scope *string
	if inp.ThresholdScope != "" {
		s2 := inp.ThresholdScope
		scope = &s2
	}

	return s.notifs.UpsertSubscription(ctx, db.UpsertAlertSubscriptionParams{
		UserID:           userID,
		BudgetProfileID:  inp.ProfileID,
		AlertType:        inp.AlertType,
		Channel:          inp.Channel,
		ThresholdPct:     thresholdPct,
		ThresholdScope:   scope,
		CategoryID:       inp.CategoryID,
		NotifyAllMembers: inp.NotifyAllMembers,
	})
}

func (s *NotificationService) DeleteSubscription(ctx context.Context, userID uuid.UUID, subscriptionID uuid.UUID) error {
	return s.notifs.DeleteSubscription(ctx, subscriptionID, userID)
}

// ─── Trigger hooks ────────────────────────────────────────────────────────────

// HandleNewTransaction fires when a variable transaction is created.
// It notifies OTHER budget members who subscribed to new_transaction alerts,
// and checks spending_threshold subscriptions.
// All failures are logged and silently swallowed — the transaction is already saved.
func (s *NotificationService) HandleNewTransaction(ctx context.Context, tx db.Transaction, periodID uuid.UUID, callerUserID uuid.UUID) {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		s.log.Error("notification.HandleNewTransaction: get period", zap.Error(err))
		return
	}
	profileID := period.BudgetProfileID

	txName := "(unnamed)"
	if tx.Name != nil {
		txName = *tx.Name
	}

	// new_transaction: notify OTHER subscribed members.
	newTxSubs, err := s.notifs.GetBudgetSubscribers(ctx, profileID, "new_transaction")
	if err != nil {
		s.log.Error("notification.HandleNewTransaction: get new_transaction subscribers", zap.Error(err))
	}
	for _, sub := range newTxSubs {
		if sub.UserID == callerUserID {
			continue // don't notify the person who created the transaction
		}
		title := "New transaction added"
		body := fmt.Sprintf("A new transaction was recorded: %s", txName)
		s.deliver(ctx, sub, &profileID, "new_transaction", title, body)
	}

	// spending_threshold: check for any threshold crossings.
	s.checkSpendingThreshold(ctx, tx, periodID, profileID)
}

// HandleReviewPending fires when a Plaid-imported transaction is queued for review.
// It notifies the subscribing user (admin/collaborator) directly.
func (s *NotificationService) HandleReviewPending(ctx context.Context, profileID uuid.UUID, txName string) {
	subs, err := s.notifs.GetBudgetSubscribers(ctx, profileID, "review_pending")
	if err != nil {
		s.log.Error("notification.HandleReviewPending: get subscribers", zap.Error(err))
		return
	}
	title := "Transaction pending review"
	body := fmt.Sprintf("A new import needs your review: %s", txName)
	for _, sub := range subs {
		s.deliver(ctx, sub, &profileID, "review_pending", title, body)
	}
}

// HandleReviewPendingBatch is the Plaid-sync counterpart to HandleReviewPending:
// one sync run can queue several imports for review across a single budget,
// and each of those already gets its own row in the To Review tab, so this
// fires a single aggregated notification per sync run instead of one per
// transaction. HandleReviewPending (singular) stays as-is for the manual
// "flag for review" path, which is inherently one transaction at a time.
func (s *NotificationService) HandleReviewPendingBatch(ctx context.Context, profileID uuid.UUID, count int) {
	if count <= 0 {
		return
	}
	subs, err := s.notifs.GetBudgetSubscribers(ctx, profileID, "review_pending")
	if err != nil {
		s.log.Error("notification.HandleReviewPendingBatch: get subscribers", zap.Error(err))
		return
	}
	title := "Transactions pending review"
	body := "A new import needs your review."
	if count > 1 {
		body = fmt.Sprintf("%d new imports need your review.", count)
	}
	for _, sub := range subs {
		s.deliver(ctx, sub, &profileID, "review_pending", title, body)
	}
}

// HandlePlaidTransactionsImported fires once per sync run (not once per
// transaction) when the Plaid sync job imports one or more new Variable
// transactions that don't need review — i.e. plain spending, not something
// auto-confirmed against a fixed expense (no action needed) or queued for
// review (covered separately by HandleReviewPendingBatch). Reuses the
// new_transaction alert type. Unlike HandleNewTransaction (the manual
// CreateTransaction path), there's no caller to exclude — nobody in the app
// performed this action, so every subscriber is notified.
func (s *NotificationService) HandlePlaidTransactionsImported(ctx context.Context, profileID uuid.UUID, count int) {
	if count <= 0 {
		return
	}
	subs, err := s.notifs.GetBudgetSubscribers(ctx, profileID, "new_transaction")
	if err != nil {
		s.log.Error("notification.HandlePlaidTransactionsImported: get subscribers", zap.Error(err))
		return
	}
	title := "New transactions imported"
	body := "A new transaction was imported from your bank."
	if count > 1 {
		body = fmt.Sprintf("%d new transactions were imported from your bank.", count)
	}
	for _, sub := range subs {
		s.deliver(ctx, sub, &profileID, "new_transaction", title, body)
	}
}

// HandlePeriodCreated fires when createNextPeriod creates a new budget period.
func (s *NotificationService) HandlePeriodCreated(ctx context.Context, period db.BudgetPeriod) {
	profileID := period.BudgetProfileID
	subs, err := s.notifs.GetBudgetSubscribers(ctx, profileID, "period_created")
	if err != nil {
		s.log.Error("notification.HandlePeriodCreated: get subscribers", zap.Error(err))
		return
	}
	start := ""
	end := ""
	if period.StartDate.Valid {
		start = period.StartDate.Time.Format("Jan 2")
	}
	if period.EndDate.Valid {
		end = period.EndDate.Time.Format("Jan 2, 2006")
	}
	title := "New budget period started"
	body := fmt.Sprintf("Your budget period %s – %s is now active.", start, end)
	for _, sub := range subs {
		s.deliver(ctx, sub, &profileID, "period_created", title, body)
	}
}

// ─── Spending threshold ───────────────────────────────────────────────────────

func (s *NotificationService) checkSpendingThreshold(ctx context.Context, newTx db.Transaction, periodID, profileID uuid.UUID) {
	subs, err := s.notifs.GetBudgetSubscribers(ctx, profileID, "spending_threshold")
	if err != nil || len(subs) == 0 {
		return
	}

	allocs, err := s.allocations.List(ctx, profileID)
	if err != nil {
		s.log.Error("notification.checkSpendingThreshold: list allocations", zap.Error(err))
		return
	}

	// Sum all non-excluded variable transactions in the period.
	variableTypeID := int32(2)
	txs, err := s.transactions.List(ctx, db.ListTransactionsParams{
		BudgetPeriodID:    periodID,
		TransactionTypeID: &variableTypeID,
	})
	if err != nil {
		s.log.Error("notification.checkSpendingThreshold: list transactions", zap.Error(err))
		return
	}

	// Build per-category sums (after the new transaction is included).
	catSums := make(map[int32]float64)
	totalSum := 0.0
	for _, t := range txs {
		if t.IsExcluded {
			continue
		}
		amt := numericToFloat(t.Amount)
		totalSum += amt
		if t.CategoryID != nil {
			catSums[*t.CategoryID] += amt
		}
	}

	// Plan totals (sum of all allocations) and per-category plans.
	catPlan := make(map[int32]float64)
	totalPlan := 0.0
	for _, a := range allocs {
		amt := numericToFloat(a.PlannedAmount)
		totalPlan += amt
		catPlan[a.CategoryID] += amt
	}

	// Previous total (before this transaction was added).
	newTxAmt := numericToFloat(newTx.Amount)
	prevTotal := totalSum - newTxAmt
	var newTxCatID int32
	if newTx.CategoryID != nil {
		newTxCatID = *newTx.CategoryID
	}
	prevCatSum := catSums[newTxCatID] - newTxAmt

	for _, sub := range subs {
		threshold := numericToFloat(sub.ThresholdPct)
		if threshold <= 0 {
			continue
		}
		scope := "budget"
		if sub.ThresholdScope != nil {
			scope = *sub.ThresholdScope
		}

		var planAmt, prevSum, newSum float64
		switch scope {
		case "category":
			catID := int32(0)
			if sub.CategoryID != nil {
				catID = *sub.CategoryID
			}
			if catID == 0 {
				continue
			}
			planAmt = catPlan[catID]
			if newTx.CategoryID != nil && *newTx.CategoryID == catID {
				prevSum = prevCatSum
				newSum = catSums[catID]
			} else {
				// This transaction isn't in the subscribed category — no crossing possible.
				continue
			}
		default: // "budget"
			planAmt = totalPlan
			prevSum = prevTotal
			newSum = totalSum
		}

		if planAmt <= 0 {
			continue
		}
		pct := threshold / 100.0
		crossed := prevSum < pct*planAmt && newSum >= pct*planAmt
		if !crossed {
			continue
		}

		txName := "(unnamed)"
		if newTx.Name != nil {
			txName = *newTx.Name
		}
		var scopeLabel string
		if scope == "category" {
			scopeLabel = "category"
		} else {
			scopeLabel = "budget"
		}
		title := fmt.Sprintf("Spending alert: %.0f%% of %s plan reached", threshold, scopeLabel)
		body := fmt.Sprintf(
			"Adding %q brought your %s spending to %.0f%% of the plan.",
			txName, scopeLabel, (newSum/planAmt)*100,
		)
		s.deliver(ctx, sub, &profileID, "spending_threshold", title, body)
	}
}

// ─── Delivery ─────────────────────────────────────────────────────────────────

// deliver creates an in-app notification and optionally sends an email, based on
// the subscription's channel setting. Fan-out to all budget members when
// notify_all_members is true.
func (s *NotificationService) deliver(ctx context.Context, sub db.AlertSubscription, profileID *uuid.UUID, alertType, title, body string) {
	recipients := []uuid.UUID{sub.UserID}
	if sub.NotifyAllMembers && profileID != nil {
		people, err := s.profiles.ListPeople(ctx, *profileID)
		if err == nil {
			recipients = nil
			for _, p := range people {
				if p.UserID != nil && p.IsActive {
					recipients = append(recipients, *p.UserID)
				}
			}
		}
	}

	for _, userID := range recipients {
		s.sendInApp(ctx, userID, profileID, alertType, title, body)
		if sub.Channel == "email" || sub.Channel == "both" {
			s.sendEmail(ctx, userID, title, body)
		}
	}
}

func (s *NotificationService) sendInApp(ctx context.Context, userID uuid.UUID, profileID *uuid.UUID, alertType, title, body string) {
	_, err := s.notifs.Create(ctx, db.CreateNotificationParams{
		UserID:          userID,
		BudgetProfileID: profileID,
		AlertType:       alertType,
		Title:           title,
		Body:            body,
	})
	if err != nil {
		s.log.Error("notification.sendInApp: create", zap.String("user_id", userID.String()), zap.Error(err))
		return
	}
	// Push isn't a separate `channel` value — it rides along with every
	// in-app notification for users who've registered a device, the same
	// way every other mobile app surfaces its in-app alerts as pushes too.
	s.sendPush(ctx, userID, title, body)
}

func (s *NotificationService) sendEmail(ctx context.Context, userID uuid.UUID, subject, htmlBody string) {
	if s.cfg.ResendAPIKey == "" {
		s.log.Warn("notification.sendEmail: RESEND_API_KEY not set, skipping email", zap.String("user_id", userID.String()))
		return
	}
	user, err := s.users.GetByID(ctx, userID)
	if err != nil {
		s.log.Error("notification.sendEmail: get user", zap.String("user_id", userID.String()), zap.Error(err))
		return
	}
	client := resend.NewClient(s.cfg.ResendAPIKey)
	wrappedBody := fmt.Sprintf(
		`<p>%s</p><p style="color:#888;font-size:12px">You are receiving this email because you subscribed to budget alerts in WellSpent. <a href="%s">Manage alerts</a></p>`,
		strings.ReplaceAll(htmlBody, "\n", "<br>"),
		strings.TrimRight(s.cfg.FrontendURL, "/"),
	)
	_, sendErr := client.Emails.Send(&resend.SendEmailRequest{
		From:    s.cfg.ResendFromEmail,
		To:      []string{user.Email},
		Subject: subject,
		Html:    wrappedBody,
	})
	if sendErr != nil {
		s.log.Error("notification.sendEmail: send failed", zap.String("to", user.Email), zap.Error(sendErr))
	}
}

// sendPush delivers a real APNs push to every device the user has
// registered. Best-effort, same posture as sendEmail: skips with a log
// warning (not an error) if APNS_AUTH_KEY isn't configured, and logs but
// swallows per-device send failures so one bad token doesn't block the rest.
func (s *NotificationService) sendPush(ctx context.Context, userID uuid.UUID, title, body string) {
	if s.cfg.APNSAuthKey == "" {
		s.log.Warn("notification.sendPush: APNS_AUTH_KEY not set, skipping push", zap.String("user_id", userID.String()))
		return
	}

	tokens, err := s.notifs.ListDeviceTokensForUser(ctx, userID)
	if err != nil {
		s.log.Error("notification.sendPush: list device tokens", zap.String("user_id", userID.String()), zap.Error(err))
		return
	}
	if len(tokens) == 0 {
		return
	}

	authKey, err := apnstoken.AuthKeyFromBytes([]byte(s.cfg.APNSAuthKey))
	if err != nil {
		s.log.Error("notification.sendPush: parse APNS_AUTH_KEY", zap.Error(err))
		return
	}
	client := apns2.NewTokenClient(&apnstoken.Token{
		AuthKey: authKey,
		KeyID:   s.cfg.APNSKeyID,
		TeamID:  s.cfg.APNSTeamID,
	})
	if s.cfg.APNSEnvironment == "production" {
		client = client.Production()
	} else {
		client = client.Development()
	}

	body2 := payload.NewPayload().AlertTitle(title).AlertBody(body).Badge(1)
	for _, dt := range tokens {
		notification := &apns2.Notification{
			DeviceToken: dt.Token,
			Topic:       s.cfg.APNSBundleID,
			Payload:     body2,
		}
		res, sendErr := client.PushWithContext(ctx, notification)
		if sendErr != nil {
			s.log.Error("notification.sendPush: send failed", zap.String("token", dt.Token), zap.Error(sendErr))
			continue
		}
		if !res.Sent() {
			s.log.Warn("notification.sendPush: not sent", zap.String("token", dt.Token), zap.String("reason", res.Reason))
		}
	}
}

// RegisterDeviceToken upserts an APNs device token for the authenticated
// user, keyed by the token itself — a device re-registering (e.g. after a
// reinstall, when APNs may issue the same or a new token) just updates its
// owner rather than creating duplicate rows.
func (s *NotificationService) RegisterDeviceToken(ctx context.Context, userID uuid.UUID, tokenValue, platform string) error {
	if platform != "ios" {
		return apperr.Invalid("platform must be \"ios\"")
	}
	if tokenValue == "" {
		return apperr.Invalid("token is required")
	}
	_, err := s.notifs.UpsertDeviceToken(ctx, db.UpsertDeviceTokenParams{
		UserID:   userID,
		Platform: platform,
		Token:    tokenValue,
	})
	return err
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

func numericToFloat(n pgtype.Numeric) float64 {
	if !n.Valid {
		return 0
	}
	f, _ := n.Float64Value()
	if !f.Valid {
		return 0
	}
	return f.Float64
}
