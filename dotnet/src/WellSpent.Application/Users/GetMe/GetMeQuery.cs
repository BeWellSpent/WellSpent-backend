using AutoMapper;
using MediatR;
using WellSpent.Application.Abstractions;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Users.GetMe;

public sealed record GetMeQuery(Guid UserId) : IRequest<UserDto>;

public sealed class GetMeQueryHandler(IUserRepository users, IMapper mapper) : IRequestHandler<GetMeQuery, UserDto>
{
    public async Task<UserDto> Handle(GetMeQuery request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct);
        return mapper.Map<UserDto>(user);
    }
}
