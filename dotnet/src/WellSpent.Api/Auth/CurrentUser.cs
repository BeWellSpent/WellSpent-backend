namespace WellSpent.Api.Auth;

/// <summary>Reads the authenticated user's id from the "sub" claim — present verbatim because JwtBearer is configured with MapInboundClaims=false (see Program.cs). Only called behind .RequireAuthorization(), so the claim is always present.</summary>
public static class CurrentUser
{
    public static Guid GetId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("missing sub claim");
        return Guid.Parse(sub);
    }
}
