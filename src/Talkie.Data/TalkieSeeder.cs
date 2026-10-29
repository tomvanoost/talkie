using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Talkie.Shared;

namespace Talkie.Data;

public sealed record SeedOptions(string OwnerPassword, string AdminPassword);

/// <summary>
/// Seeds the built-in "Talkie" company, its owner (owner@talkie.com), the app administrator (admin@talkie.com),
/// the default server with its channels and the global app settings. Idempotent.
/// </summary>
public static class TalkieSeeder
{
    public static async Task SeedAsync(TalkieDbContext db, SeedOptions options, CancellationToken ct = default)
    {
        var hasher = new PasswordHasher<User>();

        if (!await db.AppSettings.AnyAsync(ct))
        {
            db.AppSettings.Add(new AppSettings());
        }

        var company = await db.Companies.Include(c => c.Roles)
            .FirstOrDefaultAsync(c => c.Name == TalkieConstants.DefaultCompanyName, ct);

        if (company is null)
        {
            company = new Company { Name = TalkieConstants.DefaultCompanyName };
            company.Roles.AddRange(
            [
                new Role { Name = "Admin", Color = "#EF9A9A" },
                new Role { Name = "Moderator", Color = "#CE93D8" },
                new Role { Name = "Member", Color = "#90CAF9" },
            ]);
            db.Companies.Add(company);
        }

        if (!await db.Users.AnyAsync(u => u.Email == TalkieConstants.OwnerEmail, ct))
        {
            var owner = new User
            {
                Email = TalkieConstants.OwnerEmail,
                Username = "owner",
                DisplayName = "Talkie Owner",
                FirstName = "Talkie",
                LastName = "Owner",
                IsOwner = true,
                Money = 1_000,
                Company = company,
            };
            owner.PasswordHash = hasher.HashPassword(owner, options.OwnerPassword);
            db.Users.Add(owner);
        }

        if (!await db.Users.AnyAsync(u => u.Email == TalkieConstants.AdminEmail, ct))
        {
            var admin = new User
            {
                Email = TalkieConstants.AdminEmail,
                Username = "admin",
                DisplayName = "Talkie Admin",
                FirstName = "Talkie",
                LastName = "Admin",
                Money = 1_000,
                Company = company,
            };
            admin.PasswordHash = hasher.HashPassword(admin, options.AdminPassword);
            var adminRole = company.Roles.FirstOrDefault(r => r.Name == "Admin");
            if (adminRole is not null)
            {
                admin.Roles.Add(new UserRole { Role = adminRole });
            }
            db.Users.Add(admin);
        }

        if (!await db.Servers.AnyAsync(s => s.CompanyId == company.Id, ct))
        {
            db.Servers.Add(CreateDefaultServer(company));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Every company gets one server named after the company, with a #general channel.</summary>
    public static Server CreateDefaultServer(Company company)
    {
        var server = new Server { Name = company.Name, Company = company };
        server.Channels.Add(CreateChannel("general", "General chat for everyone", 0));
        server.Channels.Add(CreateChannel("random", "Off-topic and fun", 1));
        return server;
    }

    public static Channel CreateChannel(string name, string? description, int position) => new()
    {
        Name = name,
        Description = description,
        Position = position,
        // Default: everyone can send messages and react with emoji, nobody can delete other people's messages.
        Permissions = [new ChannelPermission { RoleId = null, CanSendMessages = true, CanReact = true, CanDeleteMessages = false }],
    };
}
