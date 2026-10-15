using MediatR;
using WellSpent.Application.Abstractions;

namespace WellSpent.Application.Auth.GoogleAuthUrl;

public sealed record GetGoogleAuthUrlQuery(string State) : IRequest<string>;

public sealed class GetGoogleAuthUrlQueryHandler(IGoogleOAuthClient google) : IRequestHandler<GetGoogleAuthUrlQuery, string>
{
    public Task<string> Handle(GetGoogleAuthUrlQuery request, CancellationToken ct) =>
        Task.FromResult(google.GetAuthUrl(request.State));
}
