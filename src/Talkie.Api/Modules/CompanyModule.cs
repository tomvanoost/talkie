using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;
using static Talkie.Api.Infrastructure.EndpointExtensions;

namespace Talkie.Api.Modules;

/// <summary>Members and roles of the current user's company.</summary>
public sealed class CompanyModule : IModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/company").WithTags("Company").RequireTalkieUser();
        g.MapGet("/", GetCompany);
        g.MapGet("/members", GetMembers);
        g.MapPost("/members", AddMember);
        g.MapPut("/members/{id:guid}", UpdateMember);
        g.MapDelete("/members/{id:guid}", DeleteMember);
        g.MapPost("/roles", CreateRole);
        g.MapPut("/roles/{id:guid}", UpdateRole);
        g.MapDelete("/roles/{id:guid}", DeleteRole);
    }

    private static async Task<CompanyDto> GetCompany(CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var roles = await db.Roles.Where(r => r.CompanyId == me.CompanyId).OrderBy(r => r.Name).ToListAsync(ct);
        var count = await db.Users.CountAsync(u => u.CompanyId == me.CompanyId, ct);
        return new CompanyDto(me.CompanyId, me.Company!.Name, count, roles.Select(r => r.ToDto()).ToList());
    }

    private static async Task<List<MemberDto>> GetMembers(CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var members = await db.Users.AsNoTracking().Include(u => u.Roles).ThenInclude(r => r.Role)
            .Where(u => u.CompanyId == me.CompanyId)
            .OrderByDescending(u => u.IsOwner).ThenBy(u => u.DisplayName)
            .ToListAsync(ct);
        return members.Select(m => m.ToMember()).ToList();
    }

    private static async Task<IResult> AddMember(
        AddMemberRequest request, CurrentUser current, TalkieDbContext db, EmailSender email, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        if (!CurrentUser.CanManageCompany(me, me.CompanyId)) return Forbidden("Only the company owner can add members.");

        var first = request.FirstName?.Trim() ?? "";
        var last = request.LastName?.Trim() ?? "";
        if (first.Length is < 1 or > 100 || last.Length is < 1 or > 100) return BadRequest("Name and surname are required.");
        if (!Validation.IsValidEmail(request.Email?.Trim())) return BadRequest("Please enter a valid e-mail.");
        var mail = Validation.NormalizeEmail(request.Email!);
        if (await db.Users.AnyAsync(u => u.Email == mail, ct)) return BadRequest("This e-mail is already in use.");

        var roles = await ValidRoles(db, me.CompanyId, request.RoleIds, ct);
        if (roles is null) return BadRequest("One or more roles do not exist.");

        var settings = await db.AppSettings.FirstAsync(ct);
        var password = GeneratePassword();
        var user = new User
        {
            CompanyId = me.CompanyId,
            Email = mail,
            FirstName = first,
            LastName = last,
            DisplayName = $"{first} {last}",
            Username = mail.Split('@')[0],
            Money = settings.StartingMoney,
            Shards = settings.StartingShards,
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        user.Roles.AddRange(roles.Select(r => new UserRole { RoleId = r.Id, Role = r }));
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        await email.SendAsync(mail, $"You have been added to {me.Company!.Name} on Talkie",
            $"<h2>Welcome to {System.Net.WebUtility.HtmlEncode(me.Company.Name)}!</h2>" +
            $"<p>{System.Net.WebUtility.HtmlEncode(me.DisplayName)} added you to Talkie.</p>" +
            $"<p>E-mail: <b>{mail}</b><br/>Temporary password: <b>{password}</b></p>" +
            "<p>Please change your password after your first login (Account &rarr; Security).</p>", ct);

        return Results.Ok(new AddMemberResponse(user.ToMember(), password));
    }

    private static async Task<IResult> UpdateMember(Guid id, UpdateMemberRequest request, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        if (!CurrentUser.CanManageCompany(me, me.CompanyId)) return Forbidden("Only the company owner can edit members.");

        var user = await db.Users.Include(u => u.Roles).ThenInclude(r => r.Role)
            .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == me.CompanyId, ct);
        if (user is null) return NotFound("Member not found.");

        var roles = await ValidRoles(db, me.CompanyId, request.RoleIds, ct);
        if (roles is null) return BadRequest("One or more roles do not exist.");
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Username))
            return BadRequest("Display name and username are required.");

        user.FirstName = request.FirstName?.Trim();
        user.LastName = request.LastName?.Trim();
        user.DisplayName = request.DisplayName.Trim();
        user.Username = request.Username.Trim();
        user.Roles.RemoveAll(r => !roles.Any(x => x.Id == r.RoleId));
        foreach (var role in roles.Where(r => user.Roles.All(x => x.RoleId != r.Id)))
        {
            user.Roles.Add(new UserRole { RoleId = role.Id, Role = role });
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(user.ToMember());
    }

    private static async Task<IResult> DeleteMember(Guid id, CurrentUser current, TalkieDbContext db, DeletionService deletion, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        if (!CurrentUser.CanManageCompany(me, me.CompanyId)) return Forbidden("Only the company owner can delete members.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == me.CompanyId, ct);
        if (user is null) return NotFound("Member not found.");
        if (user.Id == me.Id) return BadRequest("You cannot delete yourself.");
        if (user.IsOwner) return BadRequest("The company owner cannot be deleted.");
        if (CurrentUser.IsAdmin(user)) return BadRequest("The app administrator cannot be deleted.");

        await deletion.DeleteUserAsync(user.Id, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateRole(RoleInput input, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        if (!CurrentUser.CanManageCompany(me, me.CompanyId)) return Forbidden("Only the company owner can manage roles.");
        if (RoleProblem(input) is { } problem) return BadRequest(problem);
        var name = input.Name.Trim();
        if (await db.Roles.AnyAsync(r => r.CompanyId == me.CompanyId && r.Name == name, ct)) return BadRequest("This role already exists.");
        var role = new Role { CompanyId = me.CompanyId, Name = name, Color = input.Color };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return Results.Ok(role.ToDto());
    }

    private static async Task<IResult> UpdateRole(Guid id, RoleInput input, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        if (!CurrentUser.CanManageCompany(me, me.CompanyId)) return Forbidden("Only the company owner can manage roles.");
        if (RoleProblem(input) is { } problem) return BadRequest(problem);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == me.CompanyId, ct);
        if (role is null) return NotFound("Role not found.");
        var name = input.Name.Trim();
        if (await db.Roles.AnyAsync(r => r.CompanyId == me.CompanyId && r.Name == name && r.Id != id, ct)) return BadRequest("This role already exists.");
        role.Name = name;
        role.Color = input.Color;
        await db.SaveChangesAsync(ct);
        return Results.Ok(role.ToDto());
    }

    private static async Task<IResult> DeleteRole(Guid id, CurrentUser current, TalkieDbContext db, DeletionService deletion, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        if (!CurrentUser.CanManageCompany(me, me.CompanyId)) return Forbidden("Only the company owner can manage roles.");
        if (!await db.Roles.AnyAsync(r => r.Id == id && r.CompanyId == me.CompanyId, ct)) return NotFound("Role not found.");
        await deletion.DeleteRoleAsync(id, ct);
        return Results.NoContent();
    }

    private static string? RoleProblem(RoleInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 50) return "Role name is required (max 50 characters).";
        if (!Validation.IsValidColor(input.Color)) return "Role color must look like #90CAF9.";
        return null;
    }

    internal static async Task<List<Role>?> ValidRoles(TalkieDbContext db, Guid companyId, List<Guid>? ids, CancellationToken ct)
    {
        ids = ids?.Distinct().ToList() ?? [];
        var roles = await db.Roles.Where(r => r.CompanyId == companyId && ids.Contains(r.Id)).ToListAsync(ct);
        return roles.Count == ids.Count ? roles : null;
    }

    private static string GeneratePassword()
    {
        const string letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        var chars = Enumerable.Range(0, 8).Select(_ => letters[RandomNumberGenerator.GetInt32(letters.Length)])
            .Concat(Enumerable.Range(0, 3).Select(_ => digits[RandomNumberGenerator.GetInt32(digits.Length)]))
            .ToArray();
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
