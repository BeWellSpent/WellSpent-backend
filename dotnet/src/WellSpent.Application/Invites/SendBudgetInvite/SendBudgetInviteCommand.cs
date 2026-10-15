using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Invites.SendBudgetInvite;

/// <summary>BudgetPersonId null/0 means "create a new BudgetPerson on acceptance" rather than linking an existing placeholder.</summary>
public sealed record SendBudgetInviteCommand(Guid CallerId, Guid BudgetProfileId, string Email, string Role, int? BudgetPersonId)
    : IRequest<BudgetInviteDto>;

public sealed class SendBudgetInviteCommandHandler(
    IInviteRepository invites,
    IBudgetProfileRepository profiles,
    BudgetAccessGuard access,
    IEmailSender emailSender,
    IOptions<AuthOptions> options,
    ILogger<SendBudgetInviteCommandHandler> logger) : IRequestHandler<SendBudgetInviteCommand, BudgetInviteDto>
{
    public async Task<BudgetInviteDto> Handle(SendBudgetInviteCommand request, CancellationToken ct)
    {
        // Roles offered are collaborator and viewer, never admin — matching
        // the setup flow's own invite panels (an admin can remove whoever
        // invited them, which isn't a dropdown decision mid-setup).
        if (request.Role is not ("collaborator" or "viewer"))
        {
            throw new AppValidationException("role must be collaborator or viewer");
        }

        var profile = await access.EnsureAdminAsync(request.BudgetProfileId, request.CallerId, ct);

        // If a person is linked, verify they belong to this budget.
        long? personId = null;
        if (request.BudgetPersonId is { } pid && pid != 0)
        {
            await profiles.GetPersonAsync(pid, request.BudgetProfileId, ct);
            personId = pid;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var invite = await invites.CreateAsync(new BudgetInvite
        {
            BudgetProfileId = request.BudgetProfileId,
            Email = email,
            Role = request.Role,
            InvitedBy = request.CallerId,
            BudgetPersonId = personId,
            Status = "pending",
            ExpiresAt = DateTime.UtcNow.Add(InviteDisplay.Ttl),
        }, ct);

        // Best-effort — the invite is already persisted; a failed send just
        // means the recipient never got the email (no automatic resend path
        // on this RPC, same as Go).
        try
        {
            var link = $"{options.Value.FrontendUrl.TrimEnd('/')}/en/invite/{invite.Token}";
            var html = $"""
                <p>You've been invited to collaborate on the <strong>{profile.Name}</strong> budget in WellSpent.</p>
                <p><a href="{link}" style="display:inline-block;padding:10px 20px;background:#1976d2;color:#fff;text-decoration:none;border-radius:4px;">Accept invitation</a></p>
                <p>If the button above doesn't work, copy and paste this link into your browser:</p>
                <p>{link}</p>
                <p>This link expires in 7 days.</p>
                """;
            await emailSender.SendAsync(email, $"You're invited to the {profile.Name} budget", html, ct);
            logger.LogInformation("invite.email.sent to={To}", email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "invite.email.failed to={To}", email);
        }

        // budgetName/inviterName are deliberately empty here — mirrors Go's
        // handler exactly (toProtoInvite(inv, "", "")). Only GetBudgetInvite,
        // the public preview page, populates them; the caller here already
        // knows their own budget's name.
        return InviteDisplay.ToDto(invite, "", "");
    }
}
