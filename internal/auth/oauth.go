package auth

import (
	"context"
	"encoding/json"
	"fmt"
	"io"

	"golang.org/x/oauth2"
	"golang.org/x/oauth2/google"
)

type GoogleOAuth struct {
	config *oauth2.Config
}

func NewGoogleOAuth(clientID, clientSecret, redirectURI string) *GoogleOAuth {
	return &GoogleOAuth{
		config: &oauth2.Config{
			ClientID:     clientID,
			ClientSecret: clientSecret,
			RedirectURL:  redirectURI,
			Scopes:       []string{"openid", "email", "profile"},
			Endpoint:     google.Endpoint,
		},
	}
}

func (g *GoogleOAuth) AuthCodeURL(state string) string {
	return g.config.AuthCodeURL(state, oauth2.AccessTypeOffline)
}

type GoogleUserInfo struct {
	Sub       string `json:"sub"`
	Email     string `json:"email"`
	GivenName string `json:"given_name"`
	FamilyName string `json:"family_name"`
}

func (g *GoogleOAuth) Exchange(ctx context.Context, code string) (*GoogleUserInfo, *oauth2.Token, error) {
	oauthToken, err := g.config.Exchange(ctx, code)
	if err != nil {
		return nil, nil, fmt.Errorf("google oauth: exchange: %w", err)
	}

	client := g.config.Client(ctx, oauthToken)
	resp, err := client.Get("https://www.googleapis.com/oauth2/v3/userinfo")
	if err != nil {
		return nil, nil, fmt.Errorf("google oauth: userinfo: %w", err)
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, nil, fmt.Errorf("google oauth: read body: %w", err)
	}

	var info GoogleUserInfo
	if err := json.Unmarshal(body, &info); err != nil {
		return nil, nil, fmt.Errorf("google oauth: decode userinfo: %w", err)
	}
	return &info, oauthToken, nil
}
