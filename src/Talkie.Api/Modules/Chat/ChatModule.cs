using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;
using static Talkie.Api.Infrastructure.EndpointExtensions;

namespace Talkie.Api.Modules.Chat;

/// <summary>Direct messages, group chats, attachments, reactions and semantic message search.</summary>
public sealed class ChatModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration) => services.AddScoped<ChatService>();

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Chat").RequireTalkieUser();
        g.MapGet("/conversations", GetConversations);
        g.MapPost("/conversations/direct", StartDirect);
        g.MapPost("/conversations/group", CreateGroup);
        g.MapGet("/conversations/{id:guid}/messages", GetConversationMessages);
        g.MapPost("/conversations/{id:guid}/messages", SendConversationMessage);
        g.MapPost("/uploads", Upload).DisableAntiforgery();
        g.MapGet("/messages/{id:guid}", GetMessage);
        g.MapDelete("/messages/{id:guid}", DeleteMessage);
        g.MapPost("/messages/{id:guid}/reactions", React);
        g.MapGet("/search", Search);

        // Anonymous so <img>/<a> tags can load files; attachment ids are random v4 GUIDs (122 bits of entropy).
        app.MapGet("/api/attachments/{id:guid}", async (Guid id, TalkieDbContext db, CancellationToken ct) =>
        {
            var file = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (file is null) return Results.NotFound();
            return Mapping.IsImage(file.ContentType)
                ? Results.File(file.Data, file.ContentType)
                : Results.File(file.Data, file.ContentType, file.FileName);
        }).WithTags("Chat");
    }

    private static async Task<List<ConversationDto>> GetConversations(CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var meId = current.Id;
        var conversations = await db.Conversations.AsNoTracking()
            .Include(c => c.Members).ThenInclude(m => m.User).ThenInclude(u => u!.Company)
            .Where(c => c.Members.Any(m => m.UserId == meId))
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

        var ids = conversations.Select(c => c.Id).ToList();
        var lastMessages = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId != null && ids.Contains(m.ConversationId.Value))
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).Select(m => new { m.ConversationId, m.Content, m.IsDeleted, Files = m.Attachments.Count }).First())
            .ToListAsync(ct);

        return conversations.Select(c =>
        {
            var last = lastMessages.FirstOrDefault(l => l.ConversationId == c.Id);
            var preview = last is null ? null
                : last.IsDeleted ? "Message deleted"
                : last.Content.Length > 0 ? last.Content
                : last.Files > 0 ? "📎 Attachment" : null;
            return ToDto(c, meId, preview);
        }).ToList();
    }

    internal static ConversationDto ToDto(Conversation c, Guid meId, string? preview)
    {
        var members = c.Members.Where(m => m.User is not null).Select(m => m.User!.ToSummary()).ToList();
        var name = c.IsGroup ? c.Name ?? "Group" : members.FirstOrDefault(m => m.Id != meId)?.DisplayName ?? "Direct message";
        return new ConversationDto(c.Id, c.IsGroup, name, c.OwnerId, members, preview, c.LastActivityAt);
    }

    private static async Task<IResult> StartDirect(StartDirectMessageRequest request, CurrentUser current, TalkieDbContext db, ChatNotifier notifier, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var email = Validation.NormalizeEmail(request.Email ?? "");
        var other = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (other is null) return NotFound("No user with this e-mail exists.");
        if (other.Id == me.Id) return BadRequest("You cannot start a conversation with yourself.");

        var existing = await db.Conversations
            .Where(c => !c.IsGroup && c.Members.Any(m => m.UserId == me.Id) && c.Members.Any(m => m.UserId == other.Id))
            .Select(c => c.Id).FirstOrDefaultAsync(ct);

        if (existing == Guid.Empty)
        {
            var conversation = new Conversation { IsGroup = false };
            conversation.Members.Add(new ConversationMember { UserId = me.Id });
            conversation.Members.Add(new ConversationMember { UserId = other.Id });
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(ct);
            existing = conversation.Id;
            await notifier.ConversationsChanged([me.Id, other.Id]);
        }

        return Results.Ok(await LoadDto(db, existing, me.Id, ct));
    }

    private static async Task<IResult> CreateGroup(CreateGroupRequest request, CurrentUser current, TalkieDbContext db, ChatNotifier notifier, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var name = request.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 100) return BadRequest("Give the group a name (max 100 characters).");

        var emails = (request.Emails ?? []).Select(Validation.NormalizeEmail).Where(e => e.Length > 0 && e != me.Email).Distinct().ToList();
        if (emails.Count == 0) return BadRequest("Add at least one other person to the group.");

        var users = await db.Users.Where(u => emails.Contains(u.Email)).ToListAsync(ct);
        var missing = emails.Except(users.Select(u => u.Email)).ToList();
        if (missing.Count > 0) return BadRequest($"Unknown e-mail: {string.Join(", ", missing)}");

        var conversation = new Conversation { IsGroup = true, Name = name, OwnerId = me.Id };
        conversation.Members.Add(new ConversationMember { UserId = me.Id });
        conversation.Members.AddRange(users.Select(u => new ConversationMember { UserId = u.Id }));
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        await notifier.ConversationsChanged(conversation.Members.Select(m => m.UserId));
        return Results.Ok(await LoadDto(db, conversation.Id, me.Id, ct));
    }

    private static async Task<ConversationDto> LoadDto(TalkieDbContext db, Guid id, Guid meId, CancellationToken ct)
    {
        var c = await db.Conversations.AsNoTracking()
            .Include(x => x.Members).ThenInclude(m => m.User).ThenInclude(u => u!.Company)
            .FirstAsync(x => x.Id == id, ct);
        return ToDto(c, meId, null);
    }

    private static async Task<IResult> GetConversationMessages(Guid id, DateTimeOffset? before, CurrentUser current, ChatService chat, TalkieDbContext db, CancellationToken ct)
    {
        if (!await chat.IsConversationMemberAsync(id, current.Id, ct)) return NotFound("Conversation not found.");
        return Results.Ok(await chat.GetPageAsync(db.Messages.Where(m => m.ConversationId == id), current.Id, before, ct));
    }

    private static async Task<IResult> SendConversationMessage(Guid id, SendMessageRequest request, CurrentUser current, ChatService chat, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        if (!await chat.IsConversationMemberAsync(id, me.Id, ct)) return NotFound("Conversation not found.");

        var (messageId, error) = await chat.CreateMessageAsync(me, id, null, request, ct);
        if (error is not null) return BadRequest(error);

        var message = await db.Messages.FirstAsync(m => m.Id == messageId, ct);
        await chat.NotifyAsync(message, me.CompanyId, ct);
        return Results.Ok((await chat.ToDtosAsync([message.Id], me.Id, ct))[0]);
    }

    private static async Task<IResult> Upload(IFormFile file, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("The file is empty.");
        if (file.Length > TalkieConstants.MaxAttachmentBytes) return BadRequest("Files can be at most 10 MB.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            UploaderId = current.Id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = contentType,
            Size = file.Length,
            Data = ms.ToArray(),
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new UploadResultDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, Mapping.IsImage(contentType)));
    }

    private static async Task<IResult> GetMessage(Guid id, CurrentUser current, ChatService chat, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (message is null || !await CanReadAsync(message, me, chat, ct)) return NotFound();
        return Results.Ok((await chat.ToDtosAsync([id], me.Id, ct))[0]);
    }

    private static async Task<bool> CanReadAsync(Message message, User me, ChatService chat, CancellationToken ct) =>
        message.ConversationId is { } cid
            ? await chat.IsConversationMemberAsync(cid, me.Id, ct)
            : message.ChannelId is { } chid && await chat.GetChannelAccessAsync(chid, me, ct) is not null;

    /// <summary>
    /// Anyone can delete their own messages. In a group the group owner can delete any message;
    /// in a channel, members whose role has "Delete messages" can.
    /// </summary>
    private static async Task<IResult> DeleteMessage(Guid id, CurrentUser current, ChatService chat, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (message is null || message.IsDeleted) return NotFound("Message not found.");

        bool allowed;
        if (message.ConversationId is { } cid)
        {
            if (!await chat.IsConversationMemberAsync(cid, me.Id, ct)) return NotFound("Message not found.");
            var conversation = await db.Conversations.AsNoTracking().FirstAsync(c => c.Id == cid, ct);
            allowed = message.SenderId == me.Id || (conversation.IsGroup && conversation.OwnerId == me.Id);
        }
        else
        {
            var access = await chat.GetChannelAccessAsync(message.ChannelId!.Value, me, ct);
            if (access is null) return NotFound("Message not found.");
            allowed = message.SenderId == me.Id || access.CanDelete;
        }

        if (!allowed) return Forbidden("You are not allowed to delete this message.");
        await chat.SoftDeleteAsync(message, ct);
        await chat.NotifyAsync(message, me.CompanyId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> React(Guid id, ReactRequest request, CurrentUser current, ChatService chat, TalkieDbContext db, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        var emoji = request.Emoji?.Trim() ?? "";
        if (emoji.Length is 0 or > 16) return BadRequest("Pick an emoji.");

        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted, ct);
        if (message is null) return NotFound("Message not found.");

        if (message.ConversationId is { } cid)
        {
            if (!await chat.IsConversationMemberAsync(cid, me.Id, ct)) return NotFound("Message not found.");
        }
        else
        {
            var access = await chat.GetChannelAccessAsync(message.ChannelId!.Value, me, ct);
            if (access is null) return NotFound("Message not found.");
            if (!access.CanReact) return Forbidden("Your roles cannot react with emoji in this channel.");
        }

        await chat.ToggleReactionAsync(id, me.Id, emoji, ct);
        await chat.NotifyAsync(message, me.CompanyId, ct);
        return Results.Ok((await chat.ToDtosAsync([id], me.Id, ct))[0]);
    }

    private static async Task<List<SearchResultDto>> Search(string? q, CurrentUser current, ChatService chat, CancellationToken ct)
    {
        var me = await current.GetAsync(ct);
        return await chat.SearchAsync(me, q ?? "", 20, ct);
    }
}
