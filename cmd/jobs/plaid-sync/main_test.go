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

	subject, body := buildFailureEmail(failures)

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

	_, body := buildFailureEmail(failures)

	assert.NotContains(t, body, "<script>")
	assert.Contains(t, body, "&lt;script&gt;")
}

func TestBuildFailureEmail_SingularCount(t *testing.T) {
	failures := []syncFailure{{ItemID: "item-1", Err: errors.New("boom")}}

	subject, _ := buildFailureEmail(failures)

	assert.Equal(t, "WellSpent Plaid sync: 1 item(s) failed", subject)
}
