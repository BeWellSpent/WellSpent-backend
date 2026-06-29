// Package tax provides US federal and state income tax estimation for the
// income-complexity feature. Calculations are based on 2025 tax year figures
// and are intended as planning estimates, not tax advice.
package tax

// FilingStatus mirrors the proto FilingStatus enum values.
type FilingStatus int32

const (
	FilingStatusUnspecified         FilingStatus = 0
	FilingStatusSingle              FilingStatus = 1
	FilingStatusMarriedFilingJointly FilingStatus = 2
	FilingStatusMarriedFilingSeparately FilingStatus = 3
	FilingStatusHeadOfHousehold     FilingStatus = 4
	FilingStatusQualifyingSurvivingSpouse FilingStatus = 5
)

// Result holds the annual tax estimate broken down by source.
type Result struct {
	FederalTax    float64
	StateTax      float64
	TotalAnnual   float64
	MonthlySaving float64 // TotalAnnual / 12
}

// Estimate computes an annual US income tax estimate and the monthly savings
// amount needed to cover it (annual ÷ 12).
//
// grossIncome: combined annual gross income in whole dollars
// stateCode:   two-letter US state code (e.g. "CA") — empty for federal-only
// status:      filing status from the proto enum
func Estimate(grossIncome float64, stateCode string, status FilingStatus) Result {
	federal := computeFederalTax(grossIncome, status)
	state := ComputeStateTax(stateCode, grossIncome)
	total := federal + state
	return Result{
		FederalTax:    federal,
		StateTax:      state,
		TotalAnnual:   total,
		MonthlySaving: total / 12,
	}
}

// ── 2025 federal brackets ────────────────────────────────────────────────────

var federalBrackets = map[FilingStatus][]bracket{
	FilingStatusSingle: {
		{11925, 0.10}, {48475, 0.12}, {103350, 0.22}, {197300, 0.24},
		{250525, 0.32}, {626350, 0.35}, {1e12, 0.37},
	},
	FilingStatusMarriedFilingJointly: {
		{23850, 0.10}, {96950, 0.12}, {206700, 0.22}, {394600, 0.24},
		{501050, 0.32}, {751600, 0.35}, {1e12, 0.37},
	},
	FilingStatusMarriedFilingSeparately: {
		{11925, 0.10}, {48475, 0.12}, {103350, 0.22}, {197300, 0.24},
		{250525, 0.32}, {375800, 0.35}, {1e12, 0.37},
	},
	FilingStatusHeadOfHousehold: {
		{17000, 0.10}, {64850, 0.12}, {103350, 0.22}, {197300, 0.24},
		{250500, 0.32}, {626350, 0.35}, {1e12, 0.37},
	},
	FilingStatusQualifyingSurvivingSpouse: {
		{23850, 0.10}, {96950, 0.12}, {206700, 0.22}, {394600, 0.24},
		{501050, 0.32}, {751600, 0.35}, {1e12, 0.37},
	},
}

var federalStandardDeduction = map[FilingStatus]float64{
	FilingStatusSingle:                   15000,
	FilingStatusMarriedFilingJointly:     30000,
	FilingStatusMarriedFilingSeparately:  15000,
	FilingStatusHeadOfHousehold:          22500,
	FilingStatusQualifyingSurvivingSpouse: 30000,
}

func computeFederalTax(grossIncome float64, status FilingStatus) float64 {
	brackets, ok := federalBrackets[status]
	if !ok {
		brackets = federalBrackets[FilingStatusSingle]
	}
	deduction, ok := federalStandardDeduction[status]
	if !ok {
		deduction = federalStandardDeduction[FilingStatusSingle]
	}
	taxable := grossIncome - deduction
	if taxable <= 0 {
		return 0
	}
	var tax float64
	prev := 0.0
	for _, b := range brackets {
		if taxable <= prev {
			break
		}
		top := min64(taxable, b.upTo)
		tax += (top - prev) * b.rate
		prev = b.upTo
	}
	return tax
}
