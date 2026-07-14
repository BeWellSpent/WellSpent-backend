package repository

import (
	"context"
	"errors"
	"fmt"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type BudgetProfileRepository interface {
	// Profile
	ListByUserID(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error)
	ListByUserOrMember(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.BudgetProfile, error)
	ExistsByNameAndUser(ctx context.Context, name string, userID uuid.UUID) (bool, error)
	Create(ctx context.Context, arg db.CreateBudgetProfileParams) (db.BudgetProfile, error)
	Update(ctx context.Context, arg db.UpdateBudgetProfileParams) (db.BudgetProfile, error)
	Delete(ctx context.Context, id uuid.UUID) error

	// Period
	CreatePeriod(ctx context.Context, arg db.CreateBudgetPeriodParams) (db.BudgetPeriod, error)
	GetPeriodByID(ctx context.Context, id uuid.UUID) (db.BudgetPeriod, error)
	GetPeriodByDate(ctx context.Context, profileID uuid.UUID, date pgtype.Date) (db.BudgetPeriod, error)
	ListPeriods(ctx context.Context, profileID uuid.UUID) ([]db.BudgetPeriod, error)
	GetLatestPeriod(ctx context.Context, profileID uuid.UUID) (db.BudgetPeriod, error)
	ArchivePeriod(ctx context.Context, id uuid.UUID) error
	ListProfileIDsWithExpiredPeriod(ctx context.Context, date pgtype.Date) ([]uuid.UUID, error)

	// People
	ListPeople(ctx context.Context, profileID uuid.UUID) ([]db.BudgetToProfileMapping, error)
	GetPerson(ctx context.Context, personID int32, profileID uuid.UUID) (db.BudgetToProfileMapping, error)
	GetPersonByUserID(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetToProfileMapping, error)
	ExistsPerson(ctx context.Context, profileID uuid.UUID, userName string) (bool, error)
	ExistsPersonForUser(ctx context.Context, profileID, userID uuid.UUID) (bool, error)
	AddPerson(ctx context.Context, arg db.AddBudgetPersonToProfileParams) (db.BudgetToProfileMapping, error)
	UpdatePerson(ctx context.Context, arg db.UpdateBudgetPersonParams) (db.BudgetToProfileMapping, error)
	UpdatePersonRole(ctx context.Context, arg db.UpdateBudgetPersonRoleParams) (db.BudgetToProfileMapping, error)
	LinkPersonToUser(ctx context.Context, arg db.LinkBudgetPersonToUserParams) (db.BudgetToProfileMapping, error)
	SoftRemovePerson(ctx context.Context, arg db.SoftRemovePersonFromProfileParams) error
	SoftRemovePersonAndReassign(ctx context.Context, arg db.SoftRemovePersonAndReassignFromProfileParams) error

	// Income Sources
	ListIncomeSources(ctx context.Context, profileID uuid.UUID) ([]db.IncomeSource, error)
	AddIncomeSource(ctx context.Context, arg db.AddIncomeSourceParams) (db.IncomeSource, error)
	UpdateIncomeSource(ctx context.Context, arg db.UpdateIncomeSourceParams) (db.IncomeSource, error)
	DeleteIncomeSource(ctx context.Context, arg db.DeleteIncomeSourceParams) error

	// Income Entries
	ListIncomeEntries(ctx context.Context, periodID uuid.UUID) ([]db.IncomeEntry, error)
	CreateIncomeEntry(ctx context.Context, arg db.CreateIncomeEntryParams) (db.IncomeEntry, error)
	UpdateIncomeEntry(ctx context.Context, arg db.UpdateIncomeEntryParams) (db.IncomeEntry, error)

	// Savings Sources
	GetSavingsSource(ctx context.Context, arg db.GetSavingsSourceParams) (db.SavingsSource, error)
	AddSavingsSource(ctx context.Context, arg db.AddSavingsSourceParams) (db.SavingsSource, error)
	ListSavingsSources(ctx context.Context, profileID uuid.UUID) ([]db.SavingsSource, error)
	UpdateSavingsSource(ctx context.Context, arg db.UpdateSavingsSourceParams) (db.SavingsSource, error)
	DeleteSavingsSource(ctx context.Context, arg db.DeleteSavingsSourceParams) error
	UpsertTaxReserveSavingsSource(ctx context.Context, arg db.UpsertTaxReserveSavingsSourceParams) (db.SavingsSource, error)
	DeleteTaxReserveSavingsSource(ctx context.Context, profileID uuid.UUID) error
}

type budgetProfileRepository struct {
	q *db.Queries
}

func NewBudgetProfileRepository(q *db.Queries) BudgetProfileRepository {
	return &budgetProfileRepository{q: q}
}

// ── Profile ───────────────────────────────────────────────────────────────────

func (r *budgetProfileRepository) ListByUserID(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error) {
	return r.q.ListBudgetProfilesByUser(ctx, userID)
}

func (r *budgetProfileRepository) ListByUserOrMember(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error) {
	return r.q.ListBudgetProfilesByUserOrMember(ctx, &userID)
}

func (r *budgetProfileRepository) GetByID(ctx context.Context, id uuid.UUID) (db.BudgetProfile, error) {
	p, err := r.q.GetBudgetProfileByID(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetProfile{}, apperr.NotFound("budget_profile", id.String())
	}
	return p, err
}

func (r *budgetProfileRepository) ExistsByNameAndUser(ctx context.Context, name string, userID uuid.UUID) (bool, error) {
	return r.q.ExistsBudgetProfileByNameAndUser(ctx, db.ExistsBudgetProfileByNameAndUserParams{Name: name, UserID: userID})
}

func (r *budgetProfileRepository) Create(ctx context.Context, arg db.CreateBudgetProfileParams) (db.BudgetProfile, error) {
	return r.q.CreateBudgetProfile(ctx, arg)
}

func (r *budgetProfileRepository) Update(ctx context.Context, arg db.UpdateBudgetProfileParams) (db.BudgetProfile, error) {
	p, err := r.q.UpdateBudgetProfile(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetProfile{}, apperr.NotFound("budget_profile", arg.ID.String())
	}
	return p, err
}

func (r *budgetProfileRepository) Delete(ctx context.Context, id uuid.UUID) error {
	return r.q.DeleteBudgetProfile(ctx, id)
}

// ── Period ────────────────────────────────────────────────────────────────────

func (r *budgetProfileRepository) CreatePeriod(ctx context.Context, arg db.CreateBudgetPeriodParams) (db.BudgetPeriod, error) {
	return r.q.CreateBudgetPeriod(ctx, arg)
}

func (r *budgetProfileRepository) GetPeriodByID(ctx context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
	p, err := r.q.GetBudgetPeriodByID(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetPeriod{}, apperr.NotFound("budget_period", id.String())
	}
	return p, err
}

func (r *budgetProfileRepository) GetPeriodByDate(ctx context.Context, profileID uuid.UUID, date pgtype.Date) (db.BudgetPeriod, error) {
	p, err := r.q.GetBudgetPeriodByDate(ctx, db.GetBudgetPeriodByDateParams{
		BudgetProfileID: profileID,
		Column2:         date,
	})
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetPeriod{}, apperr.NotFound("budget_period", "date")
	}
	return p, err
}

func (r *budgetProfileRepository) ListPeriods(ctx context.Context, profileID uuid.UUID) ([]db.BudgetPeriod, error) {
	return r.q.ListBudgetPeriods(ctx, profileID)
}

func (r *budgetProfileRepository) GetLatestPeriod(ctx context.Context, profileID uuid.UUID) (db.BudgetPeriod, error) {
	p, err := r.q.GetLatestBudgetPeriod(ctx, profileID)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetPeriod{}, apperr.NotFound("budget_period", "latest for "+profileID.String())
	}
	return p, err
}

func (r *budgetProfileRepository) ArchivePeriod(ctx context.Context, id uuid.UUID) error {
	return r.q.ArchiveBudgetPeriod(ctx, id)
}

func (r *budgetProfileRepository) ListProfileIDsWithExpiredPeriod(ctx context.Context, date pgtype.Date) ([]uuid.UUID, error) {
	return r.q.ListProfileIDsWithLatestPeriodEndingOn(ctx, date)
}

// ── People ────────────────────────────────────────────────────────────────────

func (r *budgetProfileRepository) ListPeople(ctx context.Context, profileID uuid.UUID) ([]db.BudgetToProfileMapping, error) {
	return r.q.ListBudgetPeopleByProfile(ctx, profileID)
}

func (r *budgetProfileRepository) GetPerson(ctx context.Context, personID int32, profileID uuid.UUID) (db.BudgetToProfileMapping, error) {
	m, err := r.q.GetBudgetPersonByProfileID(ctx, db.GetBudgetPersonByProfileIDParams{ID: personID, BudgetProfileID: profileID})
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetToProfileMapping{}, apperr.NotFound("budget_person", fmt.Sprintf("%d", personID))
	}
	return m, err
}

func (r *budgetProfileRepository) ExistsPerson(ctx context.Context, profileID uuid.UUID, userName string) (bool, error) {
	return r.q.ExistsBudgetPersonInProfile(ctx, db.ExistsBudgetPersonInProfileParams{
		Column1:  profileID,
		UserName: &userName,
	})
}

func (r *budgetProfileRepository) ExistsPersonForUser(ctx context.Context, profileID, userID uuid.UUID) (bool, error) {
	return r.q.ExistsBudgetPersonForUser(ctx, db.ExistsBudgetPersonForUserParams{
		Column1: profileID,
		Column2: userID,
	})
}

func (r *budgetProfileRepository) GetPersonByUserID(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetToProfileMapping, error) {
	m, err := r.q.GetBudgetPersonByUserID(ctx, db.GetBudgetPersonByUserIDParams{
		BudgetProfileID: profileID,
		UserID:          &userID,
	})
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetToProfileMapping{}, apperr.NotFound("budget_person", userID.String())
	}
	return m, err
}

func (r *budgetProfileRepository) AddPerson(ctx context.Context, arg db.AddBudgetPersonToProfileParams) (db.BudgetToProfileMapping, error) {
	return r.q.AddBudgetPersonToProfile(ctx, arg)
}

func (r *budgetProfileRepository) UpdatePerson(ctx context.Context, arg db.UpdateBudgetPersonParams) (db.BudgetToProfileMapping, error) {
	m, err := r.q.UpdateBudgetPerson(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetToProfileMapping{}, apperr.NotFound("budget_person", fmt.Sprintf("%d", arg.ID))
	}
	return m, err
}

func (r *budgetProfileRepository) UpdatePersonRole(ctx context.Context, arg db.UpdateBudgetPersonRoleParams) (db.BudgetToProfileMapping, error) {
	m, err := r.q.UpdateBudgetPersonRole(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetToProfileMapping{}, apperr.NotFound("budget_person", fmt.Sprintf("%d", arg.ID))
	}
	return m, err
}

func (r *budgetProfileRepository) LinkPersonToUser(ctx context.Context, arg db.LinkBudgetPersonToUserParams) (db.BudgetToProfileMapping, error) {
	m, err := r.q.LinkBudgetPersonToUser(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetToProfileMapping{}, apperr.NotFound("budget_person", fmt.Sprintf("%d", arg.ID))
	}
	return m, err
}

func (r *budgetProfileRepository) SoftRemovePerson(ctx context.Context, arg db.SoftRemovePersonFromProfileParams) error {
	return r.q.SoftRemovePersonFromProfile(ctx, arg)
}

func (r *budgetProfileRepository) SoftRemovePersonAndReassign(ctx context.Context, arg db.SoftRemovePersonAndReassignFromProfileParams) error {
	return r.q.SoftRemovePersonAndReassignFromProfile(ctx, arg)
}

// ── Income Sources ────────────────────────────────────────────────────────────

func (r *budgetProfileRepository) ListIncomeSources(ctx context.Context, profileID uuid.UUID) ([]db.IncomeSource, error) {
	return r.q.ListIncomeSources(ctx, profileID)
}

func (r *budgetProfileRepository) AddIncomeSource(ctx context.Context, arg db.AddIncomeSourceParams) (db.IncomeSource, error) {
	return r.q.AddIncomeSource(ctx, arg)
}

func (r *budgetProfileRepository) UpdateIncomeSource(ctx context.Context, arg db.UpdateIncomeSourceParams) (db.IncomeSource, error) {
	s, err := r.q.UpdateIncomeSource(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.IncomeSource{}, apperr.NotFound("income_source", fmt.Sprintf("%d", arg.ID))
	}
	return s, err
}

func (r *budgetProfileRepository) DeleteIncomeSource(ctx context.Context, arg db.DeleteIncomeSourceParams) error {
	return r.q.DeleteIncomeSource(ctx, arg)
}

// ── Income Entries ────────────────────────────────────────────────────────────

func (r *budgetProfileRepository) ListIncomeEntries(ctx context.Context, periodID uuid.UUID) ([]db.IncomeEntry, error) {
	return r.q.ListIncomeEntries(ctx, periodID)
}

func (r *budgetProfileRepository) CreateIncomeEntry(ctx context.Context, arg db.CreateIncomeEntryParams) (db.IncomeEntry, error) {
	return r.q.CreateIncomeEntry(ctx, arg)
}

func (r *budgetProfileRepository) UpdateIncomeEntry(ctx context.Context, arg db.UpdateIncomeEntryParams) (db.IncomeEntry, error) {
	e, err := r.q.UpdateIncomeEntry(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.IncomeEntry{}, apperr.NotFound("income_entry", fmt.Sprintf("%d", arg.ID))
	}
	return e, err
}

// ── Savings Sources ───────────────────────────────────────────────────────────

func (r *budgetProfileRepository) GetSavingsSource(ctx context.Context, arg db.GetSavingsSourceParams) (db.SavingsSource, error) {
	s, err := r.q.GetSavingsSource(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.SavingsSource{}, apperr.NotFound("savings_source", fmt.Sprintf("%d", arg.ID))
	}
	return s, err
}

func (r *budgetProfileRepository) AddSavingsSource(ctx context.Context, arg db.AddSavingsSourceParams) (db.SavingsSource, error) {
	return r.q.AddSavingsSource(ctx, arg)
}

func (r *budgetProfileRepository) ListSavingsSources(ctx context.Context, profileID uuid.UUID) ([]db.SavingsSource, error) {
	return r.q.ListSavingsSources(ctx, profileID)
}

func (r *budgetProfileRepository) UpdateSavingsSource(ctx context.Context, arg db.UpdateSavingsSourceParams) (db.SavingsSource, error) {
	s, err := r.q.UpdateSavingsSource(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.SavingsSource{}, apperr.NotFound("savings_source", fmt.Sprintf("%d", arg.ID))
	}
	return s, err
}

func (r *budgetProfileRepository) DeleteSavingsSource(ctx context.Context, arg db.DeleteSavingsSourceParams) error {
	return r.q.DeleteSavingsSource(ctx, arg)
}

func (r *budgetProfileRepository) UpsertTaxReserveSavingsSource(ctx context.Context, arg db.UpsertTaxReserveSavingsSourceParams) (db.SavingsSource, error) {
	return r.q.UpsertTaxReserveSavingsSource(ctx, arg)
}

func (r *budgetProfileRepository) DeleteTaxReserveSavingsSource(ctx context.Context, profileID uuid.UUID) error {
	return r.q.DeleteTaxReserveSavingsSource(ctx, profileID)
}
