package service

import (
	"context"
	"fmt"
	"strings"

	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	resend "github.com/resend/resend-go/v2"
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
	}
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
