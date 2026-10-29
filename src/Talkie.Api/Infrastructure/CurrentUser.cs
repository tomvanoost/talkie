using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Talkie.Data;
using Talkie.Shared;

namespace Talkie.Api.Infrastructure;

/// <summary>Resolves the authenticated user for the current request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor, TalkieDbContext db)
{
    private User? _user;

    public Guid Id
    {
        get
        {
            var sub = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? accessor.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
        }
    }

    public async Task<User> GetAsync(CancellationToken ct = default)
    {
        _user ??= await db.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == Id, ct)
            ?? throw new UnauthorizedAccessException();
        return _user;
    }

    public async Task<bool> ExistsAsync(CancellationToken ct = default) =>
        Id != Guid.Empty && await db.Users.AnyAsync(u => u.Id == Id, ct);

    public static bool IsAdmin(User user) =>
        string.Equals(user.Email, TalkieConstants.AdminEmail, StringComparison.OrdinalIgnoreCase);

    /// <summary>The company owner and the app admin (for their own company) can manage members, roles and channels.</summary>
    public static bool CanManageCompany(User user, Guid companyId) =>
        user.CompanyId == companyId && (user.IsOwner || IsAdmin(user));
}

public static class EndpointExtensions
{
    /// <summary>
    /// Requires a valid token whose user still exists. Deleted members are rejected immediately, even with an unexpired token.
    /// </summary>
    public static RouteGroupBuilder RequireTalkieUser(this RouteGroupBuilder group)
    {
        group.RequireAuthorization();
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var current = ctx.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
            if (!await current.ExistsAsync(ctx.HttpContext.RequestAborted))
            {
                return Results.Unauthorized();
            }
            return await next(ctx);
        });
        return group;
    }

    public static IResult BadRequest(string message) => Results.BadRequest(new ApiError(message));
    public static IResult NotFound(string message = "Not found.") => Results.NotFound(new ApiError(message));
    public static IResult Forbidden(string message = "You are not allowed to do this.") =>
        Results.Json(new ApiError(message), statusCode: StatusCodes.Status403Forbidden);
}
