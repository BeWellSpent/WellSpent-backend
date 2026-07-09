package service

import (
	"context"
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/config"
	"github.com/mauro-afa91/spendsense/internal/repository"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	resend "github.com/resend/resend-go/v2"
	"go.uber.org/zap"
)

const inviteTTL = 7 * 24 * time.Hour

type InviteService struct {
	invites  repository.InviteRepository
	profiles repository.BudgetProfileRepository
	users    repository.UserRepository
	cfg      *config.Config
	log      *zap.Logger
}

func NewInviteService(
	invites repository.InviteRepository,
	profiles repository.BudgetProfileRepository,
	users repository.UserRepository,
	cfg *config.Config,
	log *zap.Logger,
) *InviteService {
	return &InviteService{invites: invites, profiles: profiles, users: users, cfg: cfg, log: log}
}

// Send creates an invite record and emails the recipient. Admin only.
func (s *InviteService) Send(ctx context.Context, profileID, callerID uuid.UUID, email, role string, budgetPersonID int64) (db.BudgetInvite, error) {
	// Caller must be admin on this budget.
	profile, err := s.profiles.GetByID(ctx, profileID)
	if err != nil {
		return db.BudgetInvite{}, err
	}
	if profile.UserID != callerID {
		person, pErr := s.profiles.GetPersonByUserID(ctx, profileID, callerID)
		if pErr != nil || person.Role != "admin" {
			return db.BudgetInvite{}, apperr.Forbidden("only admins can send invites")
		}
	}

	// If a person is linked, verify they belong to this budget.
	var personIDPtr *int64
	if budgetPersonID != 0 {
		pid := int32(budgetPersonID)
		if _, err := s.profiles.GetPerson(ctx, pid, profileID); err != nil {
			return db.BudgetInvite{}, apperr.NotFound("budget_person", fmt.Sprintf("%d", budgetPersonID))
		}
		personIDPtr = &budgetPersonID
	}

	expiresAt := pgtype.Timestamptz{Time: time.Now().UTC().Add(inviteTTL), Valid: true}
	inv, err := s.invites.Create(ctx, db.CreateInviteParams{
		BudgetProfileID: profileID,
		Email:           strings.ToLower(strings.TrimSpace(email)),
		Role:            role,
		InvitedBy:       callerID,
		BudgetPersonID:  personIDPtr,
		ExpiresAt:       expiresAt,
	})
	if err != nil {
		return db.BudgetInvite{}, err
	}

	// Fire email — non-fatal if email fails (invite is already persisted).
	if s.cfg.ResendAPIKey == "" {
		s.log.Warn("invite.email.skipped: RESEND_API_KEY not set", zap.String("to", email))
	} else if err := s.sendEmail(profile.Name, inv.Token, email); err != nil {
		s.log.Error("invite.email.failed", zap.String("to", email), zap.Error(err))
	} else {
		s.log.Info("invite.email.sent", zap.String("to", email))
	}

	return inv, nil
}

func (s *InviteService) sendEmail(budgetName string, token uuid.UUID, to string) error {
	client := resend.NewClient(s.cfg.ResendAPIKey)
	link := fmt.Sprintf("%s/invite/%s", strings.TrimRight(s.cfg.FrontendURL, "/"), token.String())
	body := fmt.Sprintf(
		`<p>You've been invited to collaborate on the <strong>%s</strong> budget in WellSpent.</p>`+
			`<p><a href="%s">Accept invitation</a></p>`+
			`<p>This link expires in 7 days.</p>`,
		budgetName, link,
	)
	_, err := client.Emails.Send(&resend.SendEmailRequest{
		From:    s.cfg.ResendFromEmail,
		To:      []string{to},
		Subject: fmt.Sprintf("You're invited to the %s budget", budgetName),
		Html:    body,
	})
	return err
}

// List returns all invites for a budget. Admin only.
func (s *InviteService) List(ctx context.Context, profileID, callerID uuid.UUID) ([]db.BudgetInvite, error) {
	profile, err := s.profiles.GetByID(ctx, profileID)
	if err != nil {
		return nil, err
	}
	if profile.UserID != callerID {
		person, pErr := s.profiles.GetPersonByUserID(ctx, profileID, callerID)
		if pErr != nil || person.Role != "admin" {
			return nil, apperr.Forbidden("only admins can list invites")
		}
	}
	return s.invites.ListByProfile(ctx, profileID)
}

// Cancel marks a pending invite as cancelled. Admin only.
func (s *InviteService) Cancel(ctx context.Context, inviteID, callerID uuid.UUID) (db.BudgetInvite, error) {
	inv, err := s.invites.GetByID(ctx, inviteID)
	if err != nil {
		return db.BudgetInvite{}, err
	}
	profile, err := s.profiles.GetByID(ctx, inv.BudgetProfileID)
	if err != nil {
		return db.BudgetInvite{}, err
	}
	if profile.UserID != callerID {
		person, pErr := s.profiles.GetPersonByUserID(ctx, inv.BudgetProfileID, callerID)
		if pErr != nil || person.Role != "admin" {
			return db.BudgetInvite{}, apperr.Forbidden("only admins can cancel invites")
		}
	}
	if inv.Status != "pending" {
		return db.BudgetInvite{}, apperr.Invalid("only pending invites can be cancelled")
	}
	return s.invites.UpdateStatus(ctx, db.UpdateInviteStatusParams{ID: inviteID, Status: "cancelled"})
}

// GetByToken returns invite details for the public invite page (no auth).
func (s *InviteService) GetByToken(ctx context.Context, token uuid.UUID) (db.GetInviteByTokenRow, error) {
	row, err := s.invites.GetByToken(ctx, token)
	if err != nil {
		return db.GetInviteByTokenRow{}, err
	}
	if row.Status == "cancelled" {
		return db.GetInviteByTokenRow{}, apperr.Invalid("this invitation has been cancelled")
	}
	if row.Status == "accepted" {
		return db.GetInviteByTokenRow{}, apperr.Invalid("this invitation has already been accepted")
	}
	if row.ExpiresAt.Time.Before(time.Now().UTC()) {
		_, _ = s.invites.UpdateStatus(ctx, db.UpdateInviteStatusParams{ID: row.ID, Status: "expired"})
		return db.GetInviteByTokenRow{}, apperr.Invalid("this invitation has expired")
	}
	return row, nil
}

// Accept links the authenticated user to the budget. JWT required.
func (s *InviteService) Accept(ctx context.Context, token uuid.UUID, callerID uuid.UUID) (uuid.UUID, error) {
	row, err := s.GetByToken(ctx, token)
	if err != nil {
		return uuid.UUID{}, err
	}

	// Guard: user must not already be a member.
	already, _ := s.profiles.ExistsPersonForUser(ctx, row.BudgetProfileID, callerID)
	if already {
		// Idempotent: mark accepted and return the budget ID so the frontend can redirect.
		_, _ = s.invites.UpdateStatus(ctx, db.UpdateInviteStatusParams{ID: row.ID, Status: "accepted"})
		return row.BudgetProfileID, nil
	}

	if row.BudgetPersonID != nil {
		// Link the existing placeholder person to this user.
		pid := int32(*row.BudgetPersonID)
		_, err = s.profiles.LinkPersonToUser(ctx, db.LinkBudgetPersonToUserParams{
			ID:     pid,
			UserID: callerID,
			Role:   row.Role,
		})
	} else {
		// Create a new BudgetPerson for this user.
		caller, userErr := s.users.GetByID(ctx, callerID)
		if userErr != nil {
			return uuid.UUID{}, userErr
		}
		parts := []string{}
		if caller.FirstName != nil {
			parts = append(parts, *caller.FirstName)
		}
		if caller.LastName != nil {
			parts = append(parts, *caller.LastName)
		}
		displayName := strings.Join(parts, " ")
		if displayName == "" {
			displayName = caller.Email
		}
		_, err = s.profiles.AddPerson(ctx, db.AddBudgetPersonToProfileParams{
			BudgetProfileID: row.BudgetProfileID,
			UserName:        &displayName,
			UserID:          &callerID,
			Color:           "",
			Role:            row.Role,
		})
	}
	if err != nil {
		return uuid.UUID{}, err
	}

	_, _ = s.invites.UpdateStatus(ctx, db.UpdateInviteStatusParams{ID: row.ID, Status: "accepted"})
	return row.BudgetProfileID, nil
}
