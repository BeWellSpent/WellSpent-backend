using WellSpent.Application.Transactions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Transactions;

public sealed class TransactionRulesTests
{
    private static readonly BudgetPeriod Period = new()
    {
        Id = Guid.NewGuid(), BudgetProfileId = Guid.NewGuid(),
        StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28),
    };

    [Fact]
    public void AssertNotBackdated_VariableBeforeStart_Throws()
    {
        Assert.Throws<AppValidationException>(() =>
            TransactionRules.AssertNotBackdated(2, new DateOnly(2026, 1, 31), Period));
    }

    [Fact]
    public void AssertNotBackdated_VariableOnOrAfterStart_Allowed()
    {
        TransactionRules.AssertNotBackdated(2, new DateOnly(2026, 2, 1), Period);
    }

    [Fact]
    public void AssertNotBackdated_FixedExempt_EvenBeforeStart()
    {
        TransactionRules.AssertNotBackdated(1, new DateOnly(2020, 1, 1), Period);
    }

    [Fact]
    public void AssertNotBackdated_NullDate_Allowed()
    {
        TransactionRules.AssertNotBackdated(2, null, Period);
    }

    private static readonly Guid SharedPaymentMethodId = Guid.NewGuid();

    private static Transaction BaseTransaction(decimal amount = 100m, int? categoryId = null) => new()
    {
        Id = Guid.NewGuid(), Name = "Rent", Amount = amount, PlannedAmount = 100m,
        Date = new DateOnly(2026, 2, 5), PaymentMethodId = SharedPaymentMethodId,
        TransactionFrequencyId = 4, TransactionTypeId = 1, CategoryId = categoryId,
    };

    [Fact]
    public void AssertOnlyCategoryChanged_CategoryOnly_Allowed()
    {
        var existing = BaseTransaction(categoryId: 1);
        var edit = BaseTransaction(categoryId: 99);
        TransactionRules.AssertOnlyCategoryChanged(edit, existing);
    }

    [Fact]
    public void AssertOnlyCategoryChanged_AmountChanged_Throws()
    {
        var existing = BaseTransaction(amount: 100m);
        var edit = BaseTransaction(amount: 200m);
        Assert.Throws<AppValidationException>(() => TransactionRules.AssertOnlyCategoryChanged(edit, existing));
    }

    [Fact]
    public void AssertOnlyCategoryChanged_DifferentDecimalScale_StillEqual()
    {
        // 10.50m and 10.5m must compare equal — this is exactly the case Go
        // needed Float64Value() for; C#'s decimal handles it natively.
        var existing = BaseTransaction(amount: 10.50m);
        var edit = BaseTransaction(amount: 10.5m);
        TransactionRules.AssertOnlyCategoryChanged(edit, existing);
    }
}
