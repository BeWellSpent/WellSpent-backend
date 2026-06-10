package handler

import (
	"context"

	"connectrpc.com/connect"
	v1 "github.com/mauro-afa91/spendsense/gen/spendsense/v1"
	"github.com/mauro-afa91/spendsense/internal/service"
)

type AuthHandler struct {
	svc *service.AuthService
}

func NewAuthHandler(svc *service.AuthService) *AuthHandler {
	return &AuthHandler{svc: svc}
}

func (h *AuthHandler) Login(ctx context.Context, req *connect.Request[v1.LoginRequest]) (*connect.Response[v1.LoginResponse], error) {
	result, err := h.svc.Login(ctx, req.Msg.Email, req.Msg.Password)
	if err != nil {
		return nil, toConnectError(err)
	}
	return connect.NewResponse(&v1.LoginResponse{
		AccessToken: result.AccessToken,
		TokenType:   "bearer",
		ExpiresIn:   result.ExpiresIn,
	}), nil
}

func (h *AuthHandler) Logout(_ context.Context, _ *connect.Request[v1.LogoutRequest]) (*connect.Response[v1.LogoutResponse], error) {
	// JWT is stateless; invalidation is handled client-side by discarding the token.
	return connect.NewResponse(&v1.LogoutResponse{}), nil
}

func (h *AuthHandler) RefreshToken(_ context.Context, _ *connect.Request[v1.RefreshTokenRequest]) (*connect.Response[v1.RefreshTokenResponse], error) {
	return nil, connect.NewError(connect.CodeUnimplemented, nil)
}

func (h *AuthHandler) GetGoogleAuthURL(_ context.Context, req *connect.Request[v1.GetGoogleAuthURLRequest]) (*connect.Response[v1.GetGoogleAuthURLResponse], error) {
	url := h.svc.GoogleAuthURL(req.Msg.State)
	return connect.NewResponse(&v1.GetGoogleAuthURLResponse{Url: url}), nil
}

func (h *AuthHandler) ExchangeGoogleCode(ctx context.Context, req *connect.Request[v1.ExchangeGoogleCodeRequest]) (*connect.Response[v1.ExchangeGoogleCodeResponse], error) {
	result, err := h.svc.GoogleExchange(ctx, req.Msg.Code, req.Msg.RedirectUri)
	if err != nil {
		return nil, toConnectError(err)
	}
	return connect.NewResponse(&v1.ExchangeGoogleCodeResponse{
		AccessToken: result.AccessToken,
		ExpiresIn:   result.ExpiresIn,
		IsNewUser:   result.IsNewUser,
	}), nil
}
