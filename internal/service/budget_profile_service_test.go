package service

import (
	"context"
	"errors"
	"math/big"
	"testing"
	"time"

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
	listByUserID                  func(context.Context, uuid.UUID) ([]db.BudgetProfile, error)
	listByUserOrMember            func(context.Context, uuid.UUID) ([]db.BudgetProfile, error)
	getByID                       func(context.Context, uuid.UUID) (db.BudgetProfile, error)
	existsByNameAndUser           func(context.Context, string, uuid.UUID) (bool, error)
	create                        func(context.Context, db.CreateBudgetProfileParams) (db.BudgetProfile, error)
	update                        func(context.Context, db.UpdateBudgetProfileParams) (db.BudgetProfile, error)
	delete                        func(context.Context, uuid.UUID) error
	createPeriod                  func(context.Context, db.CreateBudgetPeriodParams) (db.BudgetPeriod, error)
	getPeriodByID                 func(context.Context, uuid.UUID) (db.BudgetPeriod, error)
	listPeriods                   func(context.Context, uuid.UUID) ([]db.BudgetPeriod, error)
	getLatestPeriod               func(context.Context, uuid.UUID) (db.BudgetPeriod, error)
	listProfileIDsWithExpired     func(context.Context, pgtype.Date) ([]uuid.UUID, error)
	listPeople                    func(context.Context, uuid.UUID) ([]db.BudgetToProfileMapping, error)
	getPerson                     func(context.Context, int32, uuid.UUID) (db.BudgetToProfileMapping, error)
	getPersonByUserID             func(context.Context, uuid.UUID, uuid.UUID) (db.BudgetToProfileMapping, error)
	existsPerson                  func(context.Context, uuid.UUID, string) (bool, error)
	existsPersonForUser           func(context.Context, uuid.UUID, uuid.UUID) (bool, error)
	addPerson                     func(context.Context, db.AddBudgetPersonToProfileParams) (db.BudgetToProfileMapping, error)
	updatePerson                  func(context.Context, db.UpdateBudgetPersonParams) (db.BudgetToProfileMapping, error)
	updatePersonRole              func(context.Context, db.UpdateBudgetPersonRoleParams) (db.BudgetToProfileMapping, error)
	linkPersonToUser              func(context.Context, db.LinkBudgetPersonToUserParams) (db.BudgetToProfileMapping, error)
	softRemovePerson              func(context.Context, db.SoftRemovePersonFromProfileParams) error
	softRemovePersonAndReassign   func(context.Context, db.SoftRemovePersonAndReassignFromProfileParams) error
	listIncomeSources             func(context.Context, uuid.UUID) ([]db.IncomeSource, error)
	addIncomeSource               func(context.Context, db.AddIncomeSourceParams) (db.IncomeSource, error)
	updateIncomeSource            func(context.Context, db.UpdateIncomeSourceParams) (db.IncomeSource, error)
	deleteIncomeSource            func(context.Context, db.DeleteIncomeSourceParams) error
	listIncomeEntries             func(context.Context, uuid.UUID) ([]db.IncomeEntry, error)
	createIncomeEntry             func(context.Context, db.CreateIncomeEntryParams) (db.IncomeEntry, error)
	updateIncomeEntry             func(context.Context, db.UpdateIncomeEntryParams) (db.IncomeEntry, error)
	getSavingsSource              func(context.Context, db.GetSavingsSourceParams) (db.SavingsSource, error)
	addSavingsSource              func(context.Context, db.AddSavingsSourceParams) (db.SavingsSource, error)
	listSavingsSources            func(context.Context, uuid.UUID) ([]db.SavingsSource, error)
	updateSavingsSource           func(context.Context, db.UpdateSavingsSourceParams) (db.SavingsSource, error)
	deleteSavingsSource           func(context.Context, db.DeleteSavingsSourceParams) error
	upsertTaxReserveSavingsSource func(context.Context, db.UpsertTaxReserveSavingsSourceParams) (db.SavingsSource, error)
	deleteTaxReserveSavingsSource func(context.Context, uuid.UUID) error
}

func (m *mockBudgetProfileRepo) ListByUserID(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error) {
	if m.listByUserID != nil {
		return m.listByUserID(ctx, userID)
	}
	return nil, nil
}
func (m *mockBudgetProfileRepo) ListByUserOrMember(ctx context.Context, userID uuid.UUID) ([]db.BudgetProfile, error) {
	if m.listByUserOrMember != nil {
		return m.listByUserOrMember(ctx, userID)
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
func (m *mockBudgetProfileRepo) ExistsPersonForUser(ctx context.Context, profileID, userID uuid.UUID) (bool, error) {
	if m.existsPersonForUser != nil {
		return m.existsPersonForUser(ctx, profileID, userID)
	}
	return false, nil
}
func (m *mockBudgetProfileRepo) GetPersonByUserID(ctx context.Context, profileID, userID uuid.UUID) (db.BudgetToProfileMapping, error) {
	if m.getPersonByUserID != nil {
		return m.getPersonByUserID(ctx, profileID, userID)
	}
	return db.BudgetToProfileMapping{}, apperr.NotFound("budget_person", userID.String())
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
func (m *mockBudgetProfileRepo) UpdatePersonRole(ctx context.Context, arg db.UpdateBudgetPersonRoleParams) (db.BudgetToProfileMapping, error) {
	if m.updatePersonRole != nil {
		return m.updatePersonRole(ctx, arg)
	}
	return db.BudgetToProfileMapping{ID: arg.ID, Role: arg.Role}, nil
}
func (m *mockBudgetProfileRepo) LinkPersonToUser(ctx context.Context, arg db.LinkBudgetPersonToUserParams) (db.BudgetToProfileMapping, error) {
	if m.linkPersonToUser != nil {
		return m.linkPersonToUser(ctx, arg)
	}
	return db.BudgetToProfileMapping{ID: arg.ID, UserID: &arg.UserID, Role: arg.Role}, nil
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

func (m *mockBudgetProfileRepo) GetSavingsSource(ctx context.Context, arg db.GetSavingsSourceParams) (db.SavingsSource, error) {
	if m.getSavingsSource != nil {
		return m.getSavingsSource(ctx, arg)
	}
	return db.SavingsSource{}, nil
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

	svc := NewBudgetProfileService(profileRepo, mockTx, &mockFixedExpenseRepo{}, mockUser)
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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
	pmID := uuid.New()

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
				assert.Equal(t, []int32{1, 15}, arg.PaymentDays)
				return db.SavingsSource{ID: 1, BudgetProfileID: profileID, Name: arg.Name, Frequency: arg.Frequency}, nil
			},
		},
		&mockTransactionRepo{
			getPaymentMethod: func(_ context.Context, id uuid.UUID) (db.PaymentMethod, error) {
				assert.Equal(t, pmID, id)
				return db.PaymentMethod{ID: id, BudgetPersonID: &personID}, nil
			},
		},
		&mockFixedExpenseRepo{},
		&mockUserRepo{},
	)

	src, err := svc.AddSavingsSource(context.Background(), profileID, userID, SavingsSourceInput{
		Name:            "Emergency Fund",
		PaymentMethodID: &pmID,
		PaymentDays:     []int32{1, 15},
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
		&mockUserRepo{},
	)

	src, err := svc.UpdateSavingsSource(context.Background(), 1, profileID, userID, SavingsSourceInput{
		Name:        "Renamed Fund",
		PaymentDays: []int32{15},
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
			getSavingsSource: func(_ context.Context, arg db.GetSavingsSourceParams) (db.SavingsSource, error) {
				return db.SavingsSource{ID: arg.ID, BudgetProfileID: profileID, Name: "Fund"}, nil
			},
			deleteSavingsSource: func(_ context.Context, arg db.DeleteSavingsSourceParams) error {
				assert.Equal(t, int32(5), arg.ID)
				deleted = true
				return nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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
		&mockFixedExpenseRepo{},
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

// ── Fixed expense tests ───────────────────────────────────────────────────────

func TestCreateFixedExpense_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			create: func(_ context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
				assert.Equal(t, "Rent", arg.Name)
				assert.Equal(t, int32(1), arg.DayOfMonth)
				assert.Equal(t, int32(1), arg.IntervalMonths, "unset interval defaults to 1 (monthly)")
				return db.FixedExpense{ID: feID, BudgetProfileID: profileID, Name: arg.Name, DayOfMonth: arg.DayOfMonth, IntervalMonths: arg.IntervalMonths}, nil
			},
		},
		&mockUserRepo{},
	)

	fe, tx, err := svc.CreateFixedExpense(context.Background(), profileID, userID, FixedExpenseInput{
		Name:       "Rent",
		DayOfMonth: 1,
	})
	require.NoError(t, err)
	assert.Equal(t, "Rent", fe.Name)
	assert.Equal(t, int32(1), fe.IntervalMonths)
	assert.Nil(t, tx) // no active period in mock
}

func TestCreateFixedExpense_QuarterlyIntervalPassesThrough(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			create: func(_ context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
				assert.Equal(t, int32(3), arg.IntervalMonths)
				return db.FixedExpense{ID: feID, BudgetProfileID: profileID, Name: arg.Name, IntervalMonths: arg.IntervalMonths}, nil
			},
		},
		&mockUserRepo{},
	)

	fe, _, err := svc.CreateFixedExpense(context.Background(), profileID, userID, FixedExpenseInput{
		Name:           "Car insurance",
		DayOfMonth:     15,
		IntervalMonths: 3,
	})
	require.NoError(t, err)
	assert.Equal(t, int32(3), fe.IntervalMonths)
}

func TestCreateFixedExpense_FutureAnchorDate_SkipsImmediateSpawn(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	now := time.Now().UTC()
	currentMonthStart := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
	futureAnchor := currentMonthStart.AddDate(0, 2, 0).AddDate(0, 0, 9) // ~2 months out
	created := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			getLatestPeriod: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: uuid.New(), StartDate: pgtype.Date{Time: currentMonthStart, Valid: true}}, nil
			},
		},
		&mockTransactionRepo{
			create: func(_ context.Context, _ db.CreateTransactionParams) (db.Transaction, error) {
				created = true
				return db.Transaction{}, nil
			},
		},
		&mockFixedExpenseRepo{
			create: func(_ context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
				assert.True(t, arg.AnchorDate.Valid)
				assert.Equal(t, int32(futureAnchor.Day()), arg.DayOfMonth, "day-of-month is derived from anchor_date")
				return db.FixedExpense{
					ID:              feID,
					BudgetProfileID: profileID,
					Name:            arg.Name,
					DayOfMonth:      arg.DayOfMonth,
					IntervalMonths:  arg.IntervalMonths,
					AnchorDate:      arg.AnchorDate,
				}, nil
			},
		},
		&mockUserRepo{},
	)

	fe, tx, err := svc.CreateFixedExpense(context.Background(), profileID, userID, FixedExpenseInput{
		Name:       "Biennial subscription",
		DayOfMonth: 1, // ignored — anchor date supplies the day
		AnchorDate: &futureAnchor,
	})
	require.NoError(t, err)
	assert.Nil(t, tx, "future-dated anchor should not spawn a transaction yet")
	assert.False(t, created)
	assert.True(t, fe.AnchorDate.Valid)
}

func TestCreateFixedExpense_DueAnchorDate_SpawnsImmediately(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	now := time.Now().UTC()
	currentMonthStart := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
	anchor := currentMonthStart.AddDate(0, 0, 19) // day 20 of the current month
	var createdArg db.CreateTransactionParams
	created := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			getLatestPeriod: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: uuid.New(), StartDate: pgtype.Date{Time: currentMonthStart, Valid: true}}, nil
			},
		},
		&mockTransactionRepo{
			create: func(_ context.Context, arg db.CreateTransactionParams) (db.Transaction, error) {
				created = true
				createdArg = arg
				return db.Transaction{}, nil
			},
		},
		&mockFixedExpenseRepo{
			create: func(_ context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
				return db.FixedExpense{
					ID:              feID,
					BudgetProfileID: profileID,
					Name:            arg.Name,
					DayOfMonth:      arg.DayOfMonth,
					IntervalMonths:  arg.IntervalMonths,
					AnchorDate:      arg.AnchorDate,
				}, nil
			},
		},
		&mockUserRepo{},
	)

	_, tx, err := svc.CreateFixedExpense(context.Background(), profileID, userID, FixedExpenseInput{
		Name:       "Just-in-time subscription",
		AnchorDate: &anchor,
	})
	require.NoError(t, err)
	require.NotNil(t, tx, "anchor due this month should spawn immediately")
	assert.True(t, created)
	assert.Equal(t, anchor.Day(), createdArg.Date.Time.Day())
}

func TestCreateFixedExpense_Forbidden(t *testing.T) {
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
		&mockFixedExpenseRepo{},
		&mockUserRepo{},
	)

	_, _, err := svc.CreateFixedExpense(context.Background(), profileID, otherID, FixedExpenseInput{Name: "Rent"})
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.True(t, errors.As(err, &forbidden))
}

func TestListFixedExpenses_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{
					{Name: "Rent"},
					{Name: "Internet"},
				}, nil
			},
		},
		&mockUserRepo{},
	)

	items, err := svc.ListFixedExpenses(context.Background(), profileID, userID)
	require.NoError(t, err)
	assert.Len(t, items, 2)
}

func TestUpdateFixedExpense_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			update: func(_ context.Context, arg db.UpdateFixedExpenseParams) (db.FixedExpense, error) {
				assert.Equal(t, feID, arg.ID)
				assert.Equal(t, "New Rent", arg.Name)
				assert.Equal(t, int32(6), arg.IntervalMonths)
				return db.FixedExpense{ID: feID, Name: arg.Name, IntervalMonths: arg.IntervalMonths}, nil
			},
		},
		&mockUserRepo{},
	)

	fe, err := svc.UpdateFixedExpense(context.Background(), feID, profileID, userID, FixedExpenseInput{
		Name:           "New Rent",
		DayOfMonth:     1,
		IntervalMonths: 6,
	})
	require.NoError(t, err)
	assert.Equal(t, "New Rent", fe.Name)
	assert.Equal(t, int32(6), fe.IntervalMonths)
}

func TestUpdateFixedExpense_RescheduledToFuture_DeletesUnpaidTransaction(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	now := time.Now().UTC()
	currentMonthStart := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
	futureAnchor := currentMonthStart.AddDate(0, 3, 0).AddDate(0, 0, 4) // ~3 months out
	deleted := false
	propagated := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			getLatestPeriod: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: uuid.New(), StartDate: pgtype.Date{Time: currentMonthStart, Valid: true}}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			update: func(_ context.Context, arg db.UpdateFixedExpenseParams) (db.FixedExpense, error) {
				return db.FixedExpense{
					ID:              feID,
					BudgetProfileID: profileID,
					Name:            arg.Name,
					DayOfMonth:      arg.DayOfMonth,
					IntervalMonths:  arg.IntervalMonths,
					AnchorDate:      arg.AnchorDate,
				}, nil
			},
			deleteUnpaidTransactions: func(_ context.Context, arg db.DeleteUnpaidTransactionByFixedExpenseParams) error {
				deleted = true
				assert.Equal(t, feID, arg.FixedExpenseID)
				return nil
			},
			updateTransactionFromFixed: func(_ context.Context, _ db.UpdateTransactionFromFixedExpenseParams) error {
				propagated = true
				return nil
			},
		},
		&mockUserRepo{},
	)

	fe, err := svc.UpdateFixedExpense(context.Background(), feID, profileID, userID, FixedExpenseInput{
		Name:       "Rescheduled subscription",
		AnchorDate: &futureAnchor,
	})
	require.NoError(t, err)
	assert.True(t, deleted, "no-longer-due expense should have its unpaid transaction removed")
	assert.False(t, propagated, "should not propagate date changes to a transaction it just deleted")
	assert.True(t, fe.AnchorDate.Valid)
}

func TestUpdateFixedExpense_StillDue_PropagatesToUnpaidTransaction(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	now := time.Now().UTC()
	currentMonthStart := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
	deleted := false
	propagated := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			getLatestPeriod: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: uuid.New(), StartDate: pgtype.Date{Time: currentMonthStart, Valid: true}}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			update: func(_ context.Context, arg db.UpdateFixedExpenseParams) (db.FixedExpense, error) {
				// No AnchorDate given — falls back to (zero-value) CreatedAt with the
				// default monthly interval, which is always due.
				return db.FixedExpense{ID: feID, Name: arg.Name, DayOfMonth: arg.DayOfMonth, IntervalMonths: arg.IntervalMonths}, nil
			},
			deleteUnpaidTransactions: func(_ context.Context, _ db.DeleteUnpaidTransactionByFixedExpenseParams) error {
				deleted = true
				return nil
			},
			updateTransactionFromFixed: func(_ context.Context, _ db.UpdateTransactionFromFixedExpenseParams) error {
				propagated = true
				return nil
			},
		},
		&mockUserRepo{},
	)

	_, err := svc.UpdateFixedExpense(context.Background(), feID, profileID, userID, FixedExpenseInput{
		Name:       "Still due",
		DayOfMonth: 5,
	})
	require.NoError(t, err)
	assert.True(t, propagated, "still-due expense should have its unpaid transaction updated in place")
	assert.False(t, deleted)
}

func TestDeleteFixedExpense_Success(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	deactivated := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.FixedExpense, error) {
				return db.FixedExpense{ID: id, BudgetProfileID: profileID}, nil
			},
			deactivate: func(_ context.Context, arg db.DeactivateFixedExpenseParams) error {
				assert.Equal(t, feID, arg.ID)
				deactivated = true
				return nil
			},
		},
		&mockUserRepo{},
	)

	err := svc.DeleteFixedExpense(context.Background(), feID, profileID, userID)
	require.NoError(t, err)
	assert.True(t, deactivated)
}

func TestDeleteFixedExpense_Forbidden_WrongProfile(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	otherProfileID := uuid.New()
	feID := uuid.New()

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			getByID: func(_ context.Context, id uuid.UUID) (db.FixedExpense, error) {
				return db.FixedExpense{ID: id, BudgetProfileID: otherProfileID}, nil
			},
		},
		&mockUserRepo{},
	)

	err := svc.DeleteFixedExpense(context.Background(), feID, profileID, userID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.True(t, errors.As(err, &forbidden))
}

// ── Fixed expense interval / due-date tests ────────────────────────────────────

func TestIsFixedExpenseDueInMonth(t *testing.T) {
	anchor := time.Date(2026, time.January, 15, 0, 0, 0, 0, time.UTC) // created mid-January

	tests := []struct {
		name     string
		interval int32
		month    time.Time
		want     bool
	}{
		{"monthly, same month as anchor", 1, time.Date(2026, time.January, 1, 0, 0, 0, 0, time.UTC), true},
		{"monthly, every month is due", 1, time.Date(2026, time.June, 1, 0, 0, 0, 0, time.UTC), true},
		{"unset interval treated as monthly", 0, time.Date(2026, time.March, 1, 0, 0, 0, 0, time.UTC), true},
		{"quarterly, anchor month is due", 3, time.Date(2026, time.January, 1, 0, 0, 0, 0, time.UTC), true},
		{"quarterly, one month after anchor is not due", 3, time.Date(2026, time.February, 1, 0, 0, 0, 0, time.UTC), false},
		{"quarterly, two months after anchor is not due", 3, time.Date(2026, time.March, 1, 0, 0, 0, 0, time.UTC), false},
		{"quarterly, three months after anchor is due", 3, time.Date(2026, time.April, 1, 0, 0, 0, 0, time.UTC), true},
		{"quarterly, six months after anchor is due", 3, time.Date(2026, time.July, 1, 0, 0, 0, 0, time.UTC), true},
		{"yearly, same month next year is due", 12, time.Date(2027, time.January, 1, 0, 0, 0, 0, time.UTC), true},
		{"yearly, six months later is not due", 12, time.Date(2026, time.July, 1, 0, 0, 0, 0, time.UTC), false},
		{"month before anchor is never due", 1, time.Date(2025, time.December, 1, 0, 0, 0, 0, time.UTC), false},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			fe := db.FixedExpense{
				CreatedAt:      pgtype.Timestamptz{Time: anchor, Valid: true},
				IntervalMonths: tt.interval,
			}
			assert.Equal(t, tt.want, isFixedExpenseDueInMonth(fe, tt.month))
		})
	}
}

func TestIsFixedExpenseDueInMonth_AnchorDateOverridesCreatedAt(t *testing.T) {
	createdAt := time.Date(2020, time.January, 1, 0, 0, 0, 0, time.UTC) // long past, would always be "due" on its own
	futureAnchor := time.Date(2027, time.March, 10, 0, 0, 0, 0, time.UTC)
	fe := db.FixedExpense{
		CreatedAt:      pgtype.Timestamptz{Time: createdAt, Valid: true},
		AnchorDate:     pgtype.Date{Time: futureAnchor, Valid: true},
		IntervalMonths: 1,
	}

	assert.False(t, isFixedExpenseDueInMonth(fe, time.Date(2026, time.July, 1, 0, 0, 0, 0, time.UTC)), "not due before the anchor's month")
	assert.True(t, isFixedExpenseDueInMonth(fe, time.Date(2027, time.March, 1, 0, 0, 0, 0, time.UTC)), "due in the anchor's own month")
	assert.True(t, isFixedExpenseDueInMonth(fe, time.Date(2027, time.April, 1, 0, 0, 0, 0, time.UTC)), "monthly cadence stays due after the anchor")
}

func TestFixedExpenseNextDueDate(t *testing.T) {
	anchor := time.Date(2026, time.January, 15, 0, 0, 0, 0, time.UTC)
	fe := db.FixedExpense{
		CreatedAt:      pgtype.Timestamptz{Time: anchor, Valid: true},
		IntervalMonths: 3,
		DayOfMonth:     15,
	}

	// Asking from a not-due month finds the next due month ahead.
	next := FixedExpenseNextDueDate(fe, time.Date(2026, time.February, 1, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, time.Date(2026, time.April, 15, 0, 0, 0, 0, time.UTC), next)

	// Asking from a due month returns that same month's date.
	next = FixedExpenseNextDueDate(fe, time.Date(2026, time.July, 1, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, time.Date(2026, time.July, 15, 0, 0, 0, 0, time.UTC), next)
}

func TestFixedExpenseNextDueDate_ClampsToLastDayOfMonth(t *testing.T) {
	anchor := time.Date(2026, time.January, 31, 0, 0, 0, 0, time.UTC)
	fe := db.FixedExpense{
		CreatedAt:      pgtype.Timestamptz{Time: anchor, Valid: true},
		IntervalMonths: 1,
		DayOfMonth:     31,
	}
	next := FixedExpenseNextDueDate(fe, time.Date(2026, time.February, 1, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, time.Date(2026, time.February, 28, 0, 0, 0, 0, time.UTC), next, "Feb 2026 has 28 days")
}

func TestFixedExpenseNextDueDate_WithFutureAnchorDate(t *testing.T) {
	futureAnchor := time.Date(2027, time.March, 10, 0, 0, 0, 0, time.UTC)
	fe := db.FixedExpense{
		CreatedAt:      pgtype.Timestamptz{Time: time.Date(2026, time.July, 10, 0, 0, 0, 0, time.UTC), Valid: true},
		AnchorDate:     pgtype.Date{Time: futureAnchor, Valid: true},
		IntervalMonths: 1,
		DayOfMonth:     10,
	}
	next := FixedExpenseNextDueDate(fe, time.Date(2026, time.July, 1, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, futureAnchor, next, "next due date should be the future anchor, not created_at's month")
}

// ── createNextPeriod fixed-expense spawn tests ─────────────────────────────────

func TestCreateBudgetPeriod_FixedExpense_SkipsWhenNotDue(t *testing.T) {
	ownerID := uuid.New()
	profileID := uuid.New()
	now := time.Now().UTC()
	currentMonthStart := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
	// Created 1 month ago on a quarterly cadence — not due this month.
	createdAt := currentMonthStart.AddDate(0, -1, 0)
	created := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID, Cycle: "monthly"}, nil
			},
		},
		&mockTransactionRepo{
			create: func(_ context.Context, _ db.CreateTransactionParams) (db.Transaction, error) {
				created = true
				return db.Transaction{}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{{
					ID:              uuid.New(),
					BudgetProfileID: profileID,
					Name:            "Car insurance",
					IntervalMonths:  3,
					DayOfMonth:      15,
					CreatedAt:       pgtype.Timestamptz{Time: createdAt, Valid: true},
				}}, nil
			},
		},
		&mockUserRepo{},
	)

	_, err := svc.CreateBudgetPeriod(context.Background(), profileID, ownerID)
	require.NoError(t, err)
	assert.False(t, created, "not-due fixed expense should not spawn a transaction")
}

func TestCreateBudgetPeriod_FixedExpense_SpawnsWhenDue(t *testing.T) {
	ownerID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	now := time.Now().UTC()
	currentMonthStart := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
	// Created exactly 3 months ago on a quarterly cadence — due this month.
	createdAt := currentMonthStart.AddDate(0, -3, 0)
	var createdArg db.CreateTransactionParams
	created := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID, Cycle: "monthly"}, nil
			},
		},
		&mockTransactionRepo{
			create: func(_ context.Context, arg db.CreateTransactionParams) (db.Transaction, error) {
				created = true
				createdArg = arg
				return db.Transaction{}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{{
					ID:              feID,
					BudgetProfileID: profileID,
					Name:            "Car insurance",
					IntervalMonths:  3,
					DayOfMonth:      15,
					CreatedAt:       pgtype.Timestamptz{Time: createdAt, Valid: true},
				}}, nil
			},
			hasTransactionInMonth: func(_ context.Context, _ db.FixedExpenseHasTransactionInMonthParams) (bool, error) {
				return false, nil
			},
		},
		&mockUserRepo{},
	)

	_, err := svc.CreateBudgetPeriod(context.Background(), profileID, ownerID)
	require.NoError(t, err)
	require.True(t, created, "due fixed expense should spawn a transaction")
	assert.Equal(t, &feID, createdArg.FixedExpenseID)
	assert.Equal(t, 15, createdArg.Date.Time.Day())
}

func TestCreateBudgetPeriod_FixedExpense_DedupSkipsAlreadySpawned(t *testing.T) {
	ownerID := uuid.New()
	profileID := uuid.New()
	now := time.Now().UTC()
	currentMonthStart := time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, time.UTC)
	createdAt := currentMonthStart // due this month (interval 1, same month as anchor)
	created := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID, Cycle: "monthly"}, nil
			},
		},
		&mockTransactionRepo{
			create: func(_ context.Context, _ db.CreateTransactionParams) (db.Transaction, error) {
				created = true
				return db.Transaction{}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{{
					ID:              uuid.New(),
					BudgetProfileID: profileID,
					Name:            "Rent",
					IntervalMonths:  1,
					DayOfMonth:      1,
					CreatedAt:       pgtype.Timestamptz{Time: createdAt, Valid: true},
				}}, nil
			},
			// A transaction already exists for this fixed expense this month
			// (e.g. spawned by CreateFixedExpense's immediate-spawn path).
			hasTransactionInMonth: func(_ context.Context, _ db.FixedExpenseHasTransactionInMonthParams) (bool, error) {
				return true, nil
			},
		},
		&mockUserRepo{},
	)

	_, err := svc.CreateBudgetPeriod(context.Background(), profileID, ownerID)
	require.NoError(t, err)
	assert.False(t, created, "already-spawned fixed expense should not spawn a duplicate transaction")
}

// ── Weekly cadence (frequency_unit = WEEK) tests ───────────────────────────────

func TestIsFixedExpenseDueInWeek(t *testing.T) {
	anchor := time.Date(2026, time.January, 5, 0, 0, 0, 0, time.UTC) // a Monday

	tests := []struct {
		name     string
		interval int32
		week     time.Time
		want     bool
	}{
		{"weekly, anchor week is due", 1, time.Date(2026, time.January, 5, 0, 0, 0, 0, time.UTC), true},
		{"weekly, every week is due", 1, time.Date(2026, time.February, 2, 0, 0, 0, 0, time.UTC), true},
		{"unset interval treated as weekly", 0, time.Date(2026, time.January, 26, 0, 0, 0, 0, time.UTC), true},
		{"bi-weekly, anchor week is due", 2, time.Date(2026, time.January, 5, 0, 0, 0, 0, time.UTC), true},
		{"bi-weekly, one week after anchor is not due", 2, time.Date(2026, time.January, 12, 0, 0, 0, 0, time.UTC), false},
		{"bi-weekly, two weeks after anchor is due", 2, time.Date(2026, time.January, 19, 0, 0, 0, 0, time.UTC), true},
		{"week before anchor is never due", 1, time.Date(2025, time.December, 29, 0, 0, 0, 0, time.UTC), false},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			fe := db.FixedExpense{
				CreatedAt:     pgtype.Timestamptz{Time: anchor, Valid: true},
				IntervalWeeks: tt.interval,
			}
			assert.Equal(t, tt.want, isFixedExpenseDueInWeek(fe, tt.week))
		})
	}
}

func TestFixedExpenseWeekStart_ReturnsMonday(t *testing.T) {
	// Wednesday, Jan 7 2026 -> Monday, Jan 5 2026.
	ws := fixedExpenseWeekStart(time.Date(2026, time.January, 7, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, time.Date(2026, time.January, 5, 0, 0, 0, 0, time.UTC), ws)
	assert.Equal(t, time.Monday, ws.Weekday())

	// A Sunday should roll back to the Monday that started its own week, not
	// forward into the next one.
	ws = fixedExpenseWeekStart(time.Date(2026, time.January, 11, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, time.Date(2026, time.January, 5, 0, 0, 0, 0, time.UTC), ws)
}

func TestFixedExpenseNextDueDate_WeeklyUnit(t *testing.T) {
	anchor := time.Date(2026, time.January, 5, 0, 0, 0, 0, time.UTC) // Monday
	fe := db.FixedExpense{
		CreatedAt:     pgtype.Timestamptz{Time: anchor, Valid: true},
		FrequencyUnit: frequencyUnitWeek,
		IntervalWeeks: 2,
		DayOfWeek:     3, // Wednesday
	}

	// Asking from the anchor week returns that week's Wednesday.
	next := FixedExpenseNextDueDate(fe, time.Date(2026, time.January, 5, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, time.Date(2026, time.January, 7, 0, 0, 0, 0, time.UTC), next)

	// Asking from a not-due week (one week after anchor, bi-weekly cadence)
	// finds the next due week ahead.
	next = FixedExpenseNextDueDate(fe, time.Date(2026, time.January, 12, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, time.Date(2026, time.January, 21, 0, 0, 0, 0, time.UTC), next)
}

func TestFixedExpenseNextDueDate_WeeklyUnit_WithFutureAnchorDate(t *testing.T) {
	futureAnchor := time.Date(2027, time.March, 8, 0, 0, 0, 0, time.UTC) // a Monday
	fe := db.FixedExpense{
		CreatedAt:     pgtype.Timestamptz{Time: time.Date(2026, time.July, 10, 0, 0, 0, 0, time.UTC), Valid: true},
		AnchorDate:    pgtype.Date{Time: futureAnchor, Valid: true},
		FrequencyUnit: frequencyUnitWeek,
		IntervalWeeks: 1,
		DayOfWeek:     1,
	}
	next := FixedExpenseNextDueDate(fe, time.Date(2026, time.July, 1, 0, 0, 0, 0, time.UTC))
	assert.Equal(t, futureAnchor, next, "next due date should be the future anchor's week, not created_at's week")
}

func TestCreateFixedExpense_WeekUnit_SpawnsMultipleInActivePeriod(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	anchor := time.Date(2020, time.January, 6, 0, 0, 0, 0, time.UTC) // a Monday, long past — always due weekly
	periodStart := time.Date(2026, time.February, 1, 0, 0, 0, 0, time.UTC)
	periodEnd := time.Date(2026, time.February, 28, 0, 0, 0, 0, time.UTC)
	var createdDates []time.Time

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			getLatestPeriod: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{
					ID:        uuid.New(),
					StartDate: pgtype.Date{Time: periodStart, Valid: true},
					EndDate:   pgtype.Date{Time: periodEnd, Valid: true},
				}, nil
			},
		},
		&mockTransactionRepo{
			create: func(_ context.Context, arg db.CreateTransactionParams) (db.Transaction, error) {
				createdDates = append(createdDates, arg.Date.Time)
				return db.Transaction{}, nil
			},
		},
		&mockFixedExpenseRepo{
			create: func(_ context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
				assert.Equal(t, frequencyUnitWeek, arg.FrequencyUnit)
				return db.FixedExpense{
					ID:              feID,
					BudgetProfileID: profileID,
					Name:            arg.Name,
					FrequencyUnit:   arg.FrequencyUnit,
					IntervalWeeks:   arg.IntervalWeeks,
					DayOfWeek:       arg.DayOfWeek,
					CreatedAt:       pgtype.Timestamptz{Time: anchor, Valid: true},
				}, nil
			},
			hasTransactionOnDate: func(_ context.Context, _ db.FixedExpenseHasTransactionOnDateParams) (bool, error) {
				return false, nil
			},
		},
		&mockUserRepo{},
	)

	fe, tx, err := svc.CreateFixedExpense(context.Background(), profileID, userID, FixedExpenseInput{
		Name:          "Groceries",
		FrequencyUnit: frequencyUnitWeek,
		IntervalWeeks: 1,
		DayOfWeek:     1, // Monday
	})
	require.NoError(t, err)
	assert.Equal(t, frequencyUnitWeek, fe.FrequencyUnit)
	assert.Nil(t, tx, "WEEK unit has no single transaction to return — caller re-fetches")

	want := 0
	for d := periodStart; d.Before(periodEnd); d = d.AddDate(0, 0, 1) {
		if d.Weekday() == time.Monday {
			want++
		}
	}
	require.Len(t, createdDates, want)
	require.Greater(t, want, 1, "test period should span more than one Monday to prove multi-spawn")
	for _, d := range createdDates {
		assert.Equal(t, time.Monday, d.Weekday())
	}
}

func TestUpdateFixedExpense_WeekUnit_DoesNotReconcileExistingTransactions(t *testing.T) {
	userID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	deleted := false
	propagated := false

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			getLatestPeriod: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				t.Fatal("WEEK-unit update should not look up the active period — it never reconciles existing transactions")
				return db.BudgetPeriod{}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{
			update: func(_ context.Context, arg db.UpdateFixedExpenseParams) (db.FixedExpense, error) {
				assert.Equal(t, frequencyUnitWeek, arg.FrequencyUnit)
				return db.FixedExpense{ID: feID, Name: arg.Name, FrequencyUnit: arg.FrequencyUnit, IntervalWeeks: arg.IntervalWeeks, DayOfWeek: arg.DayOfWeek}, nil
			},
			deleteUnpaidTransactions: func(_ context.Context, _ db.DeleteUnpaidTransactionByFixedExpenseParams) error {
				deleted = true
				return nil
			},
			updateTransactionFromFixed: func(_ context.Context, _ db.UpdateTransactionFromFixedExpenseParams) error {
				propagated = true
				return nil
			},
		},
		&mockUserRepo{},
	)

	fe, err := svc.UpdateFixedExpense(context.Background(), feID, profileID, userID, FixedExpenseInput{
		Name:          "Groceries",
		FrequencyUnit: frequencyUnitWeek,
		IntervalWeeks: 2,
		DayOfWeek:     5,
	})
	require.NoError(t, err)
	assert.Equal(t, frequencyUnitWeek, fe.FrequencyUnit)
	assert.False(t, deleted, "WEEK-unit update must not delete already-spawned transactions")
	assert.False(t, propagated, "WEEK-unit update must not propagate into already-spawned transactions")
}

func TestCreateBudgetPeriod_FixedExpense_WeekUnit_SpawnsMultipleDueWeeksWithDedup(t *testing.T) {
	ownerID := uuid.New()
	profileID := uuid.New()
	feID := uuid.New()
	prevEnd := time.Date(2026, time.January, 31, 0, 0, 0, 0, time.UTC)
	startDate, endDate := computeNextPeriodDates("monthly", prevEnd) // Feb 1 - Feb 28, 2026
	anchor := time.Date(2020, time.January, 6, 0, 0, 0, 0, time.UTC) // a Monday, long past — always due weekly
	// Pretend the first due Monday in range was already spawned (e.g. by
	// CreateFixedExpense's immediate-spawn path) to prove per-date dedup.
	alreadySpawned := fixedExpenseWeekStart(startDate)
	for alreadySpawned.Before(startDate) {
		alreadySpawned = alreadySpawned.AddDate(0, 0, 7)
	}
	var createdDates []time.Time

	svc := NewBudgetProfileService(
		&mockBudgetProfileRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: ownerID, Cycle: "monthly"}, nil
			},
			getLatestPeriod: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: uuid.New(), EndDate: pgtype.Date{Time: prevEnd, Valid: true}}, nil
			},
		},
		&mockTransactionRepo{
			create: func(_ context.Context, arg db.CreateTransactionParams) (db.Transaction, error) {
				createdDates = append(createdDates, arg.Date.Time)
				return db.Transaction{}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{{
					ID:              feID,
					BudgetProfileID: profileID,
					Name:            "Groceries",
					FrequencyUnit:   frequencyUnitWeek,
					IntervalWeeks:   1,
					DayOfWeek:       1, // Monday
					CreatedAt:       pgtype.Timestamptz{Time: anchor, Valid: true},
				}}, nil
			},
			hasTransactionOnDate: func(_ context.Context, arg db.FixedExpenseHasTransactionOnDateParams) (bool, error) {
				return arg.TargetDate.Time.Equal(alreadySpawned), nil
			},
		},
		&mockUserRepo{},
	)

	_, err := svc.CreateBudgetPeriod(context.Background(), profileID, ownerID)
	require.NoError(t, err)

	want := 0
	for d := startDate; d.Before(endDate); d = d.AddDate(0, 0, 1) {
		if d.Weekday() == time.Monday && !d.Equal(alreadySpawned) {
			want++
		}
	}
	require.Len(t, createdDates, want)
	for _, d := range createdDates {
		assert.Equal(t, time.Monday, d.Weekday())
		assert.False(t, d.Equal(alreadySpawned), "already-spawned date should be deduped")
	}
}
