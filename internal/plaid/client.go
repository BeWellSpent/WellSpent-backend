package plaid

import (
	"context"
	"fmt"
	"time"

	plaidSDK "github.com/plaid/plaid-go/v20/plaid"
)

// Transaction is a Plaid transaction normalised for import into SpendSense.
type Transaction struct {
	PlaidID      string
	Name         string
	Amount       float64 // positive = debit (spending), negative = credit (received)
	Date         time.Time
	PFCPrimary   string // personal_finance_category.primary (e.g. "FOOD_AND_DRINK")
	PFCDetailed  string // personal_finance_category.detailed (e.g. "FOOD_AND_DRINK_GROCERIES")
}

// Client is a thin, mockable wrapper around the Plaid API.
type Client interface {
	// CreateLinkToken creates a Plaid Link token for the given user.
	CreateLinkToken(ctx context.Context, userID string) (linkToken, expiration string, err error)
	// ExchangePublicToken exchanges a Link public token for a permanent access token.
	ExchangePublicToken(ctx context.Context, publicToken string) (accessToken, itemID string, err error)
	// GetInstitutionName returns the display name for a Plaid institution ID.
	GetInstitutionName(ctx context.Context, institutionID string) (string, error)
	// RemoveItem notifies Plaid that the item should be deauthorised.
	RemoveItem(ctx context.Context, accessToken string) error
	// SyncTransactions fetches incremental transaction changes since cursor.
	// Pass an empty cursor for the initial sync.
	SyncTransactions(ctx context.Context, accessToken, cursor string) (added, modified []Transaction, removedIDs []string, nextCursor string, err error)
}

type client struct {
	api *plaidSDK.APIClient
}

// New builds a live Plaid API client.
// env must be "sandbox" or "production".
func New(clientID, secret, env string) (Client, error) {
	cfg := plaidSDK.NewConfiguration()
	cfg.AddDefaultHeader("PLAID-CLIENT-ID", clientID)
	cfg.AddDefaultHeader("PLAID-SECRET", secret)

	switch env {
	case "production":
		cfg.UseEnvironment(plaidSDK.Production)
	default:
		cfg.UseEnvironment(plaidSDK.Sandbox)
	}

	return &client{api: plaidSDK.NewAPIClient(cfg)}, nil
}

func (c *client) CreateLinkToken(ctx context.Context, userID string) (string, string, error) {
	user := plaidSDK.LinkTokenCreateRequestUser{ClientUserId: userID}
	req := plaidSDK.NewLinkTokenCreateRequest("WellSpent", "en", []plaidSDK.CountryCode{plaidSDK.COUNTRYCODE_US}, user)
	req.SetProducts([]plaidSDK.Products{plaidSDK.PRODUCTS_TRANSACTIONS})

	resp, _, err := c.api.PlaidApi.LinkTokenCreate(ctx).LinkTokenCreateRequest(*req).Execute()
	if err != nil {
		return "", "", fmt.Errorf("plaid: create link token: %w", err)
	}
	exp := resp.GetExpiration().Format(time.RFC3339)
	return resp.GetLinkToken(), exp, nil
}

func (c *client) ExchangePublicToken(ctx context.Context, publicToken string) (string, string, error) {
	req := plaidSDK.NewItemPublicTokenExchangeRequest(publicToken)
	resp, _, err := c.api.PlaidApi.ItemPublicTokenExchange(ctx).ItemPublicTokenExchangeRequest(*req).Execute()
	if err != nil {
		return "", "", fmt.Errorf("plaid: exchange public token: %w", err)
	}
	return resp.GetAccessToken(), resp.GetItemId(), nil
}

func (c *client) GetInstitutionName(ctx context.Context, institutionID string) (string, error) {
	req := plaidSDK.NewInstitutionsGetByIdRequest(institutionID, []plaidSDK.CountryCode{plaidSDK.COUNTRYCODE_US})
	resp, _, err := c.api.PlaidApi.InstitutionsGetById(ctx).InstitutionsGetByIdRequest(*req).Execute()
	if err != nil {
		return "", fmt.Errorf("plaid: get institution: %w", err)
	}
	inst := resp.GetInstitution()
	return inst.Name, nil
}

func (c *client) RemoveItem(ctx context.Context, accessToken string) error {
	req := plaidSDK.NewItemRemoveRequest(accessToken)
	_, _, err := c.api.PlaidApi.ItemRemove(ctx).ItemRemoveRequest(*req).Execute()
	if err != nil {
		return fmt.Errorf("plaid: remove item: %w", err)
	}
	return nil
}

func (c *client) SyncTransactions(ctx context.Context, accessToken, cursor string) ([]Transaction, []Transaction, []string, string, error) {
	req := plaidSDK.NewTransactionsSyncRequest(accessToken)
	if cursor != "" {
		req.SetCursor(cursor)
	}

	resp, _, err := c.api.PlaidApi.TransactionsSync(ctx).TransactionsSyncRequest(*req).Execute()
	if err != nil {
		return nil, nil, nil, "", fmt.Errorf("plaid: sync transactions: %w", err)
	}

	added := toTransactions(resp.GetAdded())
	modified := toTransactions(resp.GetModified())

	var removedIDs []string
	for _, r := range resp.GetRemoved() {
		removedIDs = append(removedIDs, r.GetTransactionId())
	}

	return added, modified, removedIDs, resp.GetNextCursor(), nil
}

// isCreditCardPayment returns true for transactions that represent paying a
// credit card bill — these are fund movements between accounts, not spending.
// All other loan payments and transfers are kept (they represent real outflows).
func isCreditCardPayment(t plaidSDK.Transaction) bool {
	if pfc, ok := t.GetPersonalFinanceCategoryOk(); ok && pfc != nil {
		return pfc.GetDetailed() == "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT"
	}
	return false
}

func toTransactions(ts []plaidSDK.Transaction) []Transaction {
	out := make([]Transaction, 0, len(ts))
	for _, t := range ts {
		if isCreditCardPayment(t) {
			continue
		}

		// Prefer authorized_date (when the user made the purchase) over date
		// (when the bank settled it). The posted date can be 1–3 days later,
		// which would mis-route boundary transactions to the wrong period.
		dateStr := t.GetDate()
		if ad, ok := t.GetAuthorizedDateOk(); ok && ad != nil && *ad != "" {
			dateStr = *ad
		}
		d, err := time.Parse("2006-01-02", dateStr)
		if err != nil {
			continue
		}
		// Prefer the Plaid-enriched merchant name when available — it's a
		// cleaner, more human-readable version of the raw bank string.
		name := t.GetName()
		if mn := t.GetMerchantName(); mn != "" {
			name = mn
		}

		primary, detailed := "", ""
		if pfc, ok := t.GetPersonalFinanceCategoryOk(); ok && pfc != nil {
			primary = pfc.GetPrimary()
			detailed = pfc.GetDetailed()
		}

		out = append(out, Transaction{
			PlaidID:     t.GetTransactionId(),
			Name:        name,
			Amount:      t.GetAmount(),
			Date:        d,
			PFCPrimary:  primary,
			PFCDetailed: detailed,
		})
	}
	return out
}
