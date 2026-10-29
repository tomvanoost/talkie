using Microsoft.EntityFrameworkCore;
using Talkie.Data;

namespace Talkie.Api.Infrastructure;

/// <summary>
/// The schema uses NO ACTION foreign keys (SQL Server does not allow multiple cascade paths),
/// so deleting a user, message, channel or company removes dependents explicitly, in a transaction.
/// </summary>
public sealed class DeletionService(TalkieDbContext db)
{
    public async Task DeleteMessagesAsync(IQueryable<Message> messages, CancellationToken ct)
    {
        var ids = messages.Select(m => m.Id);
        await db.Messages.Where(m => m.ReplyToId != null && ids.Contains(m.ReplyToId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReplyToId, (Guid?)null), ct);
        await db.Reactions.Where(r => ids.Contains(r.MessageId)).ExecuteDeleteAsync(ct);
        await db.Attachments.Where(a => a.MessageId != null && ids.Contains(a.MessageId.Value)).ExecuteDeleteAsync(ct);
        await messages.ExecuteDeleteAsync(ct);
    }

    /// <summary>Deletes a user account (and therefore frees up the e-mail address) with everything it owns.</summary>
    public async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await DeleteMessagesAsync(db.Messages.Where(m => m.SenderId == userId), ct);
        await db.Reactions.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Attachments.Where(a => a.UploaderId == userId).ExecuteDeleteAsync(ct);

        // Direct messages with this user disappear; groups they owned get a new owner (or vanish when empty).
        var directIds = db.ConversationMembers
            .Where(cm => cm.UserId == userId && !cm.Conversation!.IsGroup)
            .Select(cm => cm.ConversationId);
        await DeleteConversationsAsync(directIds, ct);

        await db.ConversationMembers.Where(cm => cm.UserId == userId).ExecuteDeleteAsync(ct);
        var ownedGroups = await db.Conversations.Where(c => c.IsGroup && c.OwnerId == userId).ToListAsync(ct);
        foreach (var group in ownedGroups)
        {
            group.OwnerId = await db.ConversationMembers.Where(cm => cm.ConversationId == group.Id)
                .OrderBy(cm => cm.JoinedAt).Select(cm => (Guid?)cm.UserId).FirstOrDefaultAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        await DeleteConversationsAsync(db.Conversations.Where(c => c.IsGroup && c.OwnerId == null).Select(c => c.Id), ct);

        await db.Reports.Where(r => r.ReporterId == userId || r.ReportedUserId == userId).ExecuteDeleteAsync(ct);
        await db.AiMessages.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UnopenedBoxes.Where(b => b.UserId == userId).ExecuteDeleteAsync(ct);
        await db.OwnedItems.Where(o => o.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserRoles.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserAvatars.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
    }

    private async Task DeleteConversationsAsync(IQueryable<Guid> conversationIds, CancellationToken ct)
    {
        var ids = await conversationIds.ToListAsync(ct);
        if (ids.Count == 0) return;
        await DeleteMessagesAsync(db.Messages.Where(m => m.ConversationId != null && ids.Contains(m.ConversationId.Value)), ct);
        await db.ConversationMembers.Where(cm => ids.Contains(cm.ConversationId)).ExecuteDeleteAsync(ct);
        await db.Conversations.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync(ct);
    }

    public async Task DeleteChannelAsync(Guid channelId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DeleteMessagesAsync(db.Messages.Where(m => m.ChannelId == channelId), ct);
        await db.ChannelPermissions.Where(p => p.ChannelId == channelId).ExecuteDeleteAsync(ct);
        await db.ChannelVisibleRoles.Where(p => p.ChannelId == channelId).ExecuteDeleteAsync(ct);
        await db.Channels.Where(c => c.Id == channelId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.UserRoles.Where(r => r.RoleId == roleId).ExecuteDeleteAsync(ct);
        await db.ChannelPermissions.Where(p => p.RoleId == roleId).ExecuteDeleteAsync(ct);
        await db.ChannelVisibleRoles.Where(p => p.RoleId == roleId).ExecuteDeleteAsync(ct);
        await db.Roles.Where(r => r.Id == roleId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteCompanyAsync(Guid companyId, CancellationToken ct)
    {
        var userIds = await db.Users.Where(u => u.CompanyId == companyId).Select(u => u.Id).ToListAsync(ct);
        foreach (var userId in userIds) await DeleteUserAsync(userId, ct);

        var channelIds = await db.Channels.Where(c => c.Server!.CompanyId == companyId).Select(c => c.Id).ToListAsync(ct);
        foreach (var channelId in channelIds) await DeleteChannelAsync(channelId, ct);

        var roleIds = await db.Roles.Where(r => r.CompanyId == companyId).Select(r => r.Id).ToListAsync(ct);
        foreach (var roleId in roleIds) await DeleteRoleAsync(roleId, ct);

        await db.Servers.Where(s => s.CompanyId == companyId).ExecuteDeleteAsync(ct);
        await db.Companies.Where(c => c.Id == companyId).ExecuteDeleteAsync(ct);
    }
}
