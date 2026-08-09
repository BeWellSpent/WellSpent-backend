package auth

import (
	"context"
	"crypto/rsa"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math/big"
	"net/http"
	"net/url"
	"strings"
	"sync"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

const (
	appleIssuer     = "https://appleid.apple.com"
	appleJWKSURL    = "https://appleid.apple.com/auth/keys"
	appleTokenURL   = "https://appleid.apple.com/auth/token"
	appleRevokeURL  = "https://appleid.apple.com/auth/revoke"
	appleHTTPTimout = 10 * time.Second

	// clientSecretLifetime is well under Apple's 6-month cap. The secret is
	// minted per call rather than cached — an ECDSA sign is microseconds, and
	// the two operations that need one (first-link code exchange, revoke on
	// delete) happen at most once per account, so caching would only add a
	// staleness failure mode for no measurable gain.
	clientSecretLifetime = 5 * time.Minute

	// minJWKSRefetchInterval floors how often an unrecognised `kid` can force
	// a refetch, so a stream of tokens carrying garbage key IDs can't turn
	// into a request flood against Apple.
	minJWKSRefetchInterval = time.Minute
)

// ErrAppleKeyNotConfigured signals that APPLE_KEY_ID/APPLE_PRIVATE_KEY are
// unset, so no client secret can be minted. Callers treat this as a
// degradation to log, never a failure to surface: identity-token verification
// (and therefore sign-in itself) needs no private key, only the code exchange
// and revocation do.
var ErrAppleKeyNotConfigured = errors.New("apple: signing key not configured")

// AppleIdentity is the subset of a verified Apple identity token the app uses.
type AppleIdentity struct {
	// Sub is Apple's stable per-app user identifier. It is the only field
	// guaranteed present on every sign-in, so it — not the email — is the
	// key an account is looked up by.
	Sub string
	// Email may be a real address or an @privaterelay.appleid.com alias when
	// the user chose "Hide My Email".
	Email          string
	EmailVerified  bool
	IsPrivateEmail bool
}

// AppleAuthenticator is the seam the service layer depends on, so account
// resolution can be unit-tested without reaching Apple. (Contrast GoogleOAuth,
// a concrete struct, which is why GoogleExchange has no test coverage.)
type AppleAuthenticator interface {
	VerifyIdentityToken(ctx context.Context, identityToken string) (AppleIdentity, error)
	ExchangeCode(ctx context.Context, code string) (refreshToken string, err error)
	RevokeRefreshToken(ctx context.Context, refreshToken string) error
}

// AppleAuth implements AppleAuthenticator against Apple's real endpoints.
type AppleAuth struct {
	clientID   string
	teamID     string
	keyID      string
	privateKey string

	httpClient *http.Client

	// Overridable in tests (same package); production values come from the
	// constructor.
	jwksURL   string
	tokenURL  string
	revokeURL string

	mu        sync.RWMutex
	keys      map[string]*rsa.PublicKey
	fetchedAt time.Time
}

func NewAppleAuth(clientID, teamID, keyID, privateKey string) *AppleAuth {
	return &AppleAuth{
		clientID:   clientID,
		teamID:     teamID,
		keyID:      keyID,
		privateKey: privateKey,
		httpClient: &http.Client{Timeout: appleHTTPTimout},
		jwksURL:    appleJWKSURL,
		tokenURL:   appleTokenURL,
		revokeURL:  appleRevokeURL,
		keys:       map[string]*rsa.PublicKey{},
	}
}

// flexBool decodes Apple's boolean-ish claims, which have historically been
// serialised both as real JSON booleans and as the strings "true"/"false"
// depending on the endpoint and API vintage.
type flexBool bool

func (b *flexBool) UnmarshalJSON(data []byte) error {
	var asBool bool
	if err := json.Unmarshal(data, &asBool); err == nil {
		*b = flexBool(asBool)
		return nil
	}
	var asString string
	if err := json.Unmarshal(data, &asString); err == nil {
		*b = flexBool(asString == "true")
		return nil
	}
	return fmt.Errorf("apple: claim is neither bool nor string: %s", string(data))
}

type appleClaims struct {
	jwt.RegisteredClaims
	Email          string   `json:"email"`
	EmailVerified  flexBool `json:"email_verified"`
	IsPrivateEmail flexBool `json:"is_private_email"`
}

// VerifyIdentityToken checks the token's signature against Apple's published
// JWKS and validates issuer, audience and expiry. Any failure is a plain
// error — the service layer maps it to an invalid-argument response rather
// than leaking which specific check failed.
func (a *AppleAuth) VerifyIdentityToken(ctx context.Context, identityToken string) (AppleIdentity, error) {
	if strings.TrimSpace(identityToken) == "" {
		return AppleIdentity{}, errors.New("apple: empty identity token")
	}

	var claims appleClaims
	_, err := jwt.ParseWithClaims(identityToken, &claims, func(token *jwt.Token) (any, error) {
		kid, _ := token.Header["kid"].(string)
		if kid == "" {
			return nil, errors.New("apple: token has no kid header")
		}
		return a.publicKey(ctx, kid)
	},
		jwt.WithValidMethods([]string{"RS256"}),
		jwt.WithIssuer(appleIssuer),
		jwt.WithAudience(a.clientID),
		jwt.WithExpirationRequired(),
	)
	if err != nil {
		return AppleIdentity{}, fmt.Errorf("apple: verify identity token: %w", err)
	}
	if claims.Subject == "" {
		return AppleIdentity{}, errors.New("apple: token has no subject")
	}

	return AppleIdentity{
		Sub:            claims.Subject,
		Email:          strings.ToLower(strings.TrimSpace(claims.Email)),
		EmailVerified:  bool(claims.EmailVerified),
		IsPrivateEmail: bool(claims.IsPrivateEmail),
	}, nil
}

// publicKey returns the cached RSA key for kid, refetching the key set once if
// the kid is unknown (Apple rotates keys without notice).
func (a *AppleAuth) publicKey(ctx context.Context, kid string) (*rsa.PublicKey, error) {
	a.mu.RLock()
	key, ok := a.keys[kid]
	lastFetch := a.fetchedAt
	a.mu.RUnlock()
	if ok {
		return key, nil
	}

	if !lastFetch.IsZero() && time.Since(lastFetch) < minJWKSRefetchInterval {
		return nil, fmt.Errorf("apple: unknown key id %q", kid)
	}
	if err := a.refreshKeys(ctx); err != nil {
		return nil, err
	}

	a.mu.RLock()
	key, ok = a.keys[kid]
	a.mu.RUnlock()
	if !ok {
		return nil, fmt.Errorf("apple: unknown key id %q", kid)
	}
	return key, nil
}

type appleJWK struct {
	Kty string `json:"kty"`
	Kid string `json:"kid"`
	N   string `json:"n"`
	E   string `json:"e"`
}

func (a *AppleAuth) refreshKeys(ctx context.Context) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, a.jwksURL, nil)
	if err != nil {
		return fmt.Errorf("apple: build jwks request: %w", err)
	}
	resp, err := a.httpClient.Do(req)
	if err != nil {
		return fmt.Errorf("apple: fetch jwks: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("apple: fetch jwks: unexpected status %d", resp.StatusCode)
	}

	var payload struct {
		Keys []appleJWK `json:"keys"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&payload); err != nil {
		return fmt.Errorf("apple: decode jwks: %w", err)
	}

	parsed := make(map[string]*rsa.PublicKey, len(payload.Keys))
	for _, k := range payload.Keys {
		if k.Kty != "RSA" || k.Kid == "" {
			continue
		}
		key, err := rsaPublicKeyFromJWK(k)
		if err != nil {
			// One malformed entry shouldn't invalidate the rest of the set.
			continue
		}
		parsed[k.Kid] = key
	}
	if len(parsed) == 0 {
		return errors.New("apple: jwks contained no usable RSA keys")
	}

	a.mu.Lock()
	a.keys = parsed
	a.fetchedAt = time.Now()
	a.mu.Unlock()
	return nil
}

func rsaPublicKeyFromJWK(k appleJWK) (*rsa.PublicKey, error) {
	nBytes, err := base64.RawURLEncoding.DecodeString(k.N)
	if err != nil {
		return nil, fmt.Errorf("apple: decode modulus: %w", err)
	}
	eBytes, err := base64.RawURLEncoding.DecodeString(k.E)
	if err != nil {
		return nil, fmt.Errorf("apple: decode exponent: %w", err)
	}
	if len(nBytes) == 0 || len(eBytes) == 0 {
		return nil, errors.New("apple: empty modulus or exponent")
	}
	// The exponent is a big-endian integer of unspecified width (Apple sends
	// three bytes for 65537); big.Int handles any width without assuming one.
	return &rsa.PublicKey{
		N: new(big.Int).SetBytes(nBytes),
		E: int(new(big.Int).SetBytes(eBytes).Int64()),
	}, nil
}

// clientSecret mints the short-lived ES256 JWT Apple accepts in place of a
// static client secret on its token endpoints.
func (a *AppleAuth) clientSecret() (string, error) {
	if a.keyID == "" || strings.TrimSpace(a.privateKey) == "" {
		return "", ErrAppleKeyNotConfigured
	}
	key, err := jwt.ParseECPrivateKeyFromPEM([]byte(a.privateKey))
	if err != nil {
		return "", fmt.Errorf("apple: parse private key: %w", err)
	}
	now := time.Now()
	// MapClaims rather than RegisteredClaims: the latter's ClaimStrings
	// serialises `aud` as a one-element array, while Apple documents (and
	// every published example uses) a bare string.
	token := jwt.NewWithClaims(jwt.SigningMethodES256, jwt.MapClaims{
		"iss": a.teamID,
		"sub": a.clientID,
		"aud": appleIssuer,
		"iat": now.Unix(),
		"exp": now.Add(clientSecretLifetime).Unix(),
	})
	token.Header["kid"] = a.keyID
	signed, err := token.SignedString(key)
	if err != nil {
		return "", fmt.Errorf("apple: sign client secret: %w", err)
	}
	return signed, nil
}

// ExchangeCode trades the one-time authorization code from the client for a
// refresh token, which is the only credential Apple's revoke endpoint accepts.
// No redirect_uri is sent — native app flows don't have one.
func (a *AppleAuth) ExchangeCode(ctx context.Context, code string) (string, error) {
	if strings.TrimSpace(code) == "" {
		return "", errors.New("apple: empty authorization code")
	}
	secret, err := a.clientSecret()
	if err != nil {
		return "", err
	}

	form := url.Values{
		"client_id":     {a.clientID},
		"client_secret": {secret},
		"code":          {code},
		"grant_type":    {"authorization_code"},
	}
	body, err := a.postForm(ctx, a.tokenURL, form)
	if err != nil {
		return "", err
	}

	var payload struct {
		RefreshToken string `json:"refresh_token"`
	}
	if err := json.Unmarshal(body, &payload); err != nil {
		return "", fmt.Errorf("apple: decode token response: %w", err)
	}
	if payload.RefreshToken == "" {
		return "", errors.New("apple: token response contained no refresh token")
	}
	return payload.RefreshToken, nil
}

// RevokeRefreshToken invalidates the user's Apple credentials for this app, as
// App Store Review 5.1.1(v) requires on account deletion.
func (a *AppleAuth) RevokeRefreshToken(ctx context.Context, refreshToken string) error {
	if strings.TrimSpace(refreshToken) == "" {
		return errors.New("apple: empty refresh token")
	}
	secret, err := a.clientSecret()
	if err != nil {
		return err
	}

	form := url.Values{
		"client_id":       {a.clientID},
		"client_secret":   {secret},
		"token":           {refreshToken},
		"token_type_hint": {"refresh_token"},
	}
	_, err = a.postForm(ctx, a.revokeURL, form)
	return err
}

func (a *AppleAuth) postForm(ctx context.Context, endpoint string, form url.Values) ([]byte, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, endpoint, strings.NewReader(form.Encode()))
	if err != nil {
		return nil, fmt.Errorf("apple: build request: %w", err)
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")

	resp, err := a.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("apple: request %s: %w", endpoint, err)
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("apple: read response: %w", err)
	}
	if resp.StatusCode != http.StatusOK {
		// Apple returns {"error":"invalid_grant"} and friends; surfacing the
		// body makes a misconfigured key or a reused code diagnosable.
		return nil, fmt.Errorf("apple: %s returned %d: %s", endpoint, resp.StatusCode, strings.TrimSpace(string(body)))
	}
	return body, nil
}
