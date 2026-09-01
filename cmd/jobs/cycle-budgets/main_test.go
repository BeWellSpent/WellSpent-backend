package main

import (
	"errors"
	"testing"

	"github.com/BeWellSpent/wellspent-backend/internal/service"
	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
)

func TestDescribePreSyncResult_SyncErrorStillCyclesAnyway(t *testing.T) {
	got := describePreSyncResult(service.ProfileSyncResult{}, errors.New("db unavailable"))

	assert.Contains(t, got, "sync failed")
	assert.Contains(t, got, "db unavailable")
	assert.Contains(t, got, "cycling anyway")
}

func TestDescribePreSyncResult_NoConnections(t *testing.T) {
	got := describePreSyncResult(service.ProfileSyncResult{}, nil)

	assert.Equal(t, "no Plaid connections", got)
}

func TestDescribePreSyncResult_SummarizesImportsAndRepoints(t *testing.T) {
	result := service.ProfileSyncResult{
		ProfileID: uuid.New(),
		Items: []service.ItemSyncResult{
			{Imported: 3, Repointed: 1},
			{Imported: 2},
		},
	}

	got := describePreSyncResult(result, nil)

	assert.Contains(t, got, "2 connection(s)")
	assert.Contains(t, got, "5 imported")
	assert.Contains(t, got, "1 repointed")
	assert.NotContains(t, got, "failed", "no item failed, so the line must not claim one did")
}

func TestDescribePreSyncResult_ReportsPerItemFailuresButStillCycles(t *testing.T) {
	result := service.ProfileSyncResult{
		Items: []service.ItemSyncResult{
			{Imported: 1},
			{Err: errors.New("plaid: rate limited")},
		},
	}

	got := describePreSyncResult(result, nil)

	assert.Contains(t, got, "1 failed")
	assert.Contains(t, got, "cycling anyway")
}
