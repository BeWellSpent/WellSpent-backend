package main

import (
	"errors"
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestBuildFailureEmail_SummarizesEachFailure(t *testing.T) {
	failures := []syncFailure{
		{ItemID: "item-1", Err: errors.New("plaid: TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION")},
		{ItemID: "item-2", Err: errors.New("context deadline exceeded")},
	}

	subject, body := buildFailureEmail(failures, nil)

	assert.Equal(t, "WellSpent Plaid sync: 2 item(s) failed", subject)
	assert.Contains(t, body, "item-1")
	assert.Contains(t, body, "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION")
	assert.Contains(t, body, "item-2")
	assert.Contains(t, body, "context deadline exceeded")
}

func TestBuildFailureEmail_EscapesErrorText(t *testing.T) {
	failures := []syncFailure{
		{ItemID: "item-1", Err: errors.New("<script>alert(1)</script>")},
	}

	_, body := buildFailureEmail(failures, nil)

	assert.NotContains(t, body, "<script>")
	assert.Contains(t, body, "&lt;script&gt;")
}

func TestBuildFailureEmail_SingularCount(t *testing.T) {
	failures := []syncFailure{{ItemID: "item-1", Err: errors.New("boom")}}

	subject, _ := buildFailureEmail(failures, nil)

	assert.Equal(t, "WellSpent Plaid sync: 1 item(s) failed", subject)
}

func TestBuildFailureEmail_NamesTheBudgetAndInstitution(t *testing.T) {
	failures := []syncFailure{
		{Profile: "profile-abc", Institution: "Chase", ItemID: "item-1", Err: errors.New("boom")},
	}

	_, body := buildFailureEmail(failures, nil)

	// A failure line that names the budget and the bank can be acted on from
	// the log alone; a bare item UUID previously required a database query.
	assert.Contains(t, body, "profile-abc")
	assert.Contains(t, body, "Chase")
}

func TestBuildFailureEmail_ReportsUnentitledSkipsSeparatelyFromFailures(t *testing.T) {
	skipped := []syncSkip{{Profile: "profile-abc", Institution: "Chase", ItemID: "item-9"}}

	subject, body := buildFailureEmail(nil, skipped)

	assert.Equal(t, "WellSpent Plaid sync: 1 connection(s) skipped", subject)
	assert.Contains(t, body, "not on a paid plan")
	assert.Contains(t, body, "Chase")
	assert.NotContains(t, body, "failed during this run")
}

func TestBuildFailureEmail_CombinesFailuresAndSkips(t *testing.T) {
	failures := []syncFailure{{Profile: "p1", Institution: "Amex", ItemID: "i1", Err: errors.New("boom")}}
	skipped := []syncSkip{{Profile: "p2", Institution: "Citi", ItemID: "i2"}}

	subject, body := buildFailureEmail(failures, skipped)

	assert.Equal(t, "WellSpent Plaid sync: 1 failed, 1 skipped", subject)
	assert.Contains(t, body, "Amex")
	assert.Contains(t, body, "Citi")
}
