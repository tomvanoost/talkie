using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;

namespace Talkie.Api.Modules.Chat;

/// <summary>Effective permissions of a user inside a channel.</summary>
public sealed record ChannelAccess(Channel Channel, bool CanSend, bool CanDelete, bool CanReact);

/// <summary>Shared chat logic for direct messages, groups and server channels.</summary>
public sealed class ChatService(
    TalkieDbContext db,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ChatNotifier notifier)
{
    public const int PageSize = 50;

    // ---------- Access ----------

    public Task<bool> IsConversationMemberAsync(Guid conversationId, Guid userId, CancellationToken ct) =>
        db.ConversationMembers.AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId, ct);

    /// <summary>Returns the channel with the user's effective permissions, or null when the user cannot see it.</summary>
    public async Task<ChannelAccess?> GetChannelAccessAsync(Guid channelId, User user, CancellationToken ct)
    {
        var channel = await db.Channels
            .Include(c => c.Server)
            .Include(c => c.VisibleRoles).ThenInclude(v => v.Role)
            .Include(c => c.Permissions).ThenInclude(p => p.Role)
            .FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null || channel.Server!.CompanyId != user.CompanyId) return null;

        var roleIds = await db.UserRoles.Where(r => r.UserId == user.Id).Select(r => r.RoleId).ToListAsync(ct);
        return Evaluate(channel, user, roleIds);
    }

    public static ChannelAccess? Evaluate(Channel channel, User user, IReadOnlyCollection<Guid> roleIds)
    {
        if (CurrentUser.CanManageCompany(user, channel.Server!.CompanyId))
        {
            return new ChannelAccess(channel, true, true, true);
        }

        var visible = channel.VisibleRoles.Count == 0 || channel.VisibleRoles.Any(v => roleIds.Contains(v.RoleId));
        if (!visible) return null;

        var applicable = channel.Permissions.Where(p => p.RoleId is null || roleIds.Contains(p.RoleId.Value)).ToList();
        return new ChannelAccess(
            channel,
            applicable.Any(p => p.CanSendMessages),
            applicable.Any(p => p.CanDeleteMessages),
            applicable.Any(p => p.CanReact));
    }

    public async Task<List<Guid>> VisibleChannelIdsAsync(User user, CancellationToken ct)
    {
        var channels = await db.Channels.Include(c => c.Server).Include(c => c.VisibleRoles).Include(c => c.Permissions)
            .Where(c => c.Server!.CompanyId == user.CompanyId).ToListAsync(ct);
        var roleIds = await db.UserRoles.Where(r => r.UserId == user.Id).Select(r => r.RoleId).ToListAsync(ct);
        return channels.Where(c => Evaluate(c, user, roleIds) is not null).Select(c => c.Id).ToList();
    }

    // ---------- Reading ----------

    public async Task<List<MessageDto>> GetPageAsync(IQueryable<Message> scope, Guid viewerId, DateTimeOffset? before, CancellationToken ct)
    {
        var query = scope;
        if (before is { } b) query = query.Where(m => m.CreatedAt < b);
        var page = await query.OrderByDescending(m => m.CreatedAt).Take(PageSize).Select(m => m.Id).ToListAsync(ct);
        var dtos = await ToDtosAsync(page, viewerId, ct);
        return dtos.OrderBy(m => m.CreatedAt).ToList();
    }

    public async Task<List<MessageDto>> ToDtosAsync(IReadOnlyCollection<Guid> messageIds, Guid viewerId, CancellationToken ct)
    {
        if (messageIds.Count == 0) return [];

        var messages = await db.Messages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => new
            {
                m.Id, m.ConversationId, m.ChannelId, m.SenderId, m.Content, m.CreatedAt, m.IsDeleted, m.ReplyToId,
                Sender = new { m.Sender!.DisplayName, m.Sender.AvatarContentType, m.Sender.AvatarVersion, m.Sender.ActiveEffect },
                RoleColor = m.Sender.Roles.OrderBy(r => r.Role!.Name).Select(r => r.Role!.Color).FirstOrDefault(),
                Reply = m.ReplyTo == null ? null : new { m.ReplyTo.Id, m.ReplyTo.Sender!.DisplayName, m.ReplyTo.Content, m.ReplyTo.IsDeleted },
            })
            .ToListAsync(ct);

        var attachments = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId != null && messageIds.Contains(a.MessageId.Value))
            .Select(a => new { a.Id, a.MessageId, a.FileName, a.ContentType, a.Size })
            .ToListAsync(ct);

        var reactions = await db.Reactions.AsNoTracking()
            .Where(r => messageIds.Contains(r.MessageId))
            .Select(r => new { r.MessageId, r.UserId, r.Emoji, r.CreatedAt })
            .ToListAsync(ct);

        return messages.Select(m => new MessageDto(
            m.Id, m.ConversationId, m.ChannelId, m.SenderId,
            m.Sender.DisplayName,
            m.Sender.AvatarContentType is null ? null : $"/api/users/{m.SenderId}/avatar?v={m.Sender.AvatarVersion}",
            m.Sender.ActiveEffect,
            m.ChannelId is null ? null : m.RoleColor,
            m.IsDeleted ? "" : m.Content,
            m.CreatedAt,
            m.IsDeleted,
            m.Reply is null ? null : new ReplyPreviewDto(m.Reply.Id, m.Reply.DisplayName, m.Reply.IsDeleted ? "Message deleted" : Truncate(m.Reply.Content, 120)),
            attachments.Where(a => a.MessageId == m.Id)
                .Select(a => new AttachmentDto(a.Id, a.FileName, a.ContentType, a.Size, $"/api/attachments/{a.Id}", Mapping.IsImage(a.ContentType)))
                .ToList(),
            reactions.Where(r => r.MessageId == m.Id)
                .GroupBy(r => r.Emoji)
                .OrderBy(g => g.Min(r => r.CreatedAt))
                .Select(g => new ReactionDto(g.Key, g.Count(), g.Any(r => r.UserId == viewerId)))
                .ToList()))
            .ToList();
    }

    // ---------- Writing ----------

    /// <summary>Validates and stores a new message. Returns an error message or the new message id.</summary>
    public async Task<(Guid? Id, string? Error)> CreateMessageAsync(
        User sender, Guid? conversationId, Guid? channelId, SendMessageRequest request, CancellationToken ct)
    {
        var content = request.Content?.Trim() ?? "";
        var attachmentIds = request.AttachmentIds?.Distinct().ToList() ?? [];
        if (content.Length == 0 && attachmentIds.Count == 0) return (null, "Type a message or attach a file.");
        if (content.Length > 4000) return (null, "Messages can be at most 4000 characters.");

        var attachments = await db.Attachments
            .Where(a => attachmentIds.Contains(a.Id) && a.UploaderId == sender.Id && a.MessageId == null)
            .ToListAsync(ct);
        if (attachments.Count != attachmentIds.Count) return (null, "One or more attachments could not be found.");

        if (request.ReplyToId is { } replyId)
        {
            var sameScope = await db.Messages.AnyAsync(m => m.Id == replyId && m.ConversationId == conversationId && m.ChannelId == channelId, ct);
            if (!sameScope) return (null, "The message you are replying to does not exist.");
        }

        var message = new Message
        {
            ConversationId = conversationId,
            ChannelId = channelId,
            SenderId = sender.Id,
            Content = content,
            ReplyToId = request.ReplyToId,
            Embedding = content.Length == 0 ? null : await EmbedAsync(content, ct),
        };
        db.Messages.Add(message);
        foreach (var a in attachments) a.MessageId = message.Id;

        await db.Users.Where(u => u.Id == sender.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.MessagesSent, u => u.MessagesSent + 1), ct);

        if (conversationId is { } cid)
        {
            await db.Conversations.Where(c => c.Id == cid)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityAt, message.CreatedAt), ct);
        }

        await db.SaveChangesAsync(ct);
        return (message.Id, null);
    }

    public async Task SoftDeleteAsync(Message message, CancellationToken ct)
    {
        message.IsDeleted = true;
        message.Content = "";
        message.Embedding = null;
        await db.Attachments.Where(a => a.MessageId == message.Id).ExecuteDeleteAsync(ct);
        await db.Reactions.Where(r => r.MessageId == message.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ToggleReactionAsync(Guid messageId, Guid userId, string emoji, CancellationToken ct)
    {
        var existing = await db.Reactions.FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji, ct);
        if (existing is null) db.Reactions.Add(new Reaction { MessageId = messageId, UserId = userId, Emoji = emoji });
        else db.Reactions.Remove(existing);
        await db.SaveChangesAsync(ct);
        return existing is null;
    }

    public async Task NotifyAsync(Message message, Guid companyId, CancellationToken ct)
    {
        if (message.ConversationId is { } cid)
        {
            var members = await db.ConversationMembers.Where(cm => cm.ConversationId == cid).Select(cm => cm.UserId).ToListAsync(ct);
            await notifier.MessageChangedInConversation(members, cid, message.Id);
        }
        else if (message.ChannelId is { } chid)
        {
            await notifier.MessageChangedInChannel(companyId, chid, message.Id);
        }
    }

    // ---------- Semantic search ----------

    public async Task<SqlVector<float>> EmbedAsync(string text, CancellationToken ct)
    {
        var embedding = await embeddings.GenerateVectorAsync(text, cancellationToken: ct);
        return new SqlVector<float>(embedding);
    }

    /// <summary>
    /// Semantic search over every message the user can read, ranked by SQL Server 2025's
    /// <c>VECTOR_DISTANCE('cosine', ...)</c> on the native vector column.
    /// </summary>
    public async Task<List<SearchResultDto>> SearchAsync(User user, string query, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var vector = await EmbedAsync(query, ct);

        var conversationIds = await db.ConversationMembers.Where(cm => cm.UserId == user.Id).Select(cm => cm.ConversationId).ToListAsync(ct);
        var channelIds = await VisibleChannelIdsAsync(user, ct);

        var hits = await db.Messages.AsNoTracking()
            .Where(m => !m.IsDeleted && m.Embedding != null)
            .Where(m => (m.ConversationId != null && conversationIds.Contains(m.ConversationId.Value)) ||
                        (m.ChannelId != null && channelIds.Contains(m.ChannelId.Value)))
            .Select(m => new { m.Id, Distance = EF.Functions.VectorDistance("cosine", m.Embedding!.Value, vector) })
            .OrderBy(x => x.Distance)
            .Take(take)
            .ToListAsync(ct);

        var dtos = await ToDtosAsync(hits.Select(h => h.Id).ToList(), user.Id, ct);
        var channelNames = await db.Channels.Where(c => channelIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var conversations = await ConversationNamesAsync(user.Id, ct);

        return hits.Select(h =>
        {
            var dto = dtos.First(d => d.Id == h.Id);
            var location = dto.ChannelId is { } chId && channelNames.TryGetValue(chId, out var cn) ? "#" + cn
                : dto.ConversationId is { } cId && conversations.TryGetValue(cId, out var name) ? name : "Chat";
            return new SearchResultDto(dto, location, Math.Round(1 - h.Distance, 3));
        }).ToList();
    }

    public async Task<Dictionary<Guid, string>> ConversationNamesAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Conversations.AsNoTracking()
            .Where(c => c.Members.Any(m => m.UserId == userId))
            .Select(c => new
            {
                c.Id, c.IsGroup, c.Name,
                Other = c.Members.Where(m => m.UserId != userId).Select(m => m.User!.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.IsGroup ? r.Name ?? "Group" : r.Other ?? "Direct message");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
