package service

import (
	"context"
	"fmt"
	"math"
	"math/big"
	"strconv"
	"strings"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/repository"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"github.com/mauro-afa91/spendsense/internal/tax"
)

type BudgetProfileService struct {
	profiles     repository.BudgetProfileRepository
	transactions repository.TransactionRepository
	users        repository.UserRepository
}

func NewBudgetProfileService(
	profiles repository.BudgetProfileRepository,
	transactions repository.TransactionRepository,
	users repository.UserRepository,
) *BudgetProfileService {
	return &BudgetProfileService{profiles: profiles, transactions: transactions, users: users}
}

// ── Ownership ─────────────────────────────────────────────────────────────────

func (s *BudgetProfileService) assertOwner(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetProfile, error) {
	profile, err := s.profiles.GetByID(ctx, profileID)
	if err != nil {
		return db.BudgetProfile{}, err
	}
	if profile.UserID != userID {
		return db.BudgetProfile{}, apperr.Forbidden("access denied")
	}
	return profile, nil
}

func (s *BudgetProfileService) assertPeriodOwner(ctx context.Context, periodID, userID uuid.UUID) error {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		return err
	}
	_, err = s.assertOwner(ctx, period.BudgetProfileID, userID)
	return err
}

// ── Profile CRUD ──────────────────────────────────────────────────────────────

func (s *BudgetProfileService) List(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error) {
	return s.profiles.ListByUserID(ctx, userID)
}

func (s *BudgetProfileService) Get(ctx context.Context, id, userID uuid.UUID) (db.BudgetProfile, error) {
	return s.assertOwner(ctx, id, userID)
}

func (s *BudgetProfileService) Create(ctx context.Context, userID uuid.UUID, name, cycle string) (db.BudgetProfile, db.BudgetPeriod, error) {
	exists, err := s.profiles.ExistsByNameAndUser(ctx, name, userID)
	if err != nil {
		return db.BudgetProfile{}, db.BudgetPeriod{}, fmt.Errorf("budget_profile: check exists: %w", err)
	}
	if exists {
		return db.BudgetProfile{}, db.BudgetPeriod{}, apperr.Duplicate("budget_profile", "name", name)
	}

	createParams := db.CreateBudgetProfileParams{
		UserID: userID,
		Name:   name,
		Cycle:  cycle,
	}
	// Propagate owner's country to the profile so tax features are country-gated.
	if owner, ownerErr := s.users.GetByID(ctx, userID); ownerErr == nil {
		createParams.CountryCode = owner.CountryCode
	}
	profile, err := s.profiles.Create(ctx, createParams)
	if err != nil {
		return db.BudgetProfile{}, db.BudgetPeriod{}, err
	}

	// Auto-add budget owner as the first person on the profile.
	owner, err := s.users.GetByID(ctx, userID)
	if err == nil {
		parts := []string{}
		if owner.FirstName != nil {
			parts = append(parts, *owner.FirstName)
		}
		if owner.LastName != nil {
			parts = append(parts, *owner.LastName)
		}
		displayName := strings.Join(parts, " ")
		if displayName == "" {
			displayName = owner.Email
		}
		_, _ = s.profiles.AddPerson(ctx, db.AddBudgetPersonToProfileParams{
			BudgetProfileID: profile.ID,
			UserName:        &displayName,
			UserID:          &userID,
			Color:           "",
		})
	}

	// Create the first period immediately.
	period, err := s.createNextPeriod(ctx, profile)
	if err != nil {
		// Non-fatal: profile was created, period creation failed.
		return profile, db.BudgetPeriod{}, nil
	}
	return profile, period, nil
}

func (s *BudgetProfileService) Update(ctx context.Context, id, userID uuid.UUID, name, cycle string) (db.BudgetProfile, error) {
	if _, err := s.assertOwner(ctx, id, userID); err != nil {
		return db.BudgetProfile{}, err
	}
	return s.profiles.Update(ctx, db.UpdateBudgetProfileParams{ID: id, Name: name, Cycle: cycle})
}

func (s *BudgetProfileService) Delete(ctx context.Context, id, userID uuid.UUID) error {
	if _, err := s.assertOwner(ctx, id, userID); err != nil {
		return err
	}
	return s.profiles.Delete(ctx, id)
}

// ── Period ────────────────────────────────────────────────────────────────────

// CreateBudgetPeriod creates the next cycle window for a profile. Dates are computed
// from the profile's cycle and the previous period's end date (today for the first).
// Recurring income sources are pre-filled as entries; fixed+recurring transactions
// are carried forward from the previous period.
func (s *BudgetProfileService) CreateBudgetPeriod(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetPeriod, error) {
	profile, err := s.assertOwner(ctx, profileID, userID)
	if err != nil {
		return db.BudgetPeriod{}, err
	}
	return s.createNextPeriod(ctx, profile)
}

func (s *BudgetProfileService) createNextPeriod(ctx context.Context, profile db.BudgetProfile) (db.BudgetPeriod, error) {
	var startDate, endDate time.Time

	latest, err := s.profiles.GetLatestPeriod(ctx, profile.ID)
	if err != nil {
		// No previous period — use today as the start.
		startDate, endDate = computeFirstPeriodDates(profile.Cycle)
	} else {
		startDate, endDate = computeNextPeriodDates(profile.Cycle, latest.EndDate.Time)
	}

	period, err := s.profiles.CreatePeriod(ctx, db.CreateBudgetPeriodParams{
		BudgetProfileID: profile.ID,
		StartDate:       pgtype.Date{Time: startDate, Valid: true},
		EndDate:         pgtype.Date{Time: endDate, Valid: true},
	})
	if err != nil {
		return db.BudgetPeriod{}, err
	}

	// Pre-fill recurring income sources as entries.
	sources, _ := s.profiles.ListIncomeSources(ctx, profile.ID)
	var annualBeforeTaxIncome float64
	for _, src := range sources {
		if !src.Recurring {
			continue
		}
		srcID := src.ID
		_, _ = s.profiles.CreateIncomeEntry(ctx, db.CreateIncomeEntryParams{
			BudgetPeriodID: period.ID,
			IncomeSourceID: &srcID,
			BudgetPersonID: src.BudgetPersonID,
			Name:           &src.Name,
			Amount:         src.DefaultAmount,
		})
		// Accumulate before-tax income for the tax reserve calculation.
		if src.BeforeTax && src.DefaultAmount.Valid {
			f, _ := new(big.Float).SetInt(src.DefaultAmount.Int).Float64()
			if src.DefaultAmount.Exp > 0 {
				mul := math.Pow(10, float64(src.DefaultAmount.Exp))
				f *= mul
			} else if src.DefaultAmount.Exp < 0 {
				div := math.Pow(10, float64(-src.DefaultAmount.Exp))
				f /= div
			}
			annualBeforeTaxIncome += f
		}
	}

	// Upsert tax reserve savings source for US profiles with before-tax income.
	if profile.CountryCode != nil && *profile.CountryCode == "US" && annualBeforeTaxIncome > 0 {
		if owner, ownerErr := s.users.GetByID(ctx, profile.UserID); ownerErr == nil {
			stateCode := ""
			if owner.StateCode != nil {
				stateCode = *owner.StateCode
			}
			fsInt, _ := strconv.Atoi(owner.FilingStatus)
			estimate := tax.Estimate(annualBeforeTaxIncome, stateCode, tax.FilingStatus(fsInt))
			cents := int64(math.Round(estimate.MonthlySaving * 100))
			monthly := pgtype.Numeric{Int: big.NewInt(cents), Exp: -2, Valid: true}
			_, _ = s.profiles.UpsertTaxReserveSavingsSource(ctx, db.UpsertTaxReserveSavingsSourceParams{
				BudgetProfileID: profile.ID,
				Amount:          monthly,
			})
		}
	}

	// Carry forward fixed+recurring transactions from the previous period.
	if latest.ID != uuid.Nil {
		prevTxs, _ := s.transactions.ListFixedRecurring(ctx, latest.ID)
		for _, tx := range prevTxs {
			if tx.RenewalDate.Valid && tx.RenewalDate.Time.Before(startDate) {
				continue // expired
			}
			_, _ = s.transactions.Create(ctx, db.CreateTransactionParams{
				Name:                   tx.Name,
				Amount:                 tx.Amount,
				PlannedAmount:          tx.PlannedAmount,
				RenewalDate:            tx.RenewalDate,
				Recurring:              tx.Recurring,
				BudgetPeriodID:         &period.ID,
				CategoryID:             tx.CategoryID,
				PaymentMethodID:        tx.PaymentMethodID,
				TransactionFrequencyID: tx.TransactionFrequencyID,
				TransactionTypeID:      tx.TransactionTypeID,
			})
		}
	}

	return period, nil
}

func (s *BudgetProfileService) ListBudgetPeriods(ctx context.Context, profileID, userID uuid.UUID) ([]db.BudgetPeriod, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListPeriods(ctx, profileID)
}

func (s *BudgetProfileService) GetBudgetPeriod(ctx context.Context, periodID, userID uuid.UUID) (db.BudgetPeriod, error) {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		return db.BudgetPeriod{}, err
	}
	if _, err := s.assertOwner(ctx, period.BudgetProfileID, userID); err != nil {
		return db.BudgetPeriod{}, err
	}
	return period, nil
}

// ── People ────────────────────────────────────────────────────────────────────

type ProfilePersonInput struct {
	UserName string
	UserID   *uuid.UUID
	Color    string
}

func (s *BudgetProfileService) AddPeople(ctx context.Context, profileID, userID uuid.UUID, people []ProfilePersonInput) ([]db.BudgetToProfileMapping, error) {
	profile, err := s.assertOwner(ctx, profileID, userID)
	if err != nil {
		return nil, err
	}
	var results []db.BudgetToProfileMapping
	for _, p := range people {
		// Country constraint: if the person being added is a registered user,
		// they must be in the same country as the budget profile.
		if p.UserID != nil {
			person, personErr := s.users.GetByID(ctx, *p.UserID)
			if personErr == nil && profile.CountryCode != nil && person.CountryCode != nil &&
				*person.CountryCode != *profile.CountryCode {
				return nil, apperr.Invalid("all budget members must be in the same country")
			}
		}
		exists, err := s.profiles.ExistsPerson(ctx, profileID, p.UserName)
		if err != nil {
			return nil, err
		}
		if exists {
			return nil, apperr.Duplicate("person", "name", p.UserName)
		}
		m, err := s.profiles.AddPerson(ctx, db.AddBudgetPersonToProfileParams{
			BudgetProfileID: profileID,
			UserName:        &p.UserName,
			UserID:          p.UserID,
			Color:           p.Color,
		})
		if err != nil {
			return nil, err
		}
		results = append(results, m)
	}
	return results, nil
}

func (s *BudgetProfileService) UpdatePerson(ctx context.Context, profileID uuid.UUID, personID int32, color string, userID uuid.UUID) (db.BudgetToProfileMapping, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return db.BudgetToProfileMapping{}, err
	}
	return s.profiles.UpdatePerson(ctx, db.UpdateBudgetPersonParams{
		ID:              personID,
		BudgetProfileID: profileID,
		Color:           color,
	})
}

func (s *BudgetProfileService) ListPeople(ctx context.Context, profileID, userID uuid.UUID) ([]db.BudgetToProfileMapping, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListPeople(ctx, profileID)
}

func (s *BudgetProfileService) RemovePerson(ctx context.Context, profileID uuid.UUID, personID int32, replacementPersonID int32, replacementPMID uuid.UUID, userID uuid.UUID) error {
	profile, err := s.assertOwner(ctx, profileID, userID)
	if err != nil {
		return err
	}
	person, err := s.profiles.GetPerson(ctx, personID, profileID)
	if err != nil {
		return err
	}
	// Protect the profile owner from removal.
	if person.UserID != nil && *person.UserID == profile.UserID {
		return apperr.Invalid("budget owner cannot be removed")
	}
	if replacementPersonID == 0 {
		return s.profiles.SoftRemovePerson(ctx, db.SoftRemovePersonFromProfileParams{
			PersonID:        personID,
			BudgetProfileID: profileID,
		})
	}
	if _, err := s.profiles.GetPerson(ctx, replacementPersonID, profileID); err != nil {
		return apperr.NotFound("replacement_person", fmt.Sprintf("%d", replacementPersonID))
	}
	repID := replacementPersonID
	return s.profiles.SoftRemovePersonAndReassign(ctx, db.SoftRemovePersonAndReassignFromProfileParams{
		PersonID:            personID,
		BudgetProfileID:     profileID,
		ReplacementPmID:     replacementPMID,
		ReplacementPersonID: &repID,
	})
}

// ── Income Sources ────────────────────────────────────────────────────────────

type IncomeSourceInput struct {
	Name             string
	IncomeType       string
	DefaultAmount    pgtype.Numeric
	Recurring        bool
	BudgetPersonID   *int32
	PaymentFrequency string
	BeforeTax        bool
}

func (s *BudgetProfileService) AddIncomeSource(ctx context.Context, profileID, userID uuid.UUID, inp IncomeSourceInput) (db.IncomeSource, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return db.IncomeSource{}, err
	}
	return s.profiles.AddIncomeSource(ctx, db.AddIncomeSourceParams{
		BudgetProfileID:  profileID,
		BudgetPersonID:   inp.BudgetPersonID,
		Name:             inp.Name,
		IncomeType:       inp.IncomeType,
		DefaultAmount:    inp.DefaultAmount,
		Recurring:        inp.Recurring,
		PaymentFrequency: inp.PaymentFrequency,
		BeforeTax:        inp.BeforeTax,
	})
}

func (s *BudgetProfileService) ListIncomeSources(ctx context.Context, profileID, userID uuid.UUID) ([]db.IncomeSource, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListIncomeSources(ctx, profileID)
}

func (s *BudgetProfileService) UpdateIncomeSource(ctx context.Context, id int32, profileID, userID uuid.UUID, inp IncomeSourceInput) (db.IncomeSource, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return db.IncomeSource{}, err
	}
	return s.profiles.UpdateIncomeSource(ctx, db.UpdateIncomeSourceParams{
		ID:               id,
		BudgetProfileID:  profileID,
		Name:             inp.Name,
		IncomeType:       inp.IncomeType,
		DefaultAmount:    inp.DefaultAmount,
		Recurring:        inp.Recurring,
		BudgetPersonID:   inp.BudgetPersonID,
		PaymentFrequency: inp.PaymentFrequency,
		BeforeTax:        inp.BeforeTax,
	})
}

func (s *BudgetProfileService) DeleteIncomeSource(ctx context.Context, id int32, profileID, userID uuid.UUID) error {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return err
	}
	return s.profiles.DeleteIncomeSource(ctx, db.DeleteIncomeSourceParams{
		ID:              id,
		BudgetProfileID: profileID,
	})
}

// ── Income Entries ────────────────────────────────────────────────────────────

func (s *BudgetProfileService) ListIncomeEntries(ctx context.Context, periodID, userID uuid.UUID) ([]db.IncomeEntry, error) {
	if err := s.assertPeriodOwner(ctx, periodID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListIncomeEntries(ctx, periodID)
}

func (s *BudgetProfileService) UpdateIncomeEntry(ctx context.Context, id int32, periodID uuid.UUID, amount pgtype.Numeric, userID uuid.UUID) (db.IncomeEntry, error) {
	if err := s.assertPeriodOwner(ctx, periodID, userID); err != nil {
		return db.IncomeEntry{}, err
	}
	return s.profiles.UpdateIncomeEntry(ctx, db.UpdateIncomeEntryParams{
		ID:             id,
		BudgetPeriodID: periodID,
		Amount:         amount,
	})
}

// ── Savings Sources ───────────────────────────────────────────────────────────

type SavingsSourceInput struct {
	Name           string
	Amount         pgtype.Numeric
	Frequency      string
	BudgetPersonID *int32
}

func (s *BudgetProfileService) AddSavingsSource(ctx context.Context, profileID, userID uuid.UUID, inp SavingsSourceInput) (db.SavingsSource, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return db.SavingsSource{}, err
	}
	return s.profiles.AddSavingsSource(ctx, db.AddSavingsSourceParams{
		BudgetProfileID: profileID,
		BudgetPersonID:  inp.BudgetPersonID,
		Name:            inp.Name,
		Amount:          inp.Amount,
		Frequency:       inp.Frequency,
	})
}

func (s *BudgetProfileService) ListSavingsSources(ctx context.Context, profileID, userID uuid.UUID) ([]db.SavingsSource, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListSavingsSources(ctx, profileID)
}

func (s *BudgetProfileService) UpdateSavingsSource(ctx context.Context, id int32, profileID, userID uuid.UUID, inp SavingsSourceInput) (db.SavingsSource, error) {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return db.SavingsSource{}, err
	}
	return s.profiles.UpdateSavingsSource(ctx, db.UpdateSavingsSourceParams{
		ID:              id,
		BudgetProfileID: profileID,
		Name:            inp.Name,
		Amount:          inp.Amount,
		Frequency:       inp.Frequency,
		BudgetPersonID:  inp.BudgetPersonID,
	})
}

func (s *BudgetProfileService) DeleteSavingsSource(ctx context.Context, id int32, profileID, userID uuid.UUID) error {
	if _, err := s.assertOwner(ctx, profileID, userID); err != nil {
		return err
	}
	return s.profiles.DeleteSavingsSource(ctx, db.DeleteSavingsSourceParams{
		ID:              id,
		BudgetProfileID: profileID,
	})
}

// ── Period date helpers ───────────────────────────────────────────────────────

func computeFirstPeriodDates(cycle string) (start, end time.Time) {
	now := time.Now().UTC()
	today := time.Date(now.Year(), now.Month(), now.Day(), 0, 0, 0, 0, time.UTC)
	switch cycle {
	case "weekly":
		return today, today.AddDate(0, 0, 6)
	case "bi_weekly":
		return today, today.AddDate(0, 0, 13)
	case "yearly":
		s := time.Date(now.Year(), 1, 1, 0, 0, 0, 0, time.UTC)
		return s, time.Date(now.Year(), 12, 31, 0, 0, 0, 0, time.UTC)
	default: // monthly
		s := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
		e := time.Date(now.Year(), now.Month()+1, 1, 0, 0, 0, 0, time.UTC).AddDate(0, 0, -1)
		return s, e
	}
}

func computeNextPeriodDates(cycle string, prevEnd time.Time) (start, end time.Time) {
	start = prevEnd.AddDate(0, 0, 1)
	switch cycle {
	case "weekly":
		return start, start.AddDate(0, 0, 6)
	case "bi_weekly":
		return start, start.AddDate(0, 0, 13)
	case "yearly":
		e := time.Date(start.Year(), 12, 31, 0, 0, 0, 0, start.Location())
		return start, e
	default: // monthly
		e := time.Date(start.Year(), start.Month()+1, 1, 0, 0, 0, 0, start.Location()).AddDate(0, 0, -1)
		return start, e
	}
}
