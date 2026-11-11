using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.FixedExpenses;

public sealed class FixedExpenseFlowTests
{
    private static FixedExpenseFields Fields(string? flow) =>
        new("Rent", Money.FromDecimal(1850m), null, null, 1, 1, null, "month", 1, 1, null, 0, flow);

    [Theory]
    [InlineData(TransactionFlow.Spend)]
    [InlineData(TransactionFlow.Transfer)]
    [InlineData(TransactionFlow.Payment)]
    public void BuildEntity_KeepsAnOutgoingFlow(string flow) =>
        Assert.Equal(flow, FixedExpenseNormalization.BuildEntity(Fields(flow)).Flow);

    [Fact]
    public void BuildEntity_NoFlow_LeavesItToTheColumnDefaultOrTheExistingValue() =>
        Assert.Null(FixedExpenseNormalization.BuildEntity(Fields(null)).Flow);

    [Theory]
    [InlineData(TransactionFlow.Income)]
    [InlineData(TransactionFlow.Refund)]
    [InlineData("credit")]
    public void BuildEntity_RejectsFlowsABillCantHave(string flow) =>
        Assert.Throws<AppValidationException>(() => FixedExpenseNormalization.BuildEntity(Fields(flow)));

    [Fact]
    public void BuildEntity_CategoryZero_IsNoCategory() =>
        Assert.Null(FixedExpenseNormalization.BuildEntity(new FixedExpenseFields(
            "Gym", Money.FromDecimal(40m), 0, null, 1, 1, null, "month", 1, 1, null, 0)).CategoryId);
}
