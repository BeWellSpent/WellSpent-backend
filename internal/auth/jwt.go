package auth

import (
	"fmt"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"github.com/google/uuid"
)

type Claims struct {
	UserID string `json:"sub"`
	jwt.RegisteredClaims
}

type JWTService struct {
	secret   []byte
	lifetime time.Duration
}

func NewJWTService(secret string, lifetimeSeconds int) *JWTService {
	return &JWTService{
		secret:   []byte(secret),
		lifetime: time.Duration(lifetimeSeconds) * time.Second,
	}
}

func (s *JWTService) GenerateToken(userID uuid.UUID) (string, error) {
	claims := Claims{
		UserID: userID.String(),
		RegisteredClaims: jwt.RegisteredClaims{
			ExpiresAt: jwt.NewNumericDate(time.Now().Add(s.lifetime)),
			IssuedAt:  jwt.NewNumericDate(time.Now()),
		},
	}
	token := jwt.NewWithClaims(jwt.SigningMethodHS256, claims)
	signed, err := token.SignedString(s.secret)
	if err != nil {
		return "", fmt.Errorf("jwt: sign: %w", err)
	}
	return signed, nil
}

func (s *JWTService) ValidateToken(tokenStr string) (string, error) {
	token, err := jwt.ParseWithClaims(tokenStr, &Claims{}, func(t *jwt.Token) (any, error) {
		if _, ok := t.Method.(*jwt.SigningMethodHMAC); !ok {
			return nil, fmt.Errorf("unexpected signing method: %v", t.Header["alg"])
		}
		return s.secret, nil
	})
	if err != nil || !token.Valid {
		return "", fmt.Errorf("jwt: invalid token: %w", err)
	}
	claims, ok := token.Claims.(*Claims)
	if !ok {
		return "", fmt.Errorf("jwt: invalid claims")
	}
	return claims.UserID, nil
}

func (s *JWTService) LifetimeSeconds() int64 {
	return int64(s.lifetime.Seconds())
}
