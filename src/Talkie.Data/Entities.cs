using Microsoft.Data.SqlTypes;

namespace Talkie.Data;

public sealed class Company
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<User> Members { get; set; } = [];
    public List<Role> Roles { get; set; } = [];
    public Server? Server { get; set; }
}

public sealed class Role
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public required string Name { get; set; }
    public required string Color { get; set; }
    public List<UserRole> Members { get; set; } = [];
}

public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public required string Email { get; set; }
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool IsOwner { get; set; }

    /// <summary>Content type of the uploaded profile picture (the bytes live in <see cref="UserAvatar"/>).</summary>
    public string? AvatarContentType { get; set; }
    public int AvatarVersion { get; set; }

    // Economy & stats
    public long Money { get; set; }
    public long Shards { get; set; }
    public long PlaytimeSeconds { get; set; }
    public long MessagesSent { get; set; }
    public long BoxesOpened { get; set; }
    public string? ActiveEffect { get; set; }
    public int ShardMultiplier { get; set; } = 1;

    // AFK
    public DateTimeOffset? AfkSince { get; set; }
    public DateTimeOffset? AfkLastCreditedAt { get; set; }
    public long AfkShardsThisSession { get; set; }

    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<UserRole> Roles { get; set; } = [];
    public List<OwnedItem> OwnedItems { get; set; } = [];
}

/// <summary>Profile picture bytes, kept out of the Users table so loading a user never drags the image along.</summary>
public sealed class UserAvatar
{
    public Guid UserId { get; set; }
    public byte[] Data { get; set; } = [];
    public required string ContentType { get; set; }
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

public enum OwnedItemKind { Effect = 1, Booster = 2 }

public sealed class OwnedItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public OwnedItemKind Kind { get; set; }
    public required string Key { get; set; }
    public DateTimeOffset PurchasedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UnopenedBox
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string BoxKey { get; set; }
    public DateTimeOffset PurchasedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------- Chat ----------

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public bool IsGroup { get; set; }
    public string? Name { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ConversationMember> Members { get; set; } = [];
}

public sealed class ConversationMember
{
    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Server
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public required string Name { get; set; }
    public List<Channel> Channels { get; set; } = [];
}

public sealed class Channel
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ServerId { get; set; }
    public Server? Server { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ChannelVisibleRole> VisibleRoles { get; set; } = [];
    public List<ChannelPermission> Permissions { get; set; } = [];
}

/// <summary>When a channel has visible roles, only members with one of those roles (and the owner) can see it.</summary>
public sealed class ChannelVisibleRole
{
    public Guid ChannelId { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

/// <summary>A permission row. <see cref="RoleId"/> null means "@everyone".</summary>
public sealed class ChannelPermission
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ChannelId { get; set; }
    public Guid? RoleId { get; set; }
    public Role? Role { get; set; }
    public bool CanSendMessages { get; set; }
    public bool CanDeleteMessages { get; set; }
    public bool CanReact { get; set; }
}

public sealed class Message
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? ConversationId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid SenderId { get; set; }
    public User? Sender { get; set; }
    public string Content { get; set; } = "";
    public Guid? ReplyToId { get; set; }
    public Message? ReplyTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; set; }

    /// <summary>Semantic embedding of the message text, stored in SQL Server 2025's native vector type.</summary>
    public SqlVector<float>? Embedding { get; set; }

    public List<Attachment> Attachments { get; set; } = [];
    public List<Reaction> Reactions { get; set; } = [];
}

public sealed class Attachment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UploaderId { get; set; }
    public Guid? MessageId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public byte[] Data { get; set; } = [];
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Reaction
{
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public required string Emoji { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ---------- Other modules ----------

public sealed class Report
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReporterId { get; set; }
    public User? Reporter { get; set; }
    public Guid ReportedUserId { get; set; }
    public User? ReportedUser { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AiMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Global app configuration, edited by admin@talkie.com via the pencil button. Single row (Id = 1).</summary>
public sealed class AppSettings
{
    public int Id { get; set; } = 1;
    public string AppName { get; set; } = "Talkie";
    public string? Announcement { get; set; }
    public bool AllowCompanyCreation { get; set; } = true;
    public long StartingMoney { get; set; } = 1_000;
    public long StartingShards { get; set; } = 0;
    public int AfkShardsPerMinute { get; set; } = 1;
    public int AfkMaxHours { get; set; } = 24;
    public long MoneyPerShardBuy { get; set; } = 10;
    public long MoneyPerShardSell { get; set; } = 10;
    public long CommonBoxPrice { get; set; } = 200;
    public long RareBoxPrice { get; set; } = 500;
    public long ShardBoxPrice { get; set; } = 1_000;
}
