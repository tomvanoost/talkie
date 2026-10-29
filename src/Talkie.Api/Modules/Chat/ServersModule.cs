using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;
using static Talkie.Api.Infrastructure.EndpointExtensions;

namespace Talkie.Api.Modules.Chat;

/// <summary>The company server and its text channels, including per-role visibility and permissions.</summary>
public sealed class ServersModule : IModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Servers").RequireTalkieUser();
        g.MapGet("/servers", GetServers);
        g.MapPost("/servers/{serverId:guid}/channels", CreateChannel);
        g.MapPut("/channels/{id:guid}", UpdateChannel);
        g.MapDelete("/channels/{id:guid}", DeleteChannel);
        g.MapGet("/channels/{id:guid}/messages", GetMessages);
        g.MapPost("/channels/{id:guid}/messages", SendMessage);
    }

    private static async Task<List<ServerDto>> GetServers(CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var roleIds = await db.UserRoles.Where(r => r.UserId == me.Id).Select(r => r.RoleId).ToListAsync(ct);
        var servers = await db.Servers.AsNoTracking()
            .Include(s => s.Channels).ThenInclude(c => c.VisibleRoles).ThenInclude(v => v.Role)
            .Include(s => s.Channels).ThenInclude(c => c.Permissions).ThenInclude(p => p.Role)
            .Where(s => s.CompanyId == me.CompanyId)
            .ToListAsync(ct);

        return servers.Select(s => new ServerDto(
            s.Id, s.Name, CurrentUser.CanManageCompany(me, s.CompanyId),
            s.Channels.OrderBy(c => c.Position).ThenBy(c => c.CreatedAt)
                .Select(c => { c.Server = s; return ChatService.Evaluate(c, me, roleIds) is { } access ? ToDto(access) : null; })
                .OfType<ChannelDto>()
                .ToList()))
            .ToList();
    }

    private static ChannelDto ToDto(ChannelAccess access)
    {
        var c = access.Channel;
        return new ChannelDto(
            c.Id, c.ServerId, c.Name, c.Description,
            c.VisibleRoles.Where(v => v.Role is not null).Select(v => v.Role!.ToDto()).ToList(),
            c.Permissions.OrderBy(p => p.RoleId is null ? 0 : 1).ThenBy(p => p.Role?.Name)
                .Select(p => new ChannelPermissionDto(p.RoleId, p.Role?.Name ?? "@everyone", p.Role?.Color ?? "#B0BEC5",
                    p.CanSendMessages, p.CanDeleteMessages, p.CanReact))
                .ToList(),
            access.CanSend, access.CanDelete, access.CanReact);
    }

    private static async Task<string?> ValidateAsync(SaveChannelRequest request, Guid companyId, TalkieDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 100) return "Channel name is required (max 100 characters).";
        if ((request.Description?.Length ?? 0) > 500) return "Description can be at most 500 characters.";

        var roleIds = (request.VisibleToRoleIds ?? []).Concat((request.Permissions ?? []).Where(p => p.RoleId != null).Select(p => p.RoleId!.Value)).Distinct().ToList();
        var known = await db.Roles.CountAsync(r => r.CompanyId == companyId && roleIds.Contains(r.Id), ct);
        if (known != roleIds.Count) return "One or more roles do not exist.";
        if ((request.Permissions ?? []).GroupBy(p => p.RoleId).Any(g => g.Count() > 1)) return "Each role can only appear once in the permissions.";
        return null;
    }

    private static void Apply(Channel channel, SaveChannelRequest request)
    {
        channel.Name = NormalizeName(request.Name);
        channel.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        channel.VisibleRoles = (request.VisibleToRoleIds ?? []).Distinct().Select(id => new ChannelVisibleRole { ChannelId = channel.Id, RoleId = id }).ToList();

        var permissions = (request.Permissions ?? []).ToList();
        if (permissions.All(p => p.RoleId != null))
        {
            // There is always an @everyone row; by default everyone can send messages and react with emoji.
            permissions.Insert(0, new ChannelPermissionInput(null, true, false, true));
        }
        channel.Permissions = permissions.Select(p => new ChannelPermission
        {
            ChannelId = channel.Id,
            RoleId = p.RoleId,
            CanSendMessages = p.CanSendMessages,
            CanDeleteMessages = p.CanDeleteMessages,
            CanReact = p.CanReact,
        }).ToList();
    }

    internal static string NormalizeName(string name) =>
        string.Join('-', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static async Task<IResult> CreateChannel(Guid serverId, SaveChannelRequest request, CurrentUser current, TalkieDbContext db, ChatNotifier notifier, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var server = await db.Servers.FirstOrDefaultAsync(s => s.Id == serverId && s.CompanyId == me.CompanyId, ct);
        if (server is null) return NotFound("Server not found.");
        if (!CurrentUser.CanManageCompany(me, server.CompanyId)) return Forbidden("Only the company owner can create channels.");
        if (await ValidateAsync(request, me.CompanyId, db, ct) is { } error) return BadRequest(error);

        var position = await db.Channels.Where(c => c.ServerId == serverId).Select(c => (int?)c.Position).MaxAsync(ct) ?? -1;
        var channel = new Channel { ServerId = serverId, Name = "", Position = position + 1 };
        Apply(channel, request);
        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);
        await notifier.ChannelsChanged(me.CompanyId);
        return Results.Ok(channel.Id);
    }

    private static async Task<IResult> UpdateChannel(Guid id, SaveChannelRequest request, CurrentUser current, TalkieDbContext db, ChatNotifier notifier, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var channel = await db.Channels.Include(c => c.Server).Include(c => c.VisibleRoles).Include(c => c.Permissions)
            .FirstOrDefaultAsync(c => c.Id == id && c.Server!.CompanyId == me.CompanyId, ct);
        if (channel is null) return NotFound("Channel not found.");
        if (!CurrentUser.CanManageCompany(me, me.CompanyId)) return Forbidden("Only the company owner can edit channels.");
        if (await ValidateAsync(request, me.CompanyId, db, ct) is { } error) return BadRequest(error);

        db.ChannelVisibleRoles.RemoveRange(channel.VisibleRoles);
        db.ChannelPermissions.RemoveRange(channel.Permissions);
        await db.SaveChangesAsync(ct);
        Apply(channel, request);
        await db.SaveChangesAsync(ct);
        await notifier.ChannelsChanged(me.CompanyId);
        return Results.Ok(channel.Id);
    }

    private static async Task<IResult> DeleteChannel(Guid id, CurrentUser current, TalkieDbContext db, DeletionService deletion, ChatNotifier notifier, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == id && c.Server!.CompanyId == me.CompanyId, ct);
        if (channel is null) return NotFound("Channel not found.");
        if (!CurrentUser.CanManageCompany(me, me.CompanyId)) return Forbidden("Only the company owner can delete channels.");
        await deletion.DeleteChannelAsync(id, ct);
        await notifier.ChannelsChanged(me.CompanyId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetMessages(Guid id, DateTimeOffset? before, CurrentUser current, ChatService chat, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        if (await chat.GetChannelAccessAsync(id, me, ct) is null) return NotFound("Channel not found.");
        return Results.Ok(await chat.GetPageAsync(db.Messages.Where(m => m.ChannelId == id), me.Id, before, ct));
    }

    private static async Task<IResult> SendMessage(Guid id, SendMessageRequest request, CurrentUser current, ChatService chat, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var access = await chat.GetChannelAccessAsync(id, me, ct);
        if (access is null) return NotFound("Channel not found.");
        if (!access.CanSend) return Forbidden("Your roles cannot send messages in this channel.");

        var (messageId, error) = await chat.CreateMessageAsync(me, null, id, request, ct);
        if (error is not null) return BadRequest(error);

        var message = await db.Messages.FirstAsync(m => m.Id == messageId, ct);
        await chat.NotifyAsync(message, me.CompanyId, ct);
        return Results.Ok((await chat.ToDtosAsync([message.Id], me.Id, ct))[0]);
    }
}
