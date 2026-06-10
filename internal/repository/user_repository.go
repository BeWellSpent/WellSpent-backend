package repository

import (
	"context"
	"errors"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type UserRepository interface {
	GetByID(ctx context.Context, id uuid.UUID) (db.User, error)
	GetByEmail(ctx context.Context, email string) (db.User, error)
	Create(ctx context.Context, arg db.CreateUserParams) (db.User, error)
	Update(ctx context.Context, arg db.UpdateUserParams) (db.User, error)
	UpdatePassword(ctx context.Context, arg db.UpdateUserPasswordParams) error
	Delete(ctx context.Context, id uuid.UUID) error
	GetOAuthAccount(ctx context.Context, arg db.GetOAuthAccountParams) (db.OauthAccount, error)
	CreateOAuthAccount(ctx context.Context, arg db.CreateOAuthAccountParams) (db.OauthAccount, error)
}

type userRepository struct {
	q *db.Queries
}

func NewUserRepository(q *db.Queries) UserRepository {
	return &userRepository{q: q}
}

func (r *userRepository) GetByID(ctx context.Context, id uuid.UUID) (db.User, error) {
	u, err := r.q.GetUserByID(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.User{}, apperr.NotFound("user", id.String())
	}
	return u, err
}

func (r *userRepository) GetByEmail(ctx context.Context, email string) (db.User, error) {
	u, err := r.q.GetUserByEmail(ctx, email)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.User{}, apperr.NotFound("user", email)
	}
	return u, err
}

func (r *userRepository) Create(ctx context.Context, arg db.CreateUserParams) (db.User, error) {
	return r.q.CreateUser(ctx, arg)
}

func (r *userRepository) Update(ctx context.Context, arg db.UpdateUserParams) (db.User, error) {
	return r.q.UpdateUser(ctx, arg)
}

func (r *userRepository) UpdatePassword(ctx context.Context, arg db.UpdateUserPasswordParams) error {
	return r.q.UpdateUserPassword(ctx, arg)
}

func (r *userRepository) Delete(ctx context.Context, id uuid.UUID) error {
	return r.q.DeleteUser(ctx, id)
}

func (r *userRepository) GetOAuthAccount(ctx context.Context, arg db.GetOAuthAccountParams) (db.OauthAccount, error) {
	o, err := r.q.GetOAuthAccount(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.OauthAccount{}, apperr.NotFound("oauth_account", arg.AccountID)
	}
	return o, err
}

func (r *userRepository) CreateOAuthAccount(ctx context.Context, arg db.CreateOAuthAccountParams) (db.OauthAccount, error) {
	return r.q.CreateOAuthAccount(ctx, arg)
}
