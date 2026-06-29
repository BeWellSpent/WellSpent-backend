package service

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/auth"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"golang.org/x/crypto/bcrypt"
)

// ── Mock ──────────────────────────────────────────────────────────────────────

type mockUserRepo struct {
	getByEmail            func(context.Context, string) (db.User, error)
	getByID               func(context.Context, uuid.UUID) (db.User, error)
	create                func(context.Context, db.CreateUserParams) (db.User, error)
	update                func(context.Context, db.UpdateUserParams) (db.User, error)
	updatePassword        func(context.Context, db.UpdateUserPasswordParams) error
	delete                func(context.Context, uuid.UUID) error
	getOAuth              func(context.Context, db.GetOAuthAccountParams) (db.OauthAccount, error)
	createOAuth           func(context.Context, db.CreateOAuthAccountParams) (db.OauthAccount, error)
	listEnabledCountries  func(context.Context) ([]db.ListEnabledCountriesRow, error)
	listCountryFeatures   func(context.Context) ([]db.CountryFeature, error)
}

func (m *mockUserRepo) GetByEmail(ctx context.Context, email string) (db.User, error) {
	if m.getByEmail != nil {
		return m.getByEmail(ctx, email)
	}
	return db.User{}, apperr.NotFound("user", email)
}

func (m *mockUserRepo) GetByID(ctx context.Context, id uuid.UUID) (db.User, error) {
	if m.getByID != nil {
		return m.getByID(ctx, id)
	}
	return db.User{}, apperr.NotFound("user", id.String())
}

func (m *mockUserRepo) Create(ctx context.Context, arg db.CreateUserParams) (db.User, error) {
	if m.create != nil {
		return m.create(ctx, arg)
	}
	return db.User{ID: uuid.New(), Email: arg.Email, IsActive: true}, nil
}

func (m *mockUserRepo) Update(ctx context.Context, arg db.UpdateUserParams) (db.User, error) {
	if m.update != nil {
		return m.update(ctx, arg)
	}
	return db.User{}, nil
}

func (m *mockUserRepo) UpdatePassword(ctx context.Context, arg db.UpdateUserPasswordParams) error {
	if m.updatePassword != nil {
		return m.updatePassword(ctx, arg)
	}
	return nil
}

func (m *mockUserRepo) Delete(ctx context.Context, id uuid.UUID) error {
	if m.delete != nil {
		return m.delete(ctx, id)
	}
	return nil
}

func (m *mockUserRepo) GetOAuthAccount(ctx context.Context, arg db.GetOAuthAccountParams) (db.OauthAccount, error) {
	if m.getOAuth != nil {
		return m.getOAuth(ctx, arg)
	}
	return db.OauthAccount{}, apperr.NotFound("oauth_account", arg.AccountID)
}

func (m *mockUserRepo) CreateOAuthAccount(ctx context.Context, arg db.CreateOAuthAccountParams) (db.OauthAccount, error) {
	if m.createOAuth != nil {
		return m.createOAuth(ctx, arg)
	}
	return db.OauthAccount{ID: uuid.New(), UserID: arg.UserID}, nil
}

func (m *mockUserRepo) ListEnabledCountries(ctx context.Context) ([]db.ListEnabledCountriesRow, error) {
	if m.listEnabledCountries != nil {
		return m.listEnabledCountries(ctx)
	}
	return nil, nil
}

func (m *mockUserRepo) ListCountryFeatures(ctx context.Context) ([]db.CountryFeature, error) {
	if m.listCountryFeatures != nil {
		return m.listCountryFeatures(ctx)
	}
	return nil, nil
}

// ── Helpers ───────────────────────────────────────────────────────────────────

func testJWT() *auth.JWTService {
	return auth.NewJWTService("test-secret-32-chars-minimum-ok!", 3600)
}

func newAuthSvc(repo *mockUserRepo) *AuthService {
	return NewAuthService(repo, testJWT(), nil)
}

func hashFor(t *testing.T, password string) string {
	t.Helper()
	h, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.MinCost)
	require.NoError(t, err)
	return string(h)
}

// ── Register ──────────────────────────────────────────────────────────────────

func TestRegister_Success(t *testing.T) {
	repo := &mockUserRepo{}
	result, err := newAuthSvc(repo).Register(context.Background(), "new@example.com", "Strong@1", "Jane", "Doe", "", "")
	require.NoError(t, err)
	assert.NotEmpty(t, result.AccessToken)
	assert.Equal(t, int64(3600), result.ExpiresIn)
}

func TestRegister_EmailNormalized(t *testing.T) {
	var capturedEmail string
	repo := &mockUserRepo{
		create: func(_ context.Context, arg db.CreateUserParams) (db.User, error) {
			capturedEmail = arg.Email
			return db.User{ID: uuid.New(), Email: arg.Email, IsActive: true}, nil
		},
	}
	_, err := newAuthSvc(repo).Register(context.Background(), "  USER@Example.COM  ", "Strong@1", "", "", "", "")
	require.NoError(t, err)
	assert.Equal(t, "user@example.com", capturedEmail)
}

func TestRegister_InvalidEmail(t *testing.T) {
	_, err := newAuthSvc(&mockUserRepo{}).Register(context.Background(), "not-an-email", "Strong@1", "", "", "", "")
	require.Error(t, err)
	var ve *apperr.ValidationError
	require.ErrorAs(t, err, &ve)
}

func TestRegister_DuplicateEmail(t *testing.T) {
	repo := &mockUserRepo{
		getByEmail: func(_ context.Context, email string) (db.User, error) {
			return db.User{Email: email, IsActive: true}, nil
		},
	}
	_, err := newAuthSvc(repo).Register(context.Background(), "exists@example.com", "Strong@1", "", "", "", "")
	require.Error(t, err)
	var de *apperr.DuplicateError
	require.ErrorAs(t, err, &de)
}

// ── Password validation ───────────────────────────────────────────────────────

func TestValidatePassword(t *testing.T) {
	tests := []struct {
		name     string
		password string
		wantErr  bool
	}{
		{"valid", "Strong@1", false},
		{"valid long", "MyP@ssw0rd!IsLong", false},
		{"too short", "Ab@1", true},
		{"no uppercase", "strong@1", true},
		{"no lowercase", "STRONG@1", true},
		{"no digit", "Strong@!", true},
		{"no special char", "Strong12", true},
		{"exactly 8 chars valid", "Aa1!aaaa", false},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			err := validatePassword(tt.password)
			if tt.wantErr {
				require.Error(t, err)
				var ve *apperr.ValidationError
				require.ErrorAs(t, err, &ve)
			} else {
				require.NoError(t, err)
			}
		})
	}
}

// ── Login ─────────────────────────────────────────────────────────────────────

func TestLogin_Success(t *testing.T) {
	h := hashFor(t, "Strong@1")
	repo := &mockUserRepo{
		getByEmail: func(_ context.Context, email string) (db.User, error) {
			return db.User{ID: uuid.New(), Email: email, HashedPassword: &h, IsActive: true}, nil
		},
	}
	result, err := newAuthSvc(repo).Login(context.Background(), "user@example.com", "Strong@1")
	require.NoError(t, err)
	assert.NotEmpty(t, result.AccessToken)
	assert.Equal(t, int64(3600), result.ExpiresIn)
}

func TestLogin_WrongPassword(t *testing.T) {
	h := hashFor(t, "Correct@1")
	repo := &mockUserRepo{
		getByEmail: func(_ context.Context, email string) (db.User, error) {
			return db.User{ID: uuid.New(), Email: email, HashedPassword: &h, IsActive: true}, nil
		},
	}
	_, err := newAuthSvc(repo).Login(context.Background(), "user@example.com", "Wrong@1")
	require.Error(t, err)
	var ve *apperr.ValidationError
	require.ErrorAs(t, err, &ve)
	assert.Contains(t, err.Error(), "invalid email or password")
}

func TestLogin_EmailNotFound(t *testing.T) {
	// Should surface as a generic error, not reveal that email doesn't exist
	_, err := newAuthSvc(&mockUserRepo{}).Login(context.Background(), "nobody@example.com", "Strong@1")
	require.Error(t, err)
	var ve *apperr.ValidationError
	require.ErrorAs(t, err, &ve)
	assert.Contains(t, err.Error(), "invalid email or password")
}

func TestLogin_InactiveAccount(t *testing.T) {
	h := hashFor(t, "Strong@1")
	repo := &mockUserRepo{
		getByEmail: func(_ context.Context, email string) (db.User, error) {
			return db.User{ID: uuid.New(), Email: email, HashedPassword: &h, IsActive: false}, nil
		},
	}
	_, err := newAuthSvc(repo).Login(context.Background(), "user@example.com", "Strong@1")
	require.Error(t, err)
	var fe *apperr.ForbiddenError
	require.ErrorAs(t, err, &fe)
}

func TestLogin_OAuthOnlyAccount(t *testing.T) {
	repo := &mockUserRepo{
		getByEmail: func(_ context.Context, email string) (db.User, error) {
			return db.User{ID: uuid.New(), Email: email, HashedPassword: nil, IsActive: true}, nil
		},
	}
	_, err := newAuthSvc(repo).Login(context.Background(), "oauth@example.com", "Strong@1")
	require.Error(t, err)
	var ve *apperr.ValidationError
	require.ErrorAs(t, err, &ve)
	assert.Contains(t, err.Error(), "OAuth")
}

// ── UserService ───────────────────────────────────────────────────────────────

func TestUserService_Update_PassesNewFields(t *testing.T) {
	id := uuid.New()
	cc := "US"
	sc := "CA"
	var captured db.UpdateUserParams
	repo := &mockUserRepo{
		update: func(_ context.Context, arg db.UpdateUserParams) (db.User, error) {
			captured = arg
			return db.User{}, nil
		},
	}
	svc := NewUserService(repo)
	_, err := svc.Update(context.Background(), id, UpdateUserInput{
		CountryCode:         &cc,
		StateCode:           &sc,
		FilingStatus:        "1",
		TaxPaymentFrequency: 3,
	})
	require.NoError(t, err)
	assert.Equal(t, &cc, captured.CountryCode)
	assert.Equal(t, &sc, captured.StateCode)
	assert.Equal(t, "1", captured.FilingStatus)
	assert.Equal(t, int32(3), captured.TaxPaymentFrequency)
}

func TestUserService_ListCountries_MergesFeatures(t *testing.T) {
	repo := &mockUserRepo{
		listEnabledCountries: func(_ context.Context) ([]db.ListEnabledCountriesRow, error) {
			return []db.ListEnabledCountriesRow{
				{Code: "US", Name: "United States", IsEnabled: true},
				{Code: "ES", Name: "Spain", IsEnabled: true},
			}, nil
		},
		listCountryFeatures: func(_ context.Context) ([]db.CountryFeature, error) {
			return []db.CountryFeature{
				{CountryCode: "US", FeatureName: "before_tax_income", IsEnabled: true},
			}, nil
		},
	}
	svc := NewUserService(repo)
	countries, byCode, err := svc.ListCountries(context.Background())
	require.NoError(t, err)
	assert.Len(t, countries, 2)
	assert.Len(t, byCode["US"], 1)
	assert.Equal(t, "before_tax_income", byCode["US"][0].FeatureName)
	assert.Empty(t, byCode["ES"])
}
