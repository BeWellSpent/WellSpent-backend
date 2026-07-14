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
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/BeWellSpent/wellspent-backend/internal/tax"
)

type BudgetProfileService struct {
	profiles      repository.BudgetProfileRepository
	transactions  repository.TransactionRepository
	fixedExpenses repository.FixedExpenseRepository
	users         repository.UserRepository
}

func NewBudgetProfileService(
	profiles repository.BudgetProfileRepository,
	transactions repository.TransactionRepository,
	fixedExpenses repository.FixedExpenseRepository,
	users repository.UserRepository,
) *BudgetProfileService {
	return &BudgetProfileService{profiles: profiles, transactions: transactions, fixedExpenses: fixedExpenses, users: users}
}

// ── Access helpers ────────────────────────────────────────────────────────────

// getUserRole returns the profile and the caller's effective role.
// Profile owners are always admin regardless of the role column (handles legacy data and the
// creation flow where the person row may not yet exist).
func (s *BudgetProfileService) getUserRole(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetProfile, string, error) {
	profile, err := s.profiles.GetByID(ctx, profileID)
	if err != nil {
		return db.BudgetProfile{}, "", err
	}
	if profile.UserID == userID {
		return profile, "admin", nil
	}
	person, err := s.profiles.GetPersonByUserID(ctx, profileID, userID)
	if err != nil {
		return db.BudgetProfile{}, "", apperr.Forbidden("access denied")
	}
	return profile, person.Role, nil
}

func (s *BudgetProfileService) assertAdmin(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetProfile, error) {
	profile, role, err := s.getUserRole(ctx, profileID, userID)
	if err != nil {
		return db.BudgetProfile{}, err
	}
	if role != "admin" {
		return db.BudgetProfile{}, apperr.Forbidden("access denied")
	}
	return profile, nil
}

func (s *BudgetProfileService) assertCollaboratorOrAbove(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetProfile, error) {
	profile, role, err := s.getUserRole(ctx, profileID, userID)
	if err != nil {
		return db.BudgetProfile{}, err
	}
	if role != "admin" && role != "collaborator" {
		return db.BudgetProfile{}, apperr.Forbidden("access denied")
	}
	return profile, nil
}

func (s *BudgetProfileService) assertMember(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetProfile, error) {
	profile, _, err := s.getUserRole(ctx, profileID, userID)
	if err != nil {
		return db.BudgetProfile{}, err
	}
	return profile, nil
}

func (s *BudgetProfileService) assertPeriodMember(ctx context.Context, periodID, userID uuid.UUID) (db.BudgetProfile, error) {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		return db.BudgetProfile{}, err
	}
	return s.assertMember(ctx, period.BudgetProfileID, userID)
}

func (s *BudgetProfileService) assertPeriodCollaborator(ctx context.Context, periodID, userID uuid.UUID) (db.BudgetProfile, error) {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		return db.BudgetProfile{}, err
	}
	return s.assertCollaboratorOrAbove(ctx, period.BudgetProfileID, userID)
}

// ── Profile CRUD ──────────────────────────────────────────────────────────────

func (s *BudgetProfileService) List(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error) {
	return s.profiles.ListByUserOrMember(ctx, userID)
}

func (s *BudgetProfileService) Get(ctx context.Context, id, userID uuid.UUID) (db.BudgetProfile, error) {
	return s.assertMember(ctx, id, userID)
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
			Role:            "admin",
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
	if _, err := s.assertAdmin(ctx, id, userID); err != nil {
		return db.BudgetProfile{}, err
	}
	return s.profiles.Update(ctx, db.UpdateBudgetProfileParams{ID: id, Name: name, Cycle: cycle})
}

func (s *BudgetProfileService) Delete(ctx context.Context, id, userID uuid.UUID) error {
	if _, err := s.assertAdmin(ctx, id, userID); err != nil {
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
	profile, err := s.assertAdmin(ctx, profileID, userID)
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
		today := time.Now().UTC().Truncate(24 * time.Hour)
		// Idempotency: latest period is still active, nothing to create.
		if !latest.EndDate.Time.Before(today) {
			return latest, nil
		}
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

	// Archive the period that just ended now that the new one is live.
	if latest.ID != (uuid.UUID{}) {
		_ = s.profiles.ArchivePeriod(ctx, latest.ID)
	}

	// Pre-fill recurring income sources as entries.
	sources, _ := s.profiles.ListIncomeSources(ctx, profile.ID)
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
	}

	// Recalculate per-person tax reserve entries.
	s.recalculateTaxReserve(ctx, profile.ID)

	// Recreate savings transactions for the new period.
	savingsSrcs, _ := s.profiles.ListSavingsSources(ctx, profile.ID)
	for _, src := range savingsSrcs {
		if src.PaymentMethodID == nil || len(src.PaymentDays) == 0 {
			continue
		}
		s.createSavingsTransactions(ctx, profile.ID, profile.UserID, src)
	}

	// Spawn fixed expense transactions for the new period — only for expenses
	// actually due this period's month (see isFixedExpenseDueInMonth), and at
	// most once per calendar month even if weekly/bi-weekly cycles land more
	// than one period inside that month. WEEK-unit expenses use a separate
	// per-date path since a single period can contain several due weeks.
	fixedExpenses, _ := s.fixedExpenses.List(ctx, profile.ID)
	txTypeFixed := int32(1)
	monthStart := time.Date(startDate.Year(), startDate.Month(), 1, 0, 0, 0, 0, time.UTC)
	monthEnd := monthStart.AddDate(0, 1, 0)
	for _, fe := range fixedExpenses {
		// Auto-deactivate when the plan's end date has passed.
		if fe.EndDate.Valid && startDate.After(fe.EndDate.Time) {
			_ = s.fixedExpenses.Deactivate(ctx, db.DeactivateFixedExpenseParams{
				ID:              fe.ID,
				BudgetProfileID: profile.ID,
			})
			continue
		}
		if isFixedExpenseWeekUnit(fe) {
			s.spawnWeeklyFixedExpenseOccurrences(ctx, fe, period.ID, startDate, endDate)
			continue
		}
		if !isFixedExpenseDueInMonth(fe, monthStart) {
			continue
		}
		feID := fe.ID
		exists, _ := s.fixedExpenses.HasTransactionInMonth(ctx, db.FixedExpenseHasTransactionInMonthParams{
			FixedExpenseID: feID,
			MonthStart:     pgtype.Date{Time: monthStart, Valid: true},
			MonthEnd:       pgtype.Date{Time: monthEnd, Valid: true},
		})
		if exists {
			continue
		}
		txDate := pgtype.Date{Time: fixedExpenseDateInMonth(fe, monthStart), Valid: true}
		name := fe.Name
		_, _ = s.transactions.Create(ctx, db.CreateTransactionParams{
			Name:              &name,
			Amount:            fe.PlannedAmount,
			PlannedAmount:     fe.PlannedAmount,
			Date:              txDate,
			BudgetPeriodID:    &period.ID,
			CategoryID:        fe.CategoryID,
			PaymentMethodID:   fe.PaymentMethodID,
			TransactionTypeID: &txTypeFixed,
			FixedExpenseID:    &feID,
		})
	}

	return period, nil
}

// fixedExpenseMonthIndex converts a date to an absolute month number
// (year*12 + month) for interval arithmetic.
func fixedExpenseMonthIndex(t time.Time) int {
	return t.Year()*12 + int(t.Month())
}

// fixedExpenseAnchor returns the anchor time used for interval arithmetic:
// fe.AnchorDate when explicitly set (lets a fixed expense start in the
// future instead of at creation time), otherwise fe.CreatedAt.
func fixedExpenseAnchor(fe db.FixedExpense) time.Time {
	if fe.AnchorDate.Valid {
		return fe.AnchorDate.Time
	}
	return fe.CreatedAt.Time
}

// isFixedExpenseDueInMonth reports whether fe is due in the month starting at
// monthStart, using fixedExpenseAnchor's month as the anchor: due when the
// number of months elapsed since the anchor is a multiple of IntervalMonths.
// A monthStart before the anchor month is never due (covers a future-dated
// AnchorDate not having arrived yet).
func isFixedExpenseDueInMonth(fe db.FixedExpense, monthStart time.Time) bool {
	interval := int(fe.IntervalMonths)
	if interval < 1 {
		interval = 1
	}
	diff := fixedExpenseMonthIndex(monthStart) - fixedExpenseMonthIndex(fixedExpenseAnchor(fe))
	if diff < 0 {
		return false
	}
	return diff%interval == 0
}

// fixedExpenseDateInMonth returns fe's transaction date within monthStart's
// month, clamping DayOfMonth to that month's last day when needed.
func fixedExpenseDateInMonth(fe db.FixedExpense, monthStart time.Time) time.Time {
	lastDay := time.Date(monthStart.Year(), monthStart.Month()+1, 0, 0, 0, 0, 0, time.UTC).Day()
	day := int(fe.DayOfMonth)
	if day < 1 {
		day = 1
	}
	if day > lastDay {
		day = lastDay
	}
	return time.Date(monthStart.Year(), monthStart.Month(), day, 0, 0, 0, 0, time.UTC)
}

// FixedExpenseNextDueDate returns the next date on or after `from` that fe is
// due, as a full transaction date (day-of-month/day-of-week applied and
// clamped as appropriate). Used to surface a computed, non-persisted
// "next due" field to clients.
func FixedExpenseNextDueDate(fe db.FixedExpense, from time.Time) time.Time {
	if isFixedExpenseWeekUnit(fe) {
		return fixedExpenseNextDueDateWeekly(fe, from)
	}
	interval := int(fe.IntervalMonths)
	if interval < 1 {
		interval = 1
	}
	monthStart := time.Date(from.Year(), from.Month(), 1, 0, 0, 0, 0, time.UTC)
	// A future anchor (AnchorDate ahead of `from`) can be more than `interval`
	// months away, which the interval-bounded search below wouldn't reach —
	// no due month can occur before the anchor's own month, so start there.
	anchor := fixedExpenseAnchor(fe)
	anchorMonthStart := time.Date(anchor.Year(), anchor.Month(), 1, 0, 0, 0, 0, time.UTC)
	if anchorMonthStart.After(monthStart) {
		monthStart = anchorMonthStart
	}
	for i := 0; i < interval; i++ {
		if isFixedExpenseDueInMonth(fe, monthStart) {
			return fixedExpenseDateInMonth(fe, monthStart)
		}
		monthStart = monthStart.AddDate(0, 1, 0)
	}
	// Unreachable in practice (a multiple of interval is always found within
	// `interval` months), but fall back to `from`'s month if it somehow isn't.
	return fixedExpenseDateInMonth(fe, time.Date(from.Year(), from.Month(), 1, 0, 0, 0, 0, time.UTC))
}

// ── Weekly cadence (frequency_unit = WEEK) ──────────────────────────────────
//
// Parallel to the month-index math above, but weeks are counted linearly from
// a fixed Monday reference point rather than ISO calendar week-of-year, to
// avoid year-boundary rollover edge cases (week 52/53 -> week 1).

const (
	frequencyUnitMonth int16 = 1 // matches FrequencyUnit_FREQUENCY_UNIT_MONTH (also the default for UNSPECIFIED/0)
	frequencyUnitWeek  int16 = 2 // matches FrequencyUnit_FREQUENCY_UNIT_WEEK
)

// mondayEpoch is a stable Monday reference point (Jan 5 1970) for linear
// week-index arithmetic.
var mondayEpoch = time.Date(1970, 1, 5, 0, 0, 0, 0, time.UTC)

func isFixedExpenseWeekUnit(fe db.FixedExpense) bool {
	return fe.FrequencyUnit == frequencyUnitWeek
}

// fixedExpenseWeekIndex converts a date to a linear week number since
// mondayEpoch, for interval arithmetic parallel to fixedExpenseMonthIndex.
func fixedExpenseWeekIndex(t time.Time) int {
	days := int(t.Truncate(24*time.Hour).Sub(mondayEpoch).Hours() / 24)
	return int(math.Floor(float64(days) / 7))
}

// fixedExpenseWeekStart returns the Monday of t's week.
func fixedExpenseWeekStart(t time.Time) time.Time {
	t = t.Truncate(24 * time.Hour)
	offset := (int(t.Weekday()) + 6) % 7 // days since Monday (time.Weekday: Sunday=0..Saturday=6)
	return t.AddDate(0, 0, -offset)
}

// isFixedExpenseDueInWeek reports whether fe is due in the week starting at
// ws (a Monday), mirroring isFixedExpenseDueInMonth.
func isFixedExpenseDueInWeek(fe db.FixedExpense, ws time.Time) bool {
	interval := int(fe.IntervalWeeks)
	if interval < 1 {
		interval = 1
	}
	diff := fixedExpenseWeekIndex(ws) - fixedExpenseWeekIndex(fixedExpenseWeekStart(fixedExpenseAnchor(fe)))
	if diff < 0 {
		return false
	}
	return diff%interval == 0
}

// fixedExpenseDateInWeek returns fe's transaction date within the week
// starting at ws, applying DayOfWeek (1=Monday..7=Sunday). Every week has
// all 7 days, so unlike day-of-month there's no clamping needed.
func fixedExpenseDateInWeek(fe db.FixedExpense, ws time.Time) time.Time {
	dow := int(fe.DayOfWeek)
	if dow < 1 || dow > 7 {
		dow = 1
	}
	return ws.AddDate(0, 0, dow-1)
}

// fixedExpenseNextDueDateWeekly is FixedExpenseNextDueDate's WEEK-unit path.
func fixedExpenseNextDueDateWeekly(fe db.FixedExpense, from time.Time) time.Time {
	interval := int(fe.IntervalWeeks)
	if interval < 1 {
		interval = 1
	}
	ws := fixedExpenseWeekStart(from)
	anchorWeekStart := fixedExpenseWeekStart(fixedExpenseAnchor(fe))
	if anchorWeekStart.After(ws) {
		ws = anchorWeekStart
	}
	for i := 0; i < interval; i++ {
		if isFixedExpenseDueInWeek(fe, ws) {
			return fixedExpenseDateInWeek(fe, ws)
		}
		ws = ws.AddDate(0, 0, 7)
	}
	// Unreachable in practice, same rationale as the monthly fallback above.
	return fixedExpenseDateInWeek(fe, fixedExpenseWeekStart(from))
}

// spawnWeeklyFixedExpenseOccurrences spawns one transaction for every due
// week (per IntervalWeeks/DayOfWeek) whose date falls within
// [startDate, endDate). Unlike MONTH-unit expenses — at most one transaction
// per period — a WEEK-unit expense can have several due occurrences inside a
// single period (e.g. "every week" within a monthly budget period), so
// de-duplication is per exact date rather than per calendar month.
func (s *BudgetProfileService) spawnWeeklyFixedExpenseOccurrences(ctx context.Context, fe db.FixedExpense, periodID uuid.UUID, startDate, endDate time.Time) {
	txTypeFixed := int32(1)
	feID := fe.ID
	name := fe.Name
	for ws := fixedExpenseWeekStart(startDate); ws.Before(endDate); ws = ws.AddDate(0, 0, 7) {
		if !isFixedExpenseDueInWeek(fe, ws) {
			continue
		}
		date := fixedExpenseDateInWeek(fe, ws)
		if date.Before(startDate) || !date.Before(endDate) {
			continue
		}
		exists, _ := s.fixedExpenses.HasTransactionOnDate(ctx, db.FixedExpenseHasTransactionOnDateParams{
			FixedExpenseID: feID,
			TargetDate:     pgtype.Date{Time: date, Valid: true},
		})
		if exists {
			continue
		}
		_, _ = s.transactions.Create(ctx, db.CreateTransactionParams{
			Name:              &name,
			Amount:            fe.PlannedAmount,
			PlannedAmount:     fe.PlannedAmount,
			Date:              pgtype.Date{Time: date, Valid: true},
			BudgetPeriodID:    &periodID,
			CategoryID:        fe.CategoryID,
			PaymentMethodID:   fe.PaymentMethodID,
			TransactionTypeID: &txTypeFixed,
			FixedExpenseID:    &feID,
		})
	}
}

func (s *BudgetProfileService) ListBudgetPeriods(ctx context.Context, profileID, userID uuid.UUID) ([]db.BudgetPeriod, error) {
	if _, err := s.assertMember(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListPeriods(ctx, profileID)
}

func (s *BudgetProfileService) GetBudgetPeriod(ctx context.Context, periodID, userID uuid.UUID) (db.BudgetPeriod, error) {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		return db.BudgetPeriod{}, err
	}
	if _, err := s.assertMember(ctx, period.BudgetProfileID, userID); err != nil {
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
	profile, err := s.assertAdmin(ctx, profileID, userID)
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
		role := "unspecified"
		if p.UserID != nil {
			role = "collaborator"
		}
		m, err := s.profiles.AddPerson(ctx, db.AddBudgetPersonToProfileParams{
			BudgetProfileID: profileID,
			UserName:        &p.UserName,
			UserID:          p.UserID,
			Color:           p.Color,
			Role:            role,
		})
		if err != nil {
			return nil, err
		}
		results = append(results, m)
	}
	return results, nil
}

func (s *BudgetProfileService) UpdatePerson(ctx context.Context, profileID uuid.UUID, personID int32, color string, userID uuid.UUID) (db.BudgetToProfileMapping, error) {
	if _, err := s.assertAdmin(ctx, profileID, userID); err != nil {
		return db.BudgetToProfileMapping{}, err
	}
	return s.profiles.UpdatePerson(ctx, db.UpdateBudgetPersonParams{
		ID:              personID,
		BudgetProfileID: profileID,
		Color:           color,
	})
}

func (s *BudgetProfileService) UpdatePersonRole(ctx context.Context, profileID uuid.UUID, personID int32, role string, userID uuid.UUID) (db.BudgetToProfileMapping, error) {
	if _, err := s.assertAdmin(ctx, profileID, userID); err != nil {
		return db.BudgetToProfileMapping{}, err
	}
	return s.profiles.UpdatePersonRole(ctx, db.UpdateBudgetPersonRoleParams{
		ID:              personID,
		BudgetProfileID: profileID,
		Role:            role,
	})
}

func (s *BudgetProfileService) ListPeople(ctx context.Context, profileID, userID uuid.UUID) ([]db.BudgetToProfileMapping, error) {
	if _, err := s.assertMember(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListPeople(ctx, profileID)
}

func (s *BudgetProfileService) RemovePerson(ctx context.Context, profileID uuid.UUID, personID int32, replacementPersonID int32, replacementPMID uuid.UUID, userID uuid.UUID) error {
	profile, err := s.assertAdmin(ctx, profileID, userID)
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
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return db.IncomeSource{}, err
	}
	src, err := s.profiles.AddIncomeSource(ctx, db.AddIncomeSourceParams{
		BudgetProfileID:  profileID,
		BudgetPersonID:   inp.BudgetPersonID,
		Name:             inp.Name,
		IncomeType:       inp.IncomeType,
		DefaultAmount:    inp.DefaultAmount,
		Recurring:        inp.Recurring,
		PaymentFrequency: inp.PaymentFrequency,
		BeforeTax:        inp.BeforeTax,
	})
	if err != nil {
		return db.IncomeSource{}, err
	}
	s.recalculateTaxReserve(ctx, profileID)
	return src, nil
}

func (s *BudgetProfileService) ListIncomeSources(ctx context.Context, profileID, userID uuid.UUID) ([]db.IncomeSource, error) {
	if _, err := s.assertMember(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListIncomeSources(ctx, profileID)
}

func (s *BudgetProfileService) UpdateIncomeSource(ctx context.Context, id int32, profileID, userID uuid.UUID, inp IncomeSourceInput) (db.IncomeSource, error) {
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return db.IncomeSource{}, err
	}
	src, err := s.profiles.UpdateIncomeSource(ctx, db.UpdateIncomeSourceParams{
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
	if err != nil {
		return db.IncomeSource{}, err
	}
	s.recalculateTaxReserve(ctx, profileID)
	return src, nil
}

func (s *BudgetProfileService) DeleteIncomeSource(ctx context.Context, id int32, profileID, userID uuid.UUID) error {
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return err
	}
	if err := s.profiles.DeleteIncomeSource(ctx, db.DeleteIncomeSourceParams{
		ID:              id,
		BudgetProfileID: profileID,
	}); err != nil {
		return err
	}
	s.recalculateTaxReserve(ctx, profileID)
	return nil
}

// recalculateTaxReserve recomputes per-person tax reserve savings entries for a
// US profile whenever income sources change. It is best-effort — failures are
// silently ignored so the primary mutation is never blocked.
func (s *BudgetProfileService) recalculateTaxReserve(ctx context.Context, profileID uuid.UUID) {
	profile, err := s.profiles.GetByID(ctx, profileID)
	if err != nil {
		return
	}

	// Resolve effective country (fall back to owner when profile predates the column).
	countryCode := ""
	if profile.CountryCode != nil {
		countryCode = *profile.CountryCode
	}
	if countryCode == "" {
		if owner, oErr := s.users.GetByID(ctx, profile.UserID); oErr == nil && owner.CountryCode != nil {
			countryCode = *owner.CountryCode
		}
	}
	if countryCode != "US" {
		return
	}

	sources, err := s.profiles.ListIncomeSources(ctx, profileID)
	if err != nil {
		return
	}

	// Build people list once — used both to find the owner's person entry and
	// to map person → linked user for individual tax settings.
	people, _ := s.profiles.ListPeople(ctx, profileID)
	var ownerPersonID *int32
	userByPerson := map[int32]uuid.UUID{}
	for _, p := range people {
		if p.UserID != nil {
			userByPerson[p.ID] = *p.UserID
			if *p.UserID == profile.UserID {
				pid := p.ID
				ownerPersonID = &pid
			}
		}
	}

	// Group annual before-tax income by person ID.
	// Unattributed income (person 0) falls back to the owner's person entry.
	incomeByPerson := map[int32]float64{}
	for _, src := range sources {
		if !src.BeforeTax || !src.DefaultAmount.Valid {
			continue
		}
		f, _ := new(big.Float).SetInt(src.DefaultAmount.Int).Float64()
		if src.DefaultAmount.Exp > 0 {
			f *= math.Pow(10, float64(src.DefaultAmount.Exp))
		} else if src.DefaultAmount.Exp < 0 {
			f /= math.Pow(10, float64(-src.DefaultAmount.Exp))
		}
		f *= 12 // monthly → annual

		pid := int32(0)
		if src.BudgetPersonID != nil {
			pid = *src.BudgetPersonID
		}
		if pid == 0 && ownerPersonID != nil {
			pid = *ownerPersonID
		}
		incomeByPerson[pid] += f
	}

	// Delete all existing tax reserve entries; re-create one per person.
	_ = s.profiles.DeleteTaxReserveSavingsSource(ctx, profileID)
	if len(incomeByPerson) == 0 {
		return
	}

	// Load owner for fallback tax settings.
	owner, err := s.users.GetByID(ctx, profile.UserID)
	if err != nil {
		return
	}
	ownerState := ""
	if owner.StateCode != nil {
		ownerState = *owner.StateCode
	}
	ownerFS, _ := strconv.Atoi(owner.FilingStatus)

	toMonthly := func(annual float64) pgtype.Numeric {
		return pgtype.Numeric{Int: big.NewInt(int64(math.Round(annual / 12 * 100))), Exp: -2, Valid: true}
	}

	for personID, annualIncome := range incomeByPerson {
		if annualIncome == 0 {
			continue
		}
		pid := personID // local copy for pointer

		// Use the person's own tax settings if they are a linked user.
		stateCode, fsInt := ownerState, ownerFS
		if uid, ok := userByPerson[personID]; ok {
			if u, uErr := s.users.GetByID(ctx, uid); uErr == nil {
				if u.StateCode != nil {
					stateCode = *u.StateCode
				}
				if u.FilingStatus != "" {
					fsInt, _ = strconv.Atoi(u.FilingStatus)
				}
			}
		}

		estimate := tax.Estimate(annualIncome, stateCode, tax.FilingStatus(fsInt))
		_, _ = s.profiles.UpsertTaxReserveSavingsSource(ctx, db.UpsertTaxReserveSavingsSourceParams{
			BudgetProfileID: profileID,
			BudgetPersonID:  &pid,
			Amount:          toMonthly(estimate.TotalAnnual),
			FederalAmount:   toMonthly(estimate.FederalTax),
			StateAmount:     toMonthly(estimate.StateTax),
		})
	}
}

// ── Income Entries ────────────────────────────────────────────────────────────

func (s *BudgetProfileService) ListIncomeEntries(ctx context.Context, periodID, userID uuid.UUID) ([]db.IncomeEntry, error) {
	if _, err := s.assertPeriodMember(ctx, periodID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListIncomeEntries(ctx, periodID)
}

func (s *BudgetProfileService) UpdateIncomeEntry(ctx context.Context, id int32, periodID uuid.UUID, amount pgtype.Numeric, userID uuid.UUID) (db.IncomeEntry, error) {
	if _, err := s.assertPeriodCollaborator(ctx, periodID, userID); err != nil {
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
	Name            string
	Amount          pgtype.Numeric
	PaymentMethodID *uuid.UUID
	PaymentDays     []int32 // 1=monthly, 2=bi-weekly, 4=weekly; owner inferred from PM
}

func paymentDaysFrequency(n int) string {
	switch n {
	case 2:
		return "bi_weekly"
	case 4:
		return "weekly"
	default:
		return "monthly"
	}
}

func (s *BudgetProfileService) AddSavingsSource(ctx context.Context, profileID, userID uuid.UUID, inp SavingsSourceInput) (db.SavingsSource, error) {
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return db.SavingsSource{}, err
	}
	n := len(inp.PaymentDays)
	if n != 1 && n != 2 && n != 4 {
		return db.SavingsSource{}, apperr.Invalid("payment_days must have 1, 2, or 4 entries")
	}

	var personID *int32
	if inp.PaymentMethodID != nil {
		pm, err := s.transactions.GetPaymentMethod(ctx, *inp.PaymentMethodID)
		if err != nil {
			return db.SavingsSource{}, err
		}
		personID = pm.BudgetPersonID
	}

	src, err := s.profiles.AddSavingsSource(ctx, db.AddSavingsSourceParams{
		BudgetProfileID: profileID,
		BudgetPersonID:  personID,
		Name:            inp.Name,
		Amount:          inp.Amount,
		Frequency:       paymentDaysFrequency(n),
		PaymentMethodID: inp.PaymentMethodID,
		PaymentDays:     inp.PaymentDays,
	})
	if err != nil {
		return db.SavingsSource{}, err
	}

	s.createSavingsTransactions(ctx, profileID, userID, src)
	return src, nil
}

func (s *BudgetProfileService) createSavingsTransactions(ctx context.Context, profileID, userID uuid.UUID, src db.SavingsSource) {
	period, err := s.profiles.GetLatestPeriod(ctx, profileID)
	if err != nil {
		return
	}
	cats, err := s.transactions.ListCategories(ctx, userID)
	if err != nil {
		return
	}
	var savingsCatID *int32
	for _, c := range cats {
		if c.Name == "Savings" && c.IsSystem {
			id := c.ID
			savingsCatID = &id
			break
		}
	}
	if savingsCatID == nil {
		return
	}

	txTypeID := int32(1) // Fixed
	freqIDByFreq := map[string]int32{"monthly": 4, "bi_weekly": 3, "weekly": 2}
	txFreqID := freqIDByFreq[src.Frequency]

	// Split amount evenly across payment days.
	perDayAmount := src.Amount
	n := len(src.PaymentDays)
	if n > 1 && src.Amount.Int != nil {
		perDayAmount = pgtype.Numeric{
			Int:   new(big.Int).Quo(src.Amount.Int, big.NewInt(int64(n))),
			Exp:   src.Amount.Exp,
			Valid: src.Amount.Valid,
		}
	}

	startTime := period.StartDate.Time
	lastDay := time.Date(startTime.Year(), startTime.Month()+1, 0, 0, 0, 0, 0, time.UTC).Day()
	for _, day := range src.PaymentDays {
		d := int(day)
		if d > lastDay {
			d = lastDay
		}
		txDate := pgtype.Date{
			Time:  time.Date(startTime.Year(), startTime.Month(), d, 0, 0, 0, 0, time.UTC),
			Valid: true,
		}
		s.transactions.Create(ctx, db.CreateTransactionParams{ //nolint:errcheck
			Name:                   &src.Name,
			Amount:                 perDayAmount,
			PlannedAmount:          perDayAmount,
			Date:                   txDate,
			RenewalDate:            pgtype.Date{},
			Recurring:              func() *bool { v := true; return &v }(),
			BudgetPeriodID:         &period.ID,
			CategoryID:             savingsCatID,
			PaymentMethodID:        src.PaymentMethodID,
			TransactionFrequencyID: &txFreqID,
			TransactionTypeID:      &txTypeID,
		})
	}
}

func (s *BudgetProfileService) ListSavingsSources(ctx context.Context, profileID, userID uuid.UUID) ([]db.SavingsSource, error) {
	if _, err := s.assertMember(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.profiles.ListSavingsSources(ctx, profileID)
}

func (s *BudgetProfileService) UpdateSavingsSource(ctx context.Context, id int32, profileID, userID uuid.UUID, inp SavingsSourceInput) (db.SavingsSource, error) {
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return db.SavingsSource{}, err
	}
	n := len(inp.PaymentDays)
	if n != 0 && n != 1 && n != 2 && n != 4 {
		return db.SavingsSource{}, apperr.Invalid("payment_days must have 1, 2, or 4 entries")
	}

	old, err := s.profiles.GetSavingsSource(ctx, db.GetSavingsSourceParams{ID: id, BudgetProfileID: profileID})
	if err != nil {
		return db.SavingsSource{}, err
	}

	var personID *int32
	if inp.PaymentMethodID != nil {
		pm, err := s.transactions.GetPaymentMethod(ctx, *inp.PaymentMethodID)
		if err != nil {
			return db.SavingsSource{}, err
		}
		personID = pm.BudgetPersonID
	}

	freq := paymentDaysFrequency(n)
	if n == 0 {
		freq = "" // preserve existing if no days supplied
	}

	updated, err := s.profiles.UpdateSavingsSource(ctx, db.UpdateSavingsSourceParams{
		ID:              id,
		BudgetProfileID: profileID,
		Name:            inp.Name,
		Amount:          inp.Amount,
		Frequency:       freq,
		BudgetPersonID:  personID,
		PaymentMethodID: inp.PaymentMethodID,
		PaymentDays:     inp.PaymentDays,
	})
	if err != nil {
		return db.SavingsSource{}, err
	}

	// Delete old auto-created transactions, then recreate with updated values.
	if old.PaymentMethodID != nil {
		cats, err := s.transactions.ListCategories(ctx, userID)
		if err == nil {
			for _, c := range cats {
				if c.IsSystem && c.Name == "Savings" {
					catID := c.ID
					_ = s.transactions.DeleteSavingsSourceTransactions(ctx, db.DeleteSavingsSourceTransactionsParams{
						BudgetProfileID: profileID,
						Name:            &old.Name,
						PaymentMethodID: *old.PaymentMethodID,
						CategoryID:      &catID,
					})
					break
				}
			}
		}
	}
	if updated.PaymentMethodID != nil && len(updated.PaymentDays) > 0 {
		s.createSavingsTransactions(ctx, profileID, userID, updated)
	}

	return updated, nil
}

func (s *BudgetProfileService) DeleteSavingsSource(ctx context.Context, id int32, profileID, userID uuid.UUID) error {
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return err
	}
	src, err := s.profiles.GetSavingsSource(ctx, db.GetSavingsSourceParams{ID: id, BudgetProfileID: profileID})
	if err != nil {
		return err
	}
	if src.PaymentMethodID != nil {
		cats, err := s.transactions.ListCategories(ctx, userID)
		if err != nil {
			return err
		}
		var savingsCatID *int32
		for _, c := range cats {
			if c.IsSystem && c.Name == "Savings" {
				id32 := c.ID
				savingsCatID = &id32
				break
			}
		}
		if savingsCatID != nil {
			_ = s.transactions.DeleteSavingsSourceTransactions(ctx, db.DeleteSavingsSourceTransactionsParams{
				BudgetProfileID: profileID,
				Name:            &src.Name,
				PaymentMethodID: *src.PaymentMethodID,
				CategoryID:      savingsCatID,
			})
		}
	}
	return s.profiles.DeleteSavingsSource(ctx, db.DeleteSavingsSourceParams{
		ID:              id,
		BudgetProfileID: profileID,
	})
}

// ── Fixed Expenses ────────────────────────────────────────────────────────────

type FixedExpenseInput struct {
	Name            string
	PlannedAmount   pgtype.Numeric
	CategoryID      *int32
	PaymentMethodID *uuid.UUID
	DayOfMonth      int32
	IntervalMonths  int32
	AnchorDate      *time.Time // explicit anchor override; overrides DayOfMonth (day is derived from it) when set
	FrequencyUnit   int16      // 0/1 = MONTH (default), 2 = WEEK
	IntervalWeeks   int32      // applies when FrequencyUnit = WEEK
	DayOfWeek       int32      // 1 = Monday ... 7 = Sunday; applies when FrequencyUnit = WEEK
	EndDate         *time.Time // optional payment plan end date; auto-deactivates template after this date
	TotalPayments   int32      // informational: total payments in the plan; 0 = unset
}

// isoWeekday converts a date to ISO 8601 weekday numbering (1=Monday..7=Sunday).
func isoWeekday(t time.Time) int {
	d := int(t.Weekday()) // time.Weekday: Sunday=0..Saturday=6
	if d == 0 {
		return 7
	}
	return d
}

func (s *BudgetProfileService) CreateFixedExpense(ctx context.Context, profileID, userID uuid.UUID, inp FixedExpenseInput) (db.FixedExpense, *db.Transaction, error) {
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return db.FixedExpense{}, nil, err
	}
	unit := inp.FrequencyUnit
	if unit != frequencyUnitWeek {
		unit = frequencyUnitMonth
	}
	day := inp.DayOfMonth
	if day < 1 {
		day = 1
	}
	dayOfWeek := inp.DayOfWeek
	if dayOfWeek < 1 || dayOfWeek > 7 {
		dayOfWeek = 1
	}
	anchorDate := pgtype.Date{}
	if inp.AnchorDate != nil {
		day = int32(inp.AnchorDate.Day())
		dayOfWeek = int32(isoWeekday(*inp.AnchorDate))
		anchorDate = pgtype.Date{Time: *inp.AnchorDate, Valid: true}
	}
	interval := inp.IntervalMonths
	if interval < 1 {
		interval = 1
	}
	intervalWeeks := inp.IntervalWeeks
	if intervalWeeks < 1 {
		intervalWeeks = 1
	}
	endDate := pgtype.Date{}
	if inp.EndDate != nil {
		endDate = pgtype.Date{Time: *inp.EndDate, Valid: true}
	}
	var totalPayments *int32
	if inp.TotalPayments > 0 {
		totalPayments = &inp.TotalPayments
	}
	fe, err := s.fixedExpenses.Create(ctx, db.CreateFixedExpenseParams{
		BudgetProfileID: profileID,
		Name:            inp.Name,
		PlannedAmount:   inp.PlannedAmount,
		CategoryID:      inp.CategoryID,
		PaymentMethodID: inp.PaymentMethodID,
		DayOfMonth:      day,
		IntervalMonths:  interval,
		AnchorDate:      anchorDate,
		FrequencyUnit:   unit,
		IntervalWeeks:   intervalWeeks,
		DayOfWeek:       int16(dayOfWeek),
		EndDate:         endDate,
		TotalPayments:   totalPayments,
	})
	if err != nil {
		return db.FixedExpense{}, nil, err
	}

	// Spawn transaction(s) in the active period, unless an explicit anchor
	// date makes it not due yet (e.g. a future-dated subscription start).
	period, err := s.profiles.GetLatestPeriod(ctx, profileID)
	if err != nil {
		return fe, nil, nil // no active period — still return the expense
	}
	startDate := period.StartDate.Time

	if unit == frequencyUnitWeek {
		// A week-unit expense can have several due occurrences inside the
		// current period (e.g. "every week"); spawnWeeklyFixedExpenseOccurrences
		// already skips any non-due week internally (naturally handles a
		// future anchor too, same as the month-unit due-check below), so no
		// separate gate is needed here. There's no single transaction to
		// return in the response — the caller relies on re-fetching
		// transactions afterward, same as createNextPeriod's spawn path.
		s.spawnWeeklyFixedExpenseOccurrences(ctx, fe, period.ID, startDate, period.EndDate.Time)
		return fe, nil, nil
	}

	if inp.AnchorDate != nil && !isFixedExpenseDueInMonth(fe, startDate) {
		return fe, nil, nil // future-dated; no transaction until due
	}
	lastDay := time.Date(startDate.Year(), startDate.Month()+1, 0, 0, 0, 0, 0, time.UTC).Day()
	d := int(day)
	if d > lastDay {
		d = lastDay
	}
	txDate := pgtype.Date{
		Time:  time.Date(startDate.Year(), startDate.Month(), d, 0, 0, 0, 0, time.UTC),
		Valid: true,
	}
	txTypeFixed := int32(1)
	feID := fe.ID
	name := fe.Name
	tx, txErr := s.transactions.Create(ctx, db.CreateTransactionParams{
		Name:              &name,
		Amount:            fe.PlannedAmount,
		PlannedAmount:     fe.PlannedAmount,
		Date:              txDate,
		BudgetPeriodID:    &period.ID,
		CategoryID:        fe.CategoryID,
		PaymentMethodID:   fe.PaymentMethodID,
		TransactionTypeID: &txTypeFixed,
		FixedExpenseID:    &feID,
	})
	if txErr != nil {
		return fe, nil, nil
	}
	return fe, &tx, nil
}

func (s *BudgetProfileService) ListFixedExpenses(ctx context.Context, profileID, userID uuid.UUID) ([]db.FixedExpense, error) {
	if _, err := s.assertMember(ctx, profileID, userID); err != nil {
		return nil, err
	}
	return s.fixedExpenses.List(ctx, profileID)
}

func (s *BudgetProfileService) UpdateFixedExpense(ctx context.Context, id uuid.UUID, profileID, userID uuid.UUID, inp FixedExpenseInput) (db.FixedExpense, error) {
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return db.FixedExpense{}, err
	}
	unit := inp.FrequencyUnit
	if unit != frequencyUnitWeek {
		unit = frequencyUnitMonth
	}
	day := inp.DayOfMonth
	if day < 1 {
		day = 1
	}
	dayOfWeek := inp.DayOfWeek
	if dayOfWeek < 1 || dayOfWeek > 7 {
		dayOfWeek = 1
	}
	anchorDate := pgtype.Date{}
	if inp.AnchorDate != nil {
		day = int32(inp.AnchorDate.Day())
		dayOfWeek = int32(isoWeekday(*inp.AnchorDate))
		anchorDate = pgtype.Date{Time: *inp.AnchorDate, Valid: true}
	}
	interval := inp.IntervalMonths
	if interval < 1 {
		interval = 1
	}
	intervalWeeks := inp.IntervalWeeks
	if intervalWeeks < 1 {
		intervalWeeks = 1
	}
	endDate := pgtype.Date{}
	if inp.EndDate != nil {
		endDate = pgtype.Date{Time: *inp.EndDate, Valid: true}
	}
	var totalPayments *int32
	if inp.TotalPayments > 0 {
		totalPayments = &inp.TotalPayments
	}
	fe, err := s.fixedExpenses.Update(ctx, db.UpdateFixedExpenseParams{
		ID:              id,
		BudgetProfileID: profileID,
		Name:            inp.Name,
		PlannedAmount:   inp.PlannedAmount,
		CategoryID:      inp.CategoryID,
		PaymentMethodID: inp.PaymentMethodID,
		DayOfMonth:      day,
		IntervalMonths:  interval,
		AnchorDate:      anchorDate,
		FrequencyUnit:   unit,
		IntervalWeeks:   intervalWeeks,
		DayOfWeek:       int16(dayOfWeek),
		EndDate:         endDate,
		TotalPayments:   totalPayments,
	})
	if err != nil {
		return db.FixedExpense{}, err
	}

	// WEEK unit can have several transactions per period (not just one), so
	// the "reconcile the current period's single unpaid transaction" model
	// below doesn't apply — editing a WEEK-unit template only affects future
	// spawns, it does not retroactively touch already-spawned transactions.
	if unit == frequencyUnitWeek {
		return fe, nil
	}

	// Reconcile the unpaid transaction in the active period: propagate field
	// changes if fe is still due this period, or remove it if the update
	// (e.g. a rescheduled anchor) made it no longer due.
	period, err := s.profiles.GetLatestPeriod(ctx, profileID)
	if err != nil {
		return fe, nil
	}
	startDate := period.StartDate.Time
	if !isFixedExpenseDueInMonth(fe, startDate) {
		_ = s.fixedExpenses.DeleteUnpaidTransactions(ctx, db.DeleteUnpaidTransactionByFixedExpenseParams{
			FixedExpenseID:  fe.ID,
			BudgetProfileID: profileID,
		})
		return fe, nil
	}
	lastDay := time.Date(startDate.Year(), startDate.Month()+1, 0, 0, 0, 0, 0, time.UTC).Day()
	d := int(day)
	if d > lastDay {
		d = lastDay
	}
	txDate := pgtype.Date{
		Time:  time.Date(startDate.Year(), startDate.Month(), d, 0, 0, 0, 0, time.UTC),
		Valid: true,
	}
	name := fe.Name
	_, getErr := s.fixedExpenses.GetUnpaidTransaction(ctx, db.GetUnpaidTransactionByFixedExpenseParams{
		FixedExpenseID:  fe.ID,
		BudgetProfileID: profileID,
	})
	if getErr != nil {
		// No existing unpaid transaction — expense just became due (e.g. anchor
		// date was removed or moved to past). Spawn one for the current period.
		txTypeFixed := int32(1)
		feID := fe.ID
		_, _ = s.transactions.Create(ctx, db.CreateTransactionParams{
			Name:              &name,
			Amount:            fe.PlannedAmount,
			PlannedAmount:     fe.PlannedAmount,
			Date:              txDate,
			BudgetPeriodID:    &period.ID,
			CategoryID:        fe.CategoryID,
			PaymentMethodID:   fe.PaymentMethodID,
			TransactionTypeID: &txTypeFixed,
			FixedExpenseID:    &feID,
		})
	} else {
		_ = s.fixedExpenses.UpdateTransactionFromFixedExpense(ctx, db.UpdateTransactionFromFixedExpenseParams{
			FixedExpenseID:  fe.ID,
			BudgetProfileID: profileID,
			Name:            &name,
			PlannedAmount:   fe.PlannedAmount,
			CategoryID:      fe.CategoryID,
			PaymentMethodID: fe.PaymentMethodID,
			Date:            txDate,
		})
	}

	return fe, nil
}

func (s *BudgetProfileService) DeleteFixedExpense(ctx context.Context, id uuid.UUID, profileID, userID uuid.UUID) error {
	if _, err := s.assertCollaboratorOrAbove(ctx, profileID, userID); err != nil {
		return err
	}
	// Verify ownership: the expense must belong to this profile.
	fe, err := s.fixedExpenses.GetByID(ctx, id)
	if err != nil {
		return err
	}
	if fe.BudgetProfileID != profileID {
		return apperr.Forbidden("access denied")
	}

	// Remove unpaid transaction from the active period.
	_ = s.fixedExpenses.DeleteUnpaidTransactions(ctx, db.DeleteUnpaidTransactionByFixedExpenseParams{
		FixedExpenseID:  id,
		BudgetProfileID: profileID,
	})

	return s.fixedExpenses.Deactivate(ctx, db.DeactivateFixedExpenseParams{
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
