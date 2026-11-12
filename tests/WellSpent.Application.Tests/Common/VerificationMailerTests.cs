using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Common;

public sealed class VerificationMailerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly User _user = new() { Id = Guid.NewGuid(), Email = "a@b.com" };

    private VerificationMailer Mailer(bool enabled) => new(_users, _email,
        Options.Create(new AuthOptions { EmailVerificationEnabled = enabled, FrontendUrl = "http://x" }), NullLogger<VerificationMailer>.Instance);

    [Fact]
    public async Task Disabled_MarksVerified_SendsNothing()
    {
        await Mailer(enabled: false).SendAsync(_user, CancellationToken.None);

        await _users.Received().MarkVerifiedAsync(_user.Id, Arg.Any<CancellationToken>());
        await _email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enabled_SendsLink_LeavesUnverified()
    {
        await Mailer(enabled: true).SendAsync(_user, CancellationToken.None);

        await _email.Received().SendAsync("a@b.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _users.DidNotReceive().MarkVerifiedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
