package tax

// stateTax computes state income tax for a given gross income (USD) and state code.
// Returns 0 for states with no income tax.
// Calculations are approximations based on 2024/2025 rates; no state standard deductions
// are applied beyond what is noted per-state. Intended as a planning estimate only.
var stateTax = map[string]func(income float64) float64{
	// ── No income tax ──────────────────────────────────────────────────────────
	"AK": flat(0), "FL": flat(0), "NV": flat(0), "NH": flat(0),
	"SD": flat(0), "TN": flat(0), "TX": flat(0), "WA": flat(0), "WY": flat(0),

	// ── Flat rate states ───────────────────────────────────────────────────────
	"CO": flat(0.044),
	"IL": flat(0.0495),
	"IN": flat(0.0305),
	"KY": flat(0.040),
	"MA": flat(0.050),
	"MI": flat(0.0425),
	"NC": flat(0.045),
	"PA": flat(0.0307),
	"UT": flat(0.0455),

	// ── Progressive states ─────────────────────────────────────────────────────
	"AL": progressive([]bracket{{500, 0.02}, {2500, 0.04}, {1e12, 0.05}}, 2500),
	"AR": progressive([]bracket{{4400, 0.02}, {8800, 0.04}, {1e12, 0.044}}, 2200),
	"AZ": flat(0.025),
	"CA": progressive([]bracket{
		{10412, 0.01}, {24684, 0.02}, {38959, 0.04}, {54081, 0.06},
		{68350, 0.08}, {349137, 0.093}, {418961, 0.103}, {698274, 0.113}, {1e12, 0.123},
	}, 5202),
	"CT": progressive([]bracket{
		{10000, 0.02}, {50000, 0.045}, {100000, 0.055}, {200000, 0.06},
		{250000, 0.065}, {500000, 0.069}, {1e12, 0.0699},
	}, 0),
	"DC": progressive([]bracket{
		{10000, 0.04}, {40000, 0.06}, {60000, 0.065}, {350000, 0.085},
		{1000000, 0.0925}, {1e12, 0.1075},
	}, 0),
	"DE": progressive([]bracket{
		{2000, 0}, {5000, 0.022}, {10000, 0.039}, {20000, 0.048},
		{25000, 0.052}, {60000, 0.055}, {1e12, 0.066},
	}, 3250),
	"GA": flat(0.055),
	"HI": progressive([]bracket{
		{2400, 0.014}, {4800, 0.032}, {9600, 0.055}, {14400, 0.064},
		{19200, 0.068}, {24000, 0.072}, {36000, 0.076}, {48000, 0.079}, {150000, 0.0825},
		{175000, 0.09}, {200000, 0.10}, {1e12, 0.11},
	}, 2200),
	"IA": flat(0.038),
	"ID": flat(0.058),
	"KS": progressive([]bracket{{15000, 0.031}, {30000, 0.0525}, {1e12, 0.057}}, 3500),
	"LA": progressive([]bracket{{12500, 0.0185}, {50000, 0.035}, {1e12, 0.0425}}, 0),
	"ME": progressive([]bracket{{24500, 0.058}, {58050, 0.0675}, {1e12, 0.0715}}, 14600),
	"MD": progressive([]bracket{
		{1000, 0.02}, {2000, 0.03}, {3000, 0.04}, {100000, 0.0475},
		{125000, 0.05}, {150000, 0.0525}, {250000, 0.055}, {1e12, 0.0575},
	}, 2400),
	"MN": progressive([]bracket{
		{30070, 0.0535}, {98760, 0.068}, {183340, 0.0785}, {1e12, 0.0985},
	}, 14575),
	"MO": progressive([]bracket{
		{1207, 0}, {2414, 0.015}, {3621, 0.02}, {4828, 0.025},
		{6035, 0.03}, {7242, 0.035}, {8432, 0.04}, {9682, 0.045}, {1e12, 0.048},
	}, 14600),
	"MS": progressive([]bracket{{10000, 0}, {1e12, 0.047}}, 2300),
	"MT": flat(0.059),
	"NE": progressive([]bracket{
		{3700, 0.0246}, {22170, 0.0351}, {35730, 0.0501}, {1e12, 0.0664},
	}, 7900),
	"NJ": progressive([]bracket{
		{20000, 0.014}, {35000, 0.0175}, {40000, 0.035}, {75000, 0.05526},
		{500000, 0.0637}, {1000000, 0.0897}, {1e12, 0.1075},
	}, 0),
	"NM": progressive([]bracket{
		{5500, 0.017}, {11000, 0.032}, {16000, 0.047}, {210000, 0.049}, {1e12, 0.059},
	}, 14600),
	"NY": progressive([]bracket{
		{17150, 0.04}, {23600, 0.045}, {27900, 0.0525}, {161550, 0.055},
		{323200, 0.06}, {2155350, 0.0685}, {5000000, 0.0965}, {25000000, 0.103}, {1e12, 0.109},
	}, 8000),
	"OH": progressive([]bracket{{26050, 0}, {100000, 0.0275}, {1e12, 0.035}}, 0),
	"OK": progressive([]bracket{
		{1000, 0.0025}, {2500, 0.0075}, {3750, 0.0175}, {4900, 0.0275}, {7200, 0.0375}, {1e12, 0.0475},
	}, 6350),
	"OR": progressive([]bracket{
		{18400, 0.0475}, {46200, 0.0675}, {250000, 0.0875}, {1e12, 0.099},
	}, 2420),
	"RI": progressive([]bracket{{77450, 0.0375}, {176050, 0.0475}, {1e12, 0.0599}}, 10550),
	"SC": flat(0.064),
	"VA": progressive([]bracket{{3000, 0.02}, {5000, 0.03}, {17000, 0.05}, {1e12, 0.0575}}, 4500),
	"VT": progressive([]bracket{
		{45400, 0.0335}, {110050, 0.066}, {229550, 0.076}, {1e12, 0.0875},
	}, 4600),
	"WI": progressive([]bracket{
		{14320, 0.035}, {28640, 0.044}, {315310, 0.053}, {1e12, 0.0765},
	}, 13000),
	"WV": progressive([]bracket{
		{10000, 0.03}, {25000, 0.04}, {40000, 0.045}, {60000, 0.06}, {1e12, 0.065},
	}, 0),
}

// ComputeStateTax returns the estimated annual state income tax for a US resident.
// Returns 0 for unknown state codes.
func ComputeStateTax(stateCode string, grossIncome float64) float64 {
	fn, ok := stateTax[stateCode]
	if !ok {
		return 0
	}
	return fn(grossIncome)
}

// ── helpers ──────────────────────────────────────────────────────────────────

type bracket struct {
	upTo float64
	rate float64
}

func flat(rate float64) func(float64) float64 {
	return func(income float64) float64 {
		if income <= 0 {
			return 0
		}
		return income * rate
	}
}

func progressive(brackets []bracket, standardDeduction float64) func(float64) float64 {
	return func(income float64) float64 {
		taxable := income - standardDeduction
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
}

func min64(a, b float64) float64 {
	if a < b {
		return a
	}
	return b
}
