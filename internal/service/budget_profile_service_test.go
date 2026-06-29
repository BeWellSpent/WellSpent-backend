package service

import (
	"context"
	"errors"
	"math/big"
	"testing"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func bigInt(n int64) *big.Int { return big.NewInt(n) }

// ── Mock BudgetProfileRepository ─────────────────────────────────────────────

type mockBudgetProfileRepo struct {
	listByUserID                 func(context.Context, uuid.UUID) ([]db.BudgetProfile, error)
	getByID                      func(context.Context, uuid.UUID) (db.BudgetProfile, error)
	existsByNameAndUser          func(context.Context, string, uuid.UUID) (bool, error)
	create                       func(context.Context, db.CreateBudgetProfileParams) (db.BudgetProfile, error)
	update                       func(context.Context, db.UpdateBudgetProfileParams) (db.BudgetProfile, error)
	delete                       func(context.Context, uuid.UUID) error
	createPeriod                 func(context.Context, db.CreateBudgetPeriodParams) (db.BudgetPeriod, error)
	getPeriodByID                func(context.Context, uuid.UUID) (db.BudgetPeriod, error)
	listPeriods                  func(context.Context, uuid.UUID) ([]db.BudgetPeriod, error)
	getLatestPeriod              func(context.Context, uuid.UUID) (db.BudgetPeriod, error)
	listProfileIDsWithExpired    func(context.Context, pgtype.Date) ([]uuid.UUID, error)
	listPeople                   func(context.Context, uuid.UUID) ([]db.BudgetToProfileMapping, error)
	getPerson                    func(context.Context, int32, uuid.UUID) (db.BudgetToProfileMapping, error)
	existsPerson                 func(context.Context, uuid.UUID, string) (bool, error)
	addPerson                    func(context.Context, db.AddBudgetPersonToProfileParams) (db.BudgetToProfileMapping, error)
	updatePerson                 func(context.Context, db.UpdateBudgetPersonParams) (db.BudgetToProfileMapping, error)
	softRemovePerson             func(context.Context, db.SoftRemovePersonFromProfileParams) error
	softRemovePersonAndReassign  func(context.Context, db.SoftRemovePersonAndReassignFromProfileParams) error
	listIncomeSources            func(context.Context, uuid.UUID) ([]db.IncomeSource, error)
	addIncomeSource              func(context.Context, db.AddIncomeSourceParams) (db.IncomeSource, error)
	updateIncomeSource           func(context.Context, db.UpdateIncomeSourceParams) (db.IncomeSource, error)
	deleteIncomeSource           func(context.Context, db.DeleteIncomeSourceParams) error
	listIncomeEntries            func(context.Context, uuid.UUID) ([]db.IncomeEntry, error)
	createIncomeEntry            func(context.Context, db.CreateIncomeEntryParams) (db.IncomeEntry, error)
	updateIncomeEntry            func(context.Context, db.UpdateIncomeEntryParams) (db.IncomeEntry, error)
	addSavingsSource                   func(context.Context, db.AddSavingsSourceParams) (db.SavingsSource, error)
	listSavingsSources                 func(context.Context, uuid.UUID) ([]db.SavingsSource, error)
	updateSavingsSource                func(context.Context, db.UpdateSavingsSourceParams) (db.SavingsSource, error)
	deleteSavingsSource                func(context.Context, db.DeleteSavingsSourceParams) error
	upsertTaxReserveSavingsSource      func(context.Context, db.UpsertTaxReserveSavingsSourceParams) (db.SavingsSource, error)
	deleteTaxReserveSavingsSource      func(context.Context, uuid.UUID) error
}

func (m *mockBudgetProfileRepo) ListByUserID(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error) {
	if m.listByUserID != nil {
		return m.listByUserID(ctx, userID)
	}
	return nil, nil
}
func (m *mockBudgetProfileRepo) GetByID(ctx context.Context, id uuid.UUID) (db.BudgetProfile, error) {
	if m.getByID != nil {
		return m.getByID(ctx, id)
	}
	return db.BudgetProfile{}, apperr.NotFound("budget_profile", id.String())
}
func (m *mockBudgetProfileRepo) ExistsByNameAndUser(ctx context.Context, name string, userID uuid.UUID) (bool, error) {
	if m.existsByNameAndUser != nil {
		return m.existsByNameAndUser(ctx, name, userID)
	}
	return false, nil
}
func (m *mockBudgetProfileRepo) Create(ctx context.Context, arg db.CreateBudgetProfileParams) (db.BudgetProfile, error) {
	if m.create != nil {
		return m.create(ctx, arg)
	}
	return db.BudgetProfile{ID: uuid.New(), UserID: arg.UserID, Name: arg.Name, Cycle: arg.Cycle}, nil
}
func (m *mockBudgetProfileRepo) Update(ctx context.Context, arg db.UpdateBudgetProfileParams) (db.BudgetProfile, error) {
	if m.update != nil {
		return m.update(ctx, arg)
	}
	return db.BudgetProfile{ID: arg.ID, Name: arg.Name, Cycle: arg.Cycle}, nil
}
func (m *mockBudgetProfileRepo) Delete(ctx context.Context, id uuid.UUID) error {
	if m.delete != nil {
		return m.delete(ctx, id)
	}
	return nil
}
func (m *mockBudgetProfileRepo) CreatePeriod(ctx context.Context, arg db.CreateBudgetPeriodParams) (db.BudgetPeriod, error) {
	if m.createPeriod != nil {
		return m.createPeriod(ctx, arg)
	}
	return db.BudgetPeriod{ID: uuid.New(), BudgetProfileID: arg.BudgetProfileID, StartDate: arg.StartDate, EndDate: arg.EndDate}, nil
}
func (m *mockBudgetProfileRepo) GetPeriodByID(ctx context.Context, id uuid.UUID) (db.BudgetPeriod, error) {
	if m.getPeriodByID != nil {
		return m.getPeriodByID(ctx, id)
	}
	return db.BudgetPeriod{}, apperr.NotFound("budget_period", id.String())
}
func (m *mockBudgetProfileRepo) ListPeriods(ctx context.Context, profileID uuid.UUID) ([]db.BudgetPeriod, error) {
	if m.listPeriods != nil {
		return m.listPeriods(ctx, profileID)
	}
	return nil, nil
}
func (m *mockBudgetProfileRepo) GetLatestPeriod(ctx context.Context, profileID uuid.UUID) (db.BudgetPeriod, error) {
	if m.getLatestPeriod != nil {
		return m.getLatestPeriod(ctx, profileID)
	}
	return db.BudgetPeriod{}, apperr.NotFound("budget_period", "latest")
}
func (m *mockBudgetProfileRepo) ListProfileIDsWithExpiredPeriod(ctx context.Context, date pgtype.Date) ([]uuid.UUID, error) {
	if m.listProfileIDsWithExpired != nil {
		return m.listProfileIDsWithExpired(ctx, date)
	}
	return nil, nil
}
func (m *mockBudgetProfileRepo) ListPeople(ctx context.Context, profileID uuid.UUID) ([]db.BudgetToProfileMapping, error) {
	if m.listPeople != nil {
		return m.listPeople(ctx, profileID)
	}
	return nil, nil
}
func (m *mockBudgetProfileRepo) GetPerson(ctx context.Context, personID int32, profileID uuid.UUID) (db.BudgetToProfileMapping, error) {
	if m.getPerson != nil {
		return m.getPerson(ctx, personID, profileID)
	}
	return db.BudgetToProfileMapping{ID: personID, BudgetProfileID: profileID, IsActive: true}, nil
}
func (m *mockBudgetProfileRepo) ExistsPerson(ctx context.Context, profileID uuid.UUID, userName string) (bool, error) {
	if m.existsPerson != nil {
		return m.existsPerson(ctx, profileID, userName)
	}
	return false, nil
}
func (m *mockBudgetProfileRepo) AddPerson(ctx context.Context, arg db.AddBudgetPersonToProfileParams) (db.BudgetToProfileMapping, error) {
	if m.addPerson != nil {
		return m.addPerson(ctx, arg)
	}
	return db.BudgetToProfileMapping{BudgetProfileID: arg.BudgetProfileID, UserName: arg.UserName, UserID: arg.UserID}, nil
}
func (m *mockBudgetProfileRepo) UpdatePerson(ctx context.Context, arg db.UpdateBudgetPersonParams) (db.BudgetToProfileMapping, error) {
	if m.updatePerson != nil {
		return m.updatePerson(ctx, arg)
	}
	return db.BudgetToProfileMapping{Color: arg.Color}, nil
}
func (m *mockBudgetProfileRepo) SoftRemovePerson(ctx context.Context, arg db.SoftRemovePersonFromProfileParams) error {
	if m.softRemovePerson != nil {
		return m.softRemovePerson(ctx, arg)
	}
	return nil
}
func (m *mockBudgetProfileRepo) SoftRemovePersonAndReassign(ctx context.Context, arg db.SoftRemovePersonAndReassignFromProfileParams) error {
	if m.softRemovePersonAndReassign != nil {
		return m.softRemovePersonAndReassign(ctx, arg)
	}
	return nil
}
func (m *mockBudgetProfileRepo) ListIncomeSources(ctx context.Context, profileID uuid.UUID) ([]db.IncomeSource, error) {
	if m.listIncomeSources != nil {
		return m.listIncomeSources(ctx, profileID)
	}
	return nil, nil
}
func (m *mockBudgetProfileRepo) AddIncomeSource(ctx context.Context, arg db.AddIncomeSourceParams) (db.IncomeSource, error) {
	if m.addIncomeSource != nil {
		return m.addIncomeSource(ctx, arg)
	}
	return db.IncomeSource{
		BudgetProfileID: arg.BudgetProfileID,
		Name:            arg.Name,
		IncomeType:      arg.IncomeType,
		DefaultAmount:   arg.DefaultAmount,
		Recurring:       arg.Recurring,
		BudgetPersonID:  arg.BudgetPersonID,
	}, nil
}
func (m *mockBudgetProfileRepo) UpdateIncomeSource(ctx context.Context, arg db.UpdateIncomeSourceParams) (db.IncomeSource, error) {
	if m.updateIncomeSource != nil {
		return m.updateIncomeSource(ctx, arg)
	}
	return db.IncomeSource{ID: arg.ID, BudgetProfileID: arg.BudgetProfileID, Name: arg.Name}, nil
}
func (m *mockBudgetProfileRepo) DeleteIncomeSource(ctx context.Context, arg db.DeleteIncomeSourceParams) error {
	if m.deleteIncomeSource != nil {
		return m.deleteIncomeSource(ctx, arg)
	}
	return nil
}
func (m *mockBudgetProfileRepo) ListIncomeEntries(ctx context.Context, periodID uuid.UUID) ([]db.IncomeEntry, error) {
	if m.listIncomeEntries != nil {
		return m.listIncomeEntries(ctx, periodID)
	}
	return nil, nil
}
func (m *mockBudgetProfileRepo) CreateIncomeEntry(ctx context.Context, arg db.CreateIncomeEntryParams) (db.IncomeEntry, error) {
	if m.createIncomeEntry != nil {
		return m.createIncomeEntry(ctx, arg)
	}
	return db.IncomeEntry{BudgetPeriodID: arg.BudgetPeriodID, Amount: arg.Amount}, nil
}
func (m *mockBudgetProfileRepo) UpdateIncomeEntry(ctx context.Context, arg db.UpdateIncomeEntryParams) (db.IncomeEntry, error) {
	if m.updateIncomeEntry != nil {
		return m.updateIncomeEntry(ctx, arg)
	}
	return db.IncomeEntry{ID: arg.ID, BudgetPeriodID: arg.BudgetPeriodID, Amount: arg.Amount}, nil
}

func (m *mockBudgetProfileRepo) AddSavingsSource(ctx context.Context, arg db.AddSavingsSourceParams) (db.SavingsSource, error) {
	if m.addSavingsSource != nil {
		return m.addSavingsSource(ctx, arg)
	}
	return db.SavingsSource{BudgetProfileID: arg.BudgetProfileID, Name: arg.Name, Frequency: arg.Frequency}, nil
}
func (m *mockBudgetProfileRepo) ListSavingsSources(ctx context.Context, profileID uuid.UUID) ([]db.SavingsSource, error) {
	if m.listSavingsSources != nil {
		return m.listSavingsSources(ctx, profileID)
	}
	return nil, nil
}
func (m *mockBudgetProfileRepo) UpdateSavingsSource(ctx context.Context, arg db.UpdateSavingsSourceParams) (db.SavingsSource, error) {
	if m.updateSavingsSource != nil {
		return m.updateSavingsSource(ctx, arg)
	}
	return db.SavingsSource{ID: arg.ID, BudgetProfileID: arg.BudgetProfileID, Name: arg.Name}, nil
}
func (m *mockBudgetProfileRepo) DeleteSavingsSource(ctx context.Context, arg db.DeleteSavingsSourceParams) error {
	if m.deleteSavingsSource != nil {
		return m.deleteSavingsSource(ctx, arg)
	}
	return nil
}

func (m *mockBudgetProfileRepo) UpsertTaxReserveSavingsSource(ctx context.Context, arg db.UpsertTaxReserveSavingsSourceParams) (db.SavingsSource, error) {
	if m.upsertTaxReserveSavingsSource != nil {
		return m.upsertTaxReserveSavingsSource(ctx, arg)
	}
	return db.SavingsSource{BudgetProfileID: arg.BudgetProfileID, IsTaxReserve: true}, nil
}

func (m *mockBudgetProfileRepo) DeleteTaxReserveSavingsSource(ctx context.Context, profileID uuid.UUID) error {
	if m.deleteTaxReserveSavingsSource != nil {
		return m.deleteTaxReserveSavingsSource(ctx, profileID)
	}
	return nil
}

// ── BudgetProfileService tests ────────────────────────────────────────────────

func TestCreateBudgetProfile_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()

	profileRepo := &mockBudgetProfileRepo{
		create: func(_ context.Context, arg db.CreateBudgetProfileParams) (db.BudgetProfile, error) {
			assert.Equal(t, "Home Budget", arg.Name)
			assert.Equal(t, "monthly", arg.Cycle)
			assert.Equal(t, userID, arg.UserID)
			return db.BudgetProfile{ID: profileID, UserID: userID, Name: arg.Name, Cycle: arg.Cycle}, nil
		},
	}

	mockTx := &mockTransactionRepo{}
	mockUser := &mockUserRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.User, error) {
			return db.User{}, apperr.NotFound("user", id.String())
		},
	}

	svc := NewBudgetProfileService(profileRepo, mockTx, mockUser)
	profile, _, err := svc.Create(context.Background(), userID, "Home Budget", "monthly")

	require.NoError(t, err)
	assert.Equal(t, "Home Budget", profile.Name)
	assert.Equal(t, userID, profile.UserID)
}

func TestCreateBudgetProfile_Duplicate(t *testing.T) {
	userID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			existsByNameAndUser: func(_ context.Context, _ string, _ uuid.UUID) (bool, error) {
				return true, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	_, _, err := svc.Create(context.Background(), userID, "Existing Budget", "monthly")
	require.Error(t, err)
	var dup *apperr.DuplicateError
	require.ErrorAs(t, err, &dup)
}

func TestGetBudgetProfile_Forbidden_WhenNotOwner(t *testing.T) {
	profileID := uuid.New()
	ownerID := uuid.New()
	otherUserID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	_, err := svc.Get(context.Background(), profileID, otherUserID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestRemovePerson_Forbidden_WhenRemovingOwner(t *testing.T) {
	ownerID := uuid.New()
	profileID := uuid.New()
	personID := int32(1)

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
			},
			getPerson: func(_ context.Context, _ int32, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
				return db.BudgetToProfileMapping{ID: personID, UserID: &ownerID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	err := svc.RemovePerson(context.Background(), profileID, personID, 0, uuid.Nil, ownerID)
	require.Error(t, err)
	var invalid *apperr.ValidationError
	require.ErrorAs(t, err, &invalid)
}

func TestAddIncomeSource_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			addIncomeSource: func(_ context.Context, arg db.AddIncomeSourceParams) (db.IncomeSource, error) {
				assert.Equal(t, "Salary", arg.Name)
				assert.Equal(t, "salary", arg.IncomeType)
				assert.True(t, arg.Recurring)
				return db.IncomeSource{ID: 1, BudgetProfileID: profileID, Name: "Salary"}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	src, err := svc.AddIncomeSource(context.Background(), profileID, userID, IncomeSourceInput{
		Name:       "Salary",
		IncomeType: "salary",
		Recurring:  true,
	})
	require.NoError(t, err)
	assert.Equal(t, "Salary", src.Name)
}

// ── Savings source tests ──────────────────────────────────────────────────────

func TestAddSavingsSource_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	personID := int32(1)

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			addSavingsSource: func(_ context.Context, arg db.AddSavingsSourceParams) (db.SavingsSource, error) {
				assert.Equal(t, "Emergency Fund", arg.Name)
				assert.Equal(t, "bi_weekly", arg.Frequency)
				require.NotNil(t, arg.BudgetPersonID)
				assert.Equal(t, personID, *arg.BudgetPersonID)
				return db.SavingsSource{ID: 1, BudgetProfileID: profileID, Name: arg.Name, Frequency: arg.Frequency}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	src, err := svc.AddSavingsSource(context.Background(), profileID, userID, SavingsSourceInput{
		Name:           "Emergency Fund",
		Frequency:      "bi_weekly",
		BudgetPersonID: &personID,
	})
	require.NoError(t, err)
	assert.Equal(t, "Emergency Fund", src.Name)
	assert.Equal(t, "bi_weekly", src.Frequency)
}

func TestAddSavingsSource_Forbidden(t *testing.T) {
	ownerID := uuid.New()
	otherID := uuid.New()
	profileID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	_, err := svc.AddSavingsSource(context.Background(), profileID, otherID, SavingsSourceInput{Name: "Fund"})
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.True(t, errors.As(err, &forbidden))
}

func TestListSavingsSources_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			listSavingsSources: func(_ context.Context, _ uuid.UUID) ([]db.SavingsSource, error) {
				return []db.SavingsSource{
					{ID: 1, Name: "Emergency Fund", Frequency: "bi_weekly"},
					{ID: 2, Name: "Vacation", Frequency: "monthly"},
				}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	sources, err := svc.ListSavingsSources(context.Background(), profileID, userID)
	require.NoError(t, err)
	assert.Len(t, sources, 2)
	assert.Equal(t, "Emergency Fund", sources[0].Name)
}

func TestUpdateSavingsSource_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			updateSavingsSource: func(_ context.Context, arg db.UpdateSavingsSourceParams) (db.SavingsSource, error) {
				assert.Equal(t, int32(1), arg.ID)
				assert.Equal(t, "Renamed Fund", arg.Name)
				return db.SavingsSource{ID: arg.ID, Name: arg.Name}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	src, err := svc.UpdateSavingsSource(context.Background(), 1, profileID, userID, SavingsSourceInput{
		Name:      "Renamed Fund",
		Frequency: "monthly",
	})
	require.NoError(t, err)
	assert.Equal(t, "Renamed Fund", src.Name)
}

func TestDeleteSavingsSource_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	deleted := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			deleteSavingsSource: func(_ context.Context, arg db.DeleteSavingsSourceParams) error {
				assert.Equal(t, int32(5), arg.ID)
				deleted = true
				return nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	err := svc.DeleteSavingsSource(context.Background(), 5, profileID, userID)
	require.NoError(t, err)
	assert.True(t, deleted)
}

func TestDeleteSavingsSource_Forbidden(t *testing.T) {
	ownerID := uuid.New()
	otherID := uuid.New()
	profileID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	err := svc.DeleteSavingsSource(context.Background(), 1, profileID, otherID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.True(t, errors.As(err, &forbidden))
}

func TestUpdatePerson_Success(t *testing.T) {
	ownerID := uuid.New()
	profileID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	m, err := svc.UpdatePerson(context.Background(), profileID, 1, "green", ownerID)
	require.NoError(t, err)
	assert.Equal(t, "green", m.Color)
}

func TestUpdatePerson_Forbidden(t *testing.T) {
	ownerID := uuid.New()
	otherID := uuid.New()
	profileID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{},
	)

	_, err := svc.UpdatePerson(context.Background(), profileID, 1, "blue", otherID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.True(t, errors.As(err, &forbidden))
}

// mockUserRepo is defined in auth_service_test.go (same package).

func TestCreateBudgetPeriod_TaxReserveUpserted_ForUSProfile(t *testing.T) {
	ownerID := uuid.New()
	profileID := uuid.New()
	us := "US"
	ca := "CA"
	upserted := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID, CountryCode: &us}, nil
			},
			listIncomeSources: func(_ context.Context, _ uuid.UUID) ([]db.IncomeSource, error) {
				return []db.IncomeSource{
					{
						ID: 1, Name: "Salary", Recurring: true, BeforeTax: true,
						DefaultAmount: pgtype.Numeric{Int: bigInt(80000), Exp: 0, Valid: true},
					},
				}, nil
			},
			upsertTaxReserveSavingsSource: func(_ context.Context, arg db.UpsertTaxReserveSavingsSourceParams) (db.SavingsSource, error) {
				assert.Equal(t, profileID, arg.BudgetProfileID)
				assert.True(t, arg.Amount.Valid)
				upserted = true
				return db.SavingsSource{IsTaxReserve: true}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.User, error) {
				if id == ownerID {
					return db.User{ID: ownerID, StateCode: &ca, FilingStatus: "1"}, nil
				}
				return db.User{}, apperr.NotFound("user", id.String())
			},
		},
	)

	_, err := svc.CreateBudgetPeriod(context.Background(), profileID, ownerID)
	require.NoError(t, err)
	assert.True(t, upserted, "expected tax reserve savings source to be upserted")
}

func TestAddPeople_CountryMismatch_Rejected(t *testing.T) {
	ownerID := uuid.New()
	foreignUserID := uuid.New()
	profileID := uuid.New()
	us := "US"
	ar := "AR"

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID, CountryCode: &us}, nil
			},
		},
		&mockTransactionRepo{},
		&mockUserRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.User, error) {
				if id == foreignUserID {
					return db.User{ID: foreignUserID, CountryCode: &ar}, nil
				}
				return db.User{}, apperr.NotFound("user", id.String())
			},
		},
	)

	_, err := svc.AddPeople(context.Background(), profileID, ownerID, []ProfilePersonInput{
		{UserName: "Carlos", UserID: &foreignUserID, Color: "red"},
	})
	require.Error(t, err)
	var inv *apperr.ValidationError
	assert.True(t, errors.As(err, &inv), "expected ValidationError for country mismatch")
}
