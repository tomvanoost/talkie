using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;
using static Talkie.Api.Infrastructure.EndpointExtensions;

namespace Talkie.Api.Modules;

/// <summary>Login, company sign-up and the current user's profile.</summary>
public sealed class AuthModule : IModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var anon = app.MapGroup("/api/auth").WithTags("Auth");
        anon.MapPost("/login", Login);
        anon.MapPost("/companies", CreateCompany);
        anon.MapGet("/settings", async (TalkieDbContext db, CancellationToken ct) =>
        {
            var s = await db.AppSettings.AsNoTracking().FirstAsync(ct);
            return new PublicSettingsDto(s.AppName, s.Announcement, s.AllowCompanyCreation);
        });

        var account = app.MapGroup("/api/account").WithTags("Account").RequireTalkieUser();
        account.MapGet("/me", async (CurrentUser current, TalkieDbContext db, CancellationToken ct) =>
            (await LoadProfile(db, current.Id, ct)).ToProfile());
        account.MapPut("/profile", UpdateProfile);
        account.MapPut("/password", ChangePassword);
        account.MapPost("/avatar", UploadAvatar).DisableAntiforgery();
        account.MapDelete("/avatar", async (CurrentUser current, TalkieDbContext db, CancellationToken ct) =>
        {
            var user = await current.GetAsync(ct);
            await db.UserAvatars.Where(a => a.UserId == user.Id).ExecuteDeleteAsync(ct);
            user.AvatarContentType = null;
            user.AvatarVersion++;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        account.MapPost("/heartbeat", Heartbeat);

        var users = app.MapGroup("/api/users").WithTags("Users");
        // Anonymous so <img src> works without a bearer token; avatars are not secret.
        users.MapGet("/{id:guid}/avatar", async (Guid id, TalkieDbContext db, CancellationToken ct) =>
        {
            var avatar = await db.UserAvatars.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == id, ct);
            return avatar is null ? Results.NotFound() : Results.File(avatar.Data, avatar.ContentType);
        });
        users.MapGet("/search", SearchUsers).RequireAuthorization();
    }

    private static async Task<IResult> Login(LoginRequest request, TalkieDbContext db, TokenService tokens, CancellationToken ct)
    {
        var email = Validation.NormalizeEmail(request.Email ?? "");
        var user = await db.Users.Include(u => u.Company).Include(u => u.Roles).ThenInclude(r => r.Role)
            .FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || string.IsNullOrEmpty(request.Password) ||
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            return BadRequest("Invalid e-mail or password.");
        }
        return Results.Ok(new AuthResponse(tokens.CreateToken(user), user.ToProfile()));
    }

    private static async Task<IResult> CreateCompany(
        CreateCompanyRequest request, TalkieDbContext db, TokenService tokens, EmailSender email, CancellationToken ct)
    {
        var settings = await db.AppSettings.FirstAsync(ct);
        if (!settings.AllowCompanyCreation) return BadRequest("Creating new companies is currently disabled.");

        var name = request.CompanyName?.Trim() ?? "";
        if (name.Length is < 2 or > 100) return BadRequest("Company name must be between 2 and 100 characters.");
        if (string.Equals(name, TalkieConstants.DefaultCompanyName, StringComparison.OrdinalIgnoreCase))
            return BadRequest("The name Talkie is reserved.");
        if (await db.Companies.AnyAsync(c => c.Name == name, ct)) return BadRequest("A company with this name already exists.");

        if (!Validation.IsValidEmail(request.OwnerEmail?.Trim())) return BadRequest("Please enter a valid owner e-mail.");
        var ownerEmail = Validation.NormalizeEmail(request.OwnerEmail!);
        if (await db.Users.AnyAsync(u => u.Email == ownerEmail, ct)) return BadRequest("This e-mail is already in use.");
        if (Validation.PasswordProblem(request.OwnerPassword) is { } pwProblem) return BadRequest(pwProblem);

        var username = request.Username?.Trim() ?? "";
        var displayName = request.DisplayName?.Trim() ?? "";
        if (username.Length is < 2 or > 50) return BadRequest("Username must be between 2 and 50 characters.");
        if (displayName.Length is < 1 or > 100) return BadRequest("Display name is required.");

        var roles = request.Roles ?? [];
        if (roles.Any(r => string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 50 || !Validation.IsValidColor(r.Color)))
            return BadRequest("Every role needs a name (max 50 characters) and a color like #90CAF9.");
        if (roles.Select(r => r.Name.Trim().ToLowerInvariant()).Distinct().Count() != roles.Count)
            return BadRequest("Role names must be unique.");

        var company = new Company { Name = name };
        company.Roles.AddRange(roles.Select(r => new Role { Name = r.Name.Trim(), Color = r.Color }));

        var owner = new User
        {
            Email = ownerEmail,
            Username = username,
            DisplayName = displayName,
            IsOwner = true,
            Company = company,
            Money = settings.StartingMoney,
            Shards = settings.StartingShards,
        };
        owner.PasswordHash = new PasswordHasher<User>().HashPassword(owner, request.OwnerPassword);

        db.Companies.Add(company);
        db.Users.Add(owner);
        db.Servers.Add(TalkieSeeder.CreateDefaultServer(company));
        await db.SaveChangesAsync(ct);

        await email.SendAsync(ownerEmail, $"Welcome to Talkie, {displayName}!",
            $"<h2>Your company {System.Net.WebUtility.HtmlEncode(name)} is ready</h2><p>You can now log in with <b>{ownerEmail}</b>.</p>", ct);

        return Results.Ok(new AuthResponse(tokens.CreateToken(owner), owner.ToProfile()));
    }

    internal static Task<User> LoadProfile(TalkieDbContext db, Guid id, CancellationToken ct) =>
        db.Users.Include(u => u.Company).Include(u => u.Roles).ThenInclude(r => r.Role).FirstAsync(u => u.Id == id, ct);

    private static async Task<IResult> UpdateProfile(UpdateProfileRequest request, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var user = await LoadProfile(db, current.Id, ct);
        var username = request.Username?.Trim() ?? "";
        var displayName = request.DisplayName?.Trim() ?? "";
        if (username.Length is < 2 or > 50) return BadRequest("Username must be between 2 and 50 characters.");
        if (displayName.Length is < 1 or > 100) return BadRequest("Display name is required.");
        user.Username = username;
        user.DisplayName = displayName;
        user.FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim();
        user.LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim();
        await db.SaveChangesAsync(ct);
        return Results.Ok(user.ToProfile());
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var user = await current.GetAsync(ct);
        var hasher = new PasswordHasher<User>();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
            return BadRequest("Your current password is incorrect.");
        if (Validation.PasswordProblem(request.NewPassword) is { } problem) return BadRequest(problem);
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UploadAvatar(IFormFile file, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > TalkieConstants.MaxAvatarBytes) return BadRequest("Avatar must be an image up to 2 MB.");
        if (!Mapping.IsImage(file.ContentType)) return BadRequest("Only images can be used as a profile picture.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var user = await LoadProfile(db, current.Id, ct);
        var avatar = await db.UserAvatars.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (avatar is null)
        {
            avatar = new UserAvatar { UserId = user.Id, ContentType = file.ContentType };
            db.UserAvatars.Add(avatar);
        }
        avatar.Data = ms.ToArray();
        avatar.ContentType = file.ContentType;
        user.AvatarContentType = file.ContentType;
        user.AvatarVersion++;
        await db.SaveChangesAsync(ct);
        return Results.Ok(user.ToProfile());
    }

    /// <summary>The client pings every minute while the app is open; this is what "Playtime" measures.</summary>
    private static async Task<IResult> Heartbeat(CurrentUser current, TalkieDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var user = await current.GetAsync(ct);
        var now = clock.GetUtcNow();
        if (user.LastHeartbeatAt is { } last)
        {
            var elapsed = (now - last).TotalSeconds;
            // Only count continuous sessions; a gap longer than two minutes means the app was closed.
            if (elapsed is > 0 and <= 120) user.PlaytimeSeconds += (long)Math.Round(elapsed);
        }
        user.LastHeartbeatAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(user.PlaytimeSeconds);
    }

    private static async Task<List<UserSummaryDto>> SearchUsers(string? q, TalkieDbContext db, CancellationToken ct)
    {
        var term = q?.Trim().ToLowerInvariant() ?? "";
        if (term.Length < 1) return [];
        var users = await db.Users.Include(u => u.Company).AsNoTracking()
            .Where(u => u.Email.Contains(term) || u.DisplayName.Contains(term) || u.Username.Contains(term))
            .OrderBy(u => u.Email).Take(10).ToListAsync(ct);
        return users.Select(u => u.ToSummary()).ToList();
    }
}
