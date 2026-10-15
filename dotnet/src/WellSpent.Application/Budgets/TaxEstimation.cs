namespace WellSpent.Application.Budgets;

/// <summary>Port of Go's internal/tax package. US federal/state income tax estimation for planning purposes, not tax advice. Based on 2025 figures.</summary>
public static class TaxEstimation
{
    public readonly record struct Result(decimal FederalTax, decimal StateTax, decimal TotalAnnual)
    {
        public decimal MonthlySaving => TotalAnnual / 12;
    }

    private readonly record struct Bracket(decimal UpTo, decimal Rate);

    /// <summary>FilingStatus proto enum values: 1=Single, 2=MarriedFilingJointly, 3=MarriedFilingSeparately, 4=HeadOfHousehold, 5=QualifyingSurvivingSpouse.</summary>
    public static Result Estimate(decimal grossIncome, string? stateCode, int filingStatus)
    {
        var federal = ComputeFederalTax(grossIncome, filingStatus);
        var state = ComputeStateTax(stateCode, grossIncome);
        return new Result(federal, state, federal + state);
    }

    private const decimal Ceiling = 1_000_000_000_000m;

    private static readonly Dictionary<int, Bracket[]> FederalBrackets = new()
    {
        [1] = [new(11925, 0.10m), new(48475, 0.12m), new(103350, 0.22m), new(197300, 0.24m), new(250525, 0.32m), new(626350, 0.35m), new(Ceiling, 0.37m)],
        [2] = [new(23850, 0.10m), new(96950, 0.12m), new(206700, 0.22m), new(394600, 0.24m), new(501050, 0.32m), new(751600, 0.35m), new(Ceiling, 0.37m)],
        [3] = [new(11925, 0.10m), new(48475, 0.12m), new(103350, 0.22m), new(197300, 0.24m), new(250525, 0.32m), new(375800, 0.35m), new(Ceiling, 0.37m)],
        [4] = [new(17000, 0.10m), new(64850, 0.12m), new(103350, 0.22m), new(197300, 0.24m), new(250500, 0.32m), new(626350, 0.35m), new(Ceiling, 0.37m)],
        [5] = [new(23850, 0.10m), new(96950, 0.12m), new(206700, 0.22m), new(394600, 0.24m), new(501050, 0.32m), new(751600, 0.35m), new(Ceiling, 0.37m)],
    };

    private static readonly Dictionary<int, decimal> FederalStandardDeduction = new()
    {
        [1] = 15000, [2] = 30000, [3] = 15000, [4] = 22500, [5] = 30000,
    };

    private static decimal ComputeFederalTax(decimal grossIncome, int filingStatus)
    {
        var brackets = FederalBrackets.GetValueOrDefault(filingStatus, FederalBrackets[1]);
        var deduction = FederalStandardDeduction.GetValueOrDefault(filingStatus, FederalStandardDeduction[1]);
        return ApplyBrackets(grossIncome - deduction, brackets);
    }

    private static decimal ApplyBrackets(decimal taxable, Bracket[] brackets)
    {
        if (taxable <= 0) return 0;
        decimal tax = 0, prev = 0;
        foreach (var b in brackets)
        {
            if (taxable <= prev) break;
            var top = Math.Min(taxable, b.UpTo);
            tax += (top - prev) * b.Rate;
            prev = b.UpTo;
        }
        return tax;
    }

    private static decimal Flat(decimal income, decimal rate) => income <= 0 ? 0 : income * rate;

    /// <summary>Returns 0 for states with no income tax or an unrecognized code.</summary>
    public static decimal ComputeStateTax(string? stateCode, decimal grossIncome)
    {
        if (string.IsNullOrEmpty(stateCode)) return 0;
        return stateCode switch
        {
            "AK" or "FL" or "NV" or "NH" or "SD" or "TN" or "TX" or "WA" or "WY" => 0,

            "CO" => Flat(grossIncome, 0.044m),
            "IL" => Flat(grossIncome, 0.0495m),
            "IN" => Flat(grossIncome, 0.0305m),
            "KY" => Flat(grossIncome, 0.040m),
            "MA" => Flat(grossIncome, 0.050m),
            "MI" => Flat(grossIncome, 0.0425m),
            "NC" => Flat(grossIncome, 0.045m),
            "PA" => Flat(grossIncome, 0.0307m),
            "UT" => Flat(grossIncome, 0.0455m),
            "AZ" => Flat(grossIncome, 0.025m),
            "GA" => Flat(grossIncome, 0.055m),
            "IA" => Flat(grossIncome, 0.038m),
            "ID" => Flat(grossIncome, 0.058m),
            "MT" => Flat(grossIncome, 0.059m),
            "SC" => Flat(grossIncome, 0.064m),

            "AL" => ApplyBrackets(grossIncome - 2500, [new(500, 0.02m), new(2500, 0.04m), new(Ceiling, 0.05m)]),
            "AR" => ApplyBrackets(grossIncome - 2200, [new(4400, 0.02m), new(8800, 0.04m), new(Ceiling, 0.044m)]),
            "CA" => ApplyBrackets(grossIncome - 5202, [new(10412, 0.01m), new(24684, 0.02m), new(38959, 0.04m), new(54081, 0.06m), new(68350, 0.08m), new(349137, 0.093m), new(418961, 0.103m), new(698274, 0.113m), new(Ceiling, 0.123m)]),
            "CT" => ApplyBrackets(grossIncome, [new(10000, 0.02m), new(50000, 0.045m), new(100000, 0.055m), new(200000, 0.06m), new(250000, 0.065m), new(500000, 0.069m), new(Ceiling, 0.0699m)]),
            "DC" => ApplyBrackets(grossIncome, [new(10000, 0.04m), new(40000, 0.06m), new(60000, 0.065m), new(350000, 0.085m), new(1000000, 0.0925m), new(Ceiling, 0.1075m)]),
            "DE" => ApplyBrackets(grossIncome - 3250, [new(2000, 0m), new(5000, 0.022m), new(10000, 0.039m), new(20000, 0.048m), new(25000, 0.052m), new(60000, 0.055m), new(Ceiling, 0.066m)]),
            "HI" => ApplyBrackets(grossIncome - 2200, [new(2400, 0.014m), new(4800, 0.032m), new(9600, 0.055m), new(14400, 0.064m), new(19200, 0.068m), new(24000, 0.072m), new(36000, 0.076m), new(48000, 0.079m), new(150000, 0.0825m), new(175000, 0.09m), new(200000, 0.10m), new(Ceiling, 0.11m)]),
            "KS" => ApplyBrackets(grossIncome - 3500, [new(15000, 0.031m), new(30000, 0.0525m), new(Ceiling, 0.057m)]),
            "LA" => ApplyBrackets(grossIncome, [new(12500, 0.0185m), new(50000, 0.035m), new(Ceiling, 0.0425m)]),
            "ME" => ApplyBrackets(grossIncome - 14600, [new(24500, 0.058m), new(58050, 0.0675m), new(Ceiling, 0.0715m)]),
            "MD" => ApplyBrackets(grossIncome - 2400, [new(1000, 0.02m), new(2000, 0.03m), new(3000, 0.04m), new(100000, 0.0475m), new(125000, 0.05m), new(150000, 0.0525m), new(250000, 0.055m), new(Ceiling, 0.0575m)]),
            "MN" => ApplyBrackets(grossIncome - 14575, [new(30070, 0.0535m), new(98760, 0.068m), new(183340, 0.0785m), new(Ceiling, 0.0985m)]),
            "MO" => ApplyBrackets(grossIncome - 14600, [new(1207, 0m), new(2414, 0.015m), new(3621, 0.02m), new(4828, 0.025m), new(6035, 0.03m), new(7242, 0.035m), new(8432, 0.04m), new(9682, 0.045m), new(Ceiling, 0.048m)]),
            "MS" => ApplyBrackets(grossIncome - 2300, [new(10000, 0m), new(Ceiling, 0.047m)]),
            "NE" => ApplyBrackets(grossIncome - 7900, [new(3700, 0.0246m), new(22170, 0.0351m), new(35730, 0.0501m), new(Ceiling, 0.0664m)]),
            "NJ" => ApplyBrackets(grossIncome, [new(20000, 0.014m), new(35000, 0.0175m), new(40000, 0.035m), new(75000, 0.05526m), new(500000, 0.0637m), new(1000000, 0.0897m), new(Ceiling, 0.1075m)]),
            "NM" => ApplyBrackets(grossIncome - 14600, [new(5500, 0.017m), new(11000, 0.032m), new(16000, 0.047m), new(210000, 0.049m), new(Ceiling, 0.059m)]),
            "NY" => ApplyBrackets(grossIncome - 8000, [new(17150, 0.04m), new(23600, 0.045m), new(27900, 0.0525m), new(161550, 0.055m), new(323200, 0.06m), new(2155350, 0.0685m), new(5000000, 0.0965m), new(25000000, 0.103m), new(Ceiling, 0.109m)]),
            "OH" => ApplyBrackets(grossIncome, [new(26050, 0m), new(100000, 0.0275m), new(Ceiling, 0.035m)]),
            "OK" => ApplyBrackets(grossIncome - 6350, [new(1000, 0.0025m), new(2500, 0.0075m), new(3750, 0.0175m), new(4900, 0.0275m), new(7200, 0.0375m), new(Ceiling, 0.0475m)]),
            "OR" => ApplyBrackets(grossIncome - 2420, [new(18400, 0.0475m), new(46200, 0.0675m), new(250000, 0.0875m), new(Ceiling, 0.099m)]),
            "RI" => ApplyBrackets(grossIncome - 10550, [new(77450, 0.0375m), new(176050, 0.0475m), new(Ceiling, 0.0599m)]),
            "VA" => ApplyBrackets(grossIncome - 4500, [new(3000, 0.02m), new(5000, 0.03m), new(17000, 0.05m), new(Ceiling, 0.0575m)]),
            "VT" => ApplyBrackets(grossIncome - 4600, [new(45400, 0.0335m), new(110050, 0.066m), new(229550, 0.076m), new(Ceiling, 0.0875m)]),
            "WI" => ApplyBrackets(grossIncome - 13000, [new(14320, 0.035m), new(28640, 0.044m), new(315310, 0.053m), new(Ceiling, 0.0765m)]),
            "WV" => ApplyBrackets(grossIncome, [new(10000, 0.03m), new(25000, 0.04m), new(40000, 0.045m), new(60000, 0.06m), new(Ceiling, 0.065m)]),

            _ => 0,
        };
    }
}
