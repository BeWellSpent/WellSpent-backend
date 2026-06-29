package tax

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestEstimate_SingleNoState(t *testing.T) {
	// $100k gross, single filer, no state
	r := Estimate(100_000, "", FilingStatusSingle)
	// federal: (100k - 15k std deduction) = 85k taxable
	// 10% on first 11,925 = 1,192.50
	// 12% on next 36,550 (11,925→48,475) = 4,386.00
	// 22% on remaining 36,525 (48,475→85,000) = 8,035.50
	// total ≈ 13,614
	assert.InDelta(t, 13614.0, r.FederalTax, 5.0)
	assert.Equal(t, 0.0, r.StateTax)
	assert.InDelta(t, r.TotalAnnual/12, r.MonthlySaving, 0.01)
}

func TestEstimate_MFJWithCAState(t *testing.T) {
	// $200k gross, MFJ, California
	r := Estimate(200_000, "CA", FilingStatusMarriedFilingJointly)
	assert.Greater(t, r.FederalTax, 0.0)
	assert.Greater(t, r.StateTax, 0.0)
	assert.InDelta(t, r.FederalTax+r.StateTax, r.TotalAnnual, 0.01)
	assert.InDelta(t, r.TotalAnnual/12, r.MonthlySaving, 0.01)
}

func TestEstimate_NoIncome(t *testing.T) {
	r := Estimate(0, "NY", FilingStatusSingle)
	assert.Equal(t, 0.0, r.FederalTax)
	assert.Equal(t, 0.0, r.StateTax)
	assert.Equal(t, 0.0, r.TotalAnnual)
	assert.Equal(t, 0.0, r.MonthlySaving)
}

func TestEstimate_NoTaxState(t *testing.T) {
	r := Estimate(80_000, "TX", FilingStatusSingle)
	assert.Greater(t, r.FederalTax, 0.0)
	assert.Equal(t, 0.0, r.StateTax)
}

func TestEstimate_UnknownStateCode(t *testing.T) {
	r := Estimate(80_000, "XX", FilingStatusSingle)
	assert.Equal(t, 0.0, r.StateTax)
	assert.Greater(t, r.FederalTax, 0.0)
}

func TestEstimate_BelowStandardDeduction(t *testing.T) {
	// Income below the $15k standard deduction — no federal tax
	r := Estimate(10_000, "", FilingStatusSingle)
	assert.Equal(t, 0.0, r.FederalTax)
}

func TestComputeStateTax_FlatRate(t *testing.T) {
	// Colorado: 4.4% flat
	tax := ComputeStateTax("CO", 50_000)
	assert.InDelta(t, 2200.0, tax, 0.01)
}

func TestComputeStateTax_Progressive(t *testing.T) {
	// California: $80k income, single (std deduction ~5202)
	tax := ComputeStateTax("CA", 80_000)
	assert.Greater(t, tax, 0.0)
}
