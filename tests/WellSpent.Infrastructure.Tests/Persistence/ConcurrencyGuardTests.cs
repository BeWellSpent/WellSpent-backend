using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Exceptions;
using WellSpent.Infrastructure.Persistence;

namespace WellSpent.Infrastructure.Tests.Persistence;

public sealed class ConcurrencyGuardTests
{
    [Fact]
    public async Task RunAsync_TranslatesDbUpdateConcurrencyException_ToConflictException()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            ConcurrencyGuard.RunAsync(() => throw new DbUpdateConcurrencyException("conflict")));

        Assert.IsType<DbUpdateConcurrencyException>(ex.InnerException);
    }

    [Fact]
    public async Task RunAsync_LeavesOtherExceptionsUntouched()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConcurrencyGuard.RunAsync(() => throw new InvalidOperationException("unrelated")));
    }

    [Fact]
    public async Task RunAsync_NoException_CompletesNormally()
    {
        var ran = false;
        await ConcurrencyGuard.RunAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.True(ran);
    }
}
