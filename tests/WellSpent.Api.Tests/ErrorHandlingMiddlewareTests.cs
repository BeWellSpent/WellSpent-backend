using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using WellSpent.Api.Errors;
using Xunit;

namespace WellSpent.Api.Tests;

public sealed class ErrorHandlingMiddlewareTests
{
    private static async Task<int> Run(Exception ex, bool clientAborted)
    {
        var aborted = new CancellationTokenSource();
        if (clientAborted)
        {
            aborted.Cancel();
        }
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        context.Response.Body = new MemoryStream();
        await new ErrorHandlingMiddleware(_ => throw ex, NullLogger<ErrorHandlingMiddleware>.Instance).InvokeAsync(context);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task CancelledByClient_Is499_NotA500() =>
        Assert.Equal(499, await Run(new TaskCanceledException(), clientAborted: true));

    [Fact]
    public async Task CancellationWithoutClientAbort_StillA500() =>
        Assert.Equal(500, await Run(new TaskCanceledException(), clientAborted: false));
}
