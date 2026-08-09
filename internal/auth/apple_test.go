package auth

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/rsa"
	"crypto/x509"
	"encoding/base64"
	"encoding/json"
	"encoding/pem"
	"math/big"
	"net/http"
	"net/http/httptest"
	"net/url"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

const testAppleClientID = "com.bewellspent.WellSpent"

// appleTestRig stands up a fake Apple: an RSA signing key, a JWKS endpoint
// serving its public half, and an AppleAuth pointed at it.
type appleTestRig struct {
	key    *rsa.PrivateKey
	kid    string
	server *httptest.Server
	auth   *AppleAuth
}

func newAppleTestRig(t *testing.T) *appleTestRig {
	t.Helper()
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	require.NoError(t, err)

	rig := &appleTestRig{key: key, kid: "test-key-1"}
	rig.server = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(map[string]any{
			"keys": []map[string]string{{
				"kty": "RSA",
				"kid": rig.kid,
				"use": "sig",
				"alg": "RS256",
				"n":   base64.RawURLEncoding.EncodeToString(key.N.Bytes()),
				"e":   base64.RawURLEncoding.EncodeToString(big.NewInt(int64(key.E)).Bytes()),
			}},
		})
	}))
	t.Cleanup(rig.server.Close)

	rig.auth = NewAppleAuth(testAppleClientID, "TEAMID1234", "", "")
	rig.auth.jwksURL = rig.server.URL
	return rig
}

type tokenOpts struct {
	issuer         string
	audience       string
	subject        string
	email          string
	emailVerified  any
	isPrivateEmail any
	expiresAt      time.Time
	kid            string
}

func (r *appleTestRig) mintToken(t *testing.T, opts tokenOpts) string {
	t.Helper()
	if opts.issuer == "" {
		opts.issuer = appleIssuer
	}
	if opts.audience == "" {
		opts.audience = testAppleClientID
	}
	if opts.subject == "" {
		opts.subject = "001234.abcdef.5678"
	}
	if opts.expiresAt.IsZero() {
		opts.expiresAt = time.Now().Add(time.Hour)
	}
	if opts.kid == "" {
		opts.kid = r.kid
	}
	if opts.emailVerified == nil {
		opts.emailVerified = true
	}

	claims := jwt.MapClaims{
		"iss": opts.issuer,
		"aud": opts.audience,
		"sub": opts.subject,
		"iat": time.Now().Unix(),
		"exp": opts.expiresAt.Unix(),
	}
	if opts.email != "" {
		claims["email"] = opts.email
	}
	claims["email_verified"] = opts.emailVerified
	if opts.isPrivateEmail != nil {
		claims["is_private_email"] = opts.isPrivateEmail
	}

	token := jwt.NewWithClaims(jwt.SigningMethodRS256, claims)
	token.Header["kid"] = opts.kid
	signed, err := token.SignedString(r.key)
	require.NoError(t, err)
	return signed
}

func TestVerifyIdentityToken_Valid(t *testing.T) {
	rig := newAppleTestRig(t)
	token := rig.mintToken(t, tokenOpts{subject: "sub-abc", email: "User@Example.com"})

	identity, err := rig.auth.VerifyIdentityToken(context.Background(), token)
	require.NoError(t, err)
	assert.Equal(t, "sub-abc", identity.Sub)
	assert.Equal(t, "user@example.com", identity.Email, "email should be normalised to lowercase")
	assert.True(t, identity.EmailVerified)
	assert.False(t, identity.IsPrivateEmail)
}

// Apple has historically serialised these claims as strings rather than
// booleans depending on the endpoint, so both shapes must decode.
func TestVerifyIdentityToken_StringBooleanClaims(t *testing.T) {
	rig := newAppleTestRig(t)
	token := rig.mintToken(t, tokenOpts{
		email:          "relay@privaterelay.appleid.com",
		emailVerified:  "true",
		isPrivateEmail: "true",
	})

	identity, err := rig.auth.VerifyIdentityToken(context.Background(), token)
	require.NoError(t, err)
	assert.True(t, identity.EmailVerified)
	assert.True(t, identity.IsPrivateEmail)
}

func TestVerifyIdentityToken_RejectsWrongAudience(t *testing.T) {
	rig := newAppleTestRig(t)
	token := rig.mintToken(t, tokenOpts{audience: "com.someone.else"})

	_, err := rig.auth.VerifyIdentityToken(context.Background(), token)
	require.Error(t, err)
}

func TestVerifyIdentityToken_RejectsWrongIssuer(t *testing.T) {
	rig := newAppleTestRig(t)
	token := rig.mintToken(t, tokenOpts{issuer: "https://evil.example.com"})

	_, err := rig.auth.VerifyIdentityToken(context.Background(), token)
	require.Error(t, err)
}

func TestVerifyIdentityToken_RejectsExpired(t *testing.T) {
	rig := newAppleTestRig(t)
	token := rig.mintToken(t, tokenOpts{expiresAt: time.Now().Add(-time.Minute)})

	_, err := rig.auth.VerifyIdentityToken(context.Background(), token)
	require.Error(t, err)
}

func TestVerifyIdentityToken_RejectsUnknownKeyID(t *testing.T) {
	rig := newAppleTestRig(t)
	token := rig.mintToken(t, tokenOpts{kid: "not-a-real-key"})

	_, err := rig.auth.VerifyIdentityToken(context.Background(), token)
	require.Error(t, err)
	assert.Contains(t, err.Error(), "unknown key id")
}

// A token signed by a key Apple never published must not be accepted just
// because it names a kid that is in the key set.
func TestVerifyIdentityToken_RejectsForeignSigningKey(t *testing.T) {
	rig := newAppleTestRig(t)
	attackerKey, err := rsa.GenerateKey(rand.Reader, 2048)
	require.NoError(t, err)

	claims := jwt.MapClaims{
		"iss": appleIssuer,
		"aud": testAppleClientID,
		"sub": "sub-abc",
		"exp": time.Now().Add(time.Hour).Unix(),
	}
	token := jwt.NewWithClaims(jwt.SigningMethodRS256, claims)
	token.Header["kid"] = rig.kid
	signed, err := token.SignedString(attackerKey)
	require.NoError(t, err)

	_, err = rig.auth.VerifyIdentityToken(context.Background(), signed)
	require.Error(t, err)
}

// "alg": "none" is the classic JWT bypass; WithValidMethods must block it.
func TestVerifyIdentityToken_RejectsUnsignedToken(t *testing.T) {
	rig := newAppleTestRig(t)
	token := jwt.NewWithClaims(jwt.SigningMethodNone, jwt.MapClaims{
		"iss": appleIssuer,
		"aud": testAppleClientID,
		"sub": "sub-abc",
		"exp": time.Now().Add(time.Hour).Unix(),
	})
	token.Header["kid"] = rig.kid
	signed, err := token.SignedString(jwt.UnsafeAllowNoneSignatureType)
	require.NoError(t, err)

	_, err = rig.auth.VerifyIdentityToken(context.Background(), signed)
	require.Error(t, err)
}

func TestVerifyIdentityToken_RejectsEmpty(t *testing.T) {
	rig := newAppleTestRig(t)
	_, err := rig.auth.VerifyIdentityToken(context.Background(), "   ")
	require.Error(t, err)
}

func TestVerifyIdentityToken_RefetchIsRateLimited(t *testing.T) {
	rig := newAppleTestRig(t)
	// Prime the cache so fetchedAt is set.
	valid := rig.mintToken(t, tokenOpts{})
	_, err := rig.auth.VerifyIdentityToken(context.Background(), valid)
	require.NoError(t, err)

	// Point the JWKS URL at a dead address: if the rate limiter didn't hold,
	// this would surface as a connection error rather than "unknown key id".
	rig.auth.jwksURL = "http://127.0.0.1:1"
	_, err = rig.auth.VerifyIdentityToken(context.Background(), rig.mintToken(t, tokenOpts{kid: "rotated-key"}))
	require.Error(t, err)
	assert.Contains(t, err.Error(), "unknown key id")
}

// ── Client secret ─────────────────────────────────────────────────────────────

func testECPrivateKeyPEM(t *testing.T) string {
	t.Helper()
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	require.NoError(t, err)
	der, err := x509.MarshalECPrivateKey(key)
	require.NoError(t, err)
	return string(pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: der}))
}

func TestClientSecret_ClaimsMatchApplesRequirements(t *testing.T) {
	keyPEM := testECPrivateKeyPEM(t)
	a := NewAppleAuth(testAppleClientID, "TEAMID1234", "KEYID5678", keyPEM)

	secret, err := a.clientSecret()
	require.NoError(t, err)

	parsed, _, err := jwt.NewParser().ParseUnverified(secret, jwt.MapClaims{})
	require.NoError(t, err)
	assert.Equal(t, "ES256", parsed.Header["alg"])
	assert.Equal(t, "KEYID5678", parsed.Header["kid"])

	claims := parsed.Claims.(jwt.MapClaims)
	assert.Equal(t, "TEAMID1234", claims["iss"], "issuer must be the Apple team ID")
	assert.Equal(t, testAppleClientID, claims["sub"], "subject must be the client ID")
	assert.Equal(t, appleIssuer, claims["aud"])
	assert.Greater(t, claims["exp"], claims["iat"])
}

func TestClientSecret_MissingKeyIsDistinguishable(t *testing.T) {
	_, err := NewAppleAuth(testAppleClientID, "TEAMID1234", "", "").clientSecret()
	require.ErrorIs(t, err, ErrAppleKeyNotConfigured)

	_, err = NewAppleAuth(testAppleClientID, "TEAMID1234", "KEYID5678", "  ").clientSecret()
	require.ErrorIs(t, err, ErrAppleKeyNotConfigured)
}

func TestClientSecret_MalformedKey(t *testing.T) {
	_, err := NewAppleAuth(testAppleClientID, "TEAMID1234", "KEYID5678", "not a pem").clientSecret()
	require.Error(t, err)
	assert.NotErrorIs(t, err, ErrAppleKeyNotConfigured, "a malformed key is a real error, not an unset-config no-op")
}

// ── Exchange / revoke ─────────────────────────────────────────────────────────

func TestExchangeCode_ReturnsRefreshToken(t *testing.T) {
	var gotForm url.Values
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		require.NoError(t, r.ParseForm())
		gotForm = r.PostForm
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"refresh_token":"rt-123","access_token":"at-123"}`))
	}))
	defer server.Close()

	a := NewAppleAuth(testAppleClientID, "TEAMID1234", "KEYID5678", testECPrivateKeyPEM(t))
	a.tokenURL = server.URL

	refreshToken, err := a.ExchangeCode(context.Background(), "the-code")
	require.NoError(t, err)
	assert.Equal(t, "rt-123", refreshToken)
	assert.Equal(t, "authorization_code", gotForm.Get("grant_type"))
	assert.Equal(t, "the-code", gotForm.Get("code"))
	assert.Equal(t, testAppleClientID, gotForm.Get("client_id"))
	assert.NotEmpty(t, gotForm.Get("client_secret"))
}

func TestExchangeCode_SurfacesAppleError(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusBadRequest)
		_, _ = w.Write([]byte(`{"error":"invalid_grant"}`))
	}))
	defer server.Close()

	a := NewAppleAuth(testAppleClientID, "TEAMID1234", "KEYID5678", testECPrivateKeyPEM(t))
	a.tokenURL = server.URL

	_, err := a.ExchangeCode(context.Background(), "reused-code")
	require.Error(t, err)
	assert.Contains(t, err.Error(), "invalid_grant", "Apple's own error code should reach the logs")
}

func TestExchangeCode_MissingRefreshToken(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		_, _ = w.Write([]byte(`{"access_token":"at-123"}`))
	}))
	defer server.Close()

	a := NewAppleAuth(testAppleClientID, "TEAMID1234", "KEYID5678", testECPrivateKeyPEM(t))
	a.tokenURL = server.URL

	_, err := a.ExchangeCode(context.Background(), "the-code")
	require.Error(t, err)
}

func TestRevokeRefreshToken_SendsCorrectForm(t *testing.T) {
	var gotForm url.Values
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		require.NoError(t, r.ParseForm())
		gotForm = r.PostForm
		w.WriteHeader(http.StatusOK)
	}))
	defer server.Close()

	a := NewAppleAuth(testAppleClientID, "TEAMID1234", "KEYID5678", testECPrivateKeyPEM(t))
	a.revokeURL = server.URL

	require.NoError(t, a.RevokeRefreshToken(context.Background(), "rt-123"))
	assert.Equal(t, "rt-123", gotForm.Get("token"))
	assert.Equal(t, "refresh_token", gotForm.Get("token_type_hint"))
	assert.NotEmpty(t, gotForm.Get("client_secret"))
}

func TestRevokeRefreshToken_WithoutKeyReportsUnconfigured(t *testing.T) {
	err := NewAppleAuth(testAppleClientID, "TEAMID1234", "", "").RevokeRefreshToken(context.Background(), "rt-123")
	require.ErrorIs(t, err, ErrAppleKeyNotConfigured)
}
