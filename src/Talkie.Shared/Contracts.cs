namespace Talkie.Shared;

// ---------- Auth ----------
public sealed record LoginRequest(string Email, string Password);

public sealed record RoleInput(string Name, string Color);

public sealed record CreateCompanyRequest(
    string CompanyName,
    string OwnerEmail,
    string OwnerPassword,
    string Username,
    string DisplayName,
    List<RoleInput> Roles);

public sealed record AuthResponse(string Token, UserProfileDto User);

// ---------- Users / profile ----------
public sealed record UserProfileDto(
    Guid Id,
    string Email,
    string Username,
    string DisplayName,
    string? FirstName,
    string? LastName,
    Guid CompanyId,
    string CompanyName,
    bool IsOwner,
    bool IsAdmin,
    string? AvatarUrl,
    string? ActiveEffect,
    List<RoleDto> Roles);

public sealed record UpdateProfileRequest(string Username, string DisplayName, string? FirstName, string? LastName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UserSummaryDto(Guid Id, string Email, string DisplayName, string? AvatarUrl, string? ActiveEffect, string CompanyName);

// ---------- Company ----------
public sealed record RoleDto(Guid Id, string Name, string Color);

public sealed record MemberDto(
    Guid Id,
    string Email,
    string Username,
    string DisplayName,
    string? FirstName,
    string? LastName,
    bool IsOwner,
    string? AvatarUrl,
    string? ActiveEffect,
    DateTimeOffset CreatedAt,
    List<RoleDto> Roles);

public sealed record CompanyDto(Guid Id, string Name, int MemberCount, List<RoleDto> Roles);

public sealed record AddMemberRequest(string FirstName, string LastName, string Email, List<Guid> RoleIds);

public sealed record AddMemberResponse(MemberDto Member, string TemporaryPassword);

public sealed record UpdateMemberRequest(string FirstName, string LastName, string DisplayName, string Username, List<Guid> RoleIds);

// ---------- Chat ----------
public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long Size, string Url, bool IsImage);

public sealed record ReactionDto(string Emoji, int Count, bool ReactedByMe);

public sealed record ReplyPreviewDto(Guid Id, string SenderName, string Content);

public sealed record MessageDto(
    Guid Id,
    Guid? ConversationId,
    Guid? ChannelId,
    Guid SenderId,
    string SenderName,
    string? SenderAvatarUrl,
    string? SenderEffect,
    string? SenderRoleColor,
    string Content,
    DateTimeOffset CreatedAt,
    bool IsDeleted,
    ReplyPreviewDto? ReplyTo,
    List<AttachmentDto> Attachments,
    List<ReactionDto> Reactions);

public sealed record ConversationDto(
    Guid Id,
    bool IsGroup,
    string Name,
    Guid? OwnerId,
    List<UserSummaryDto> Members,
    string? LastMessagePreview,
    DateTimeOffset LastActivityAt);

public sealed record StartDirectMessageRequest(string Email);

public sealed record CreateGroupRequest(string Name, List<string> Emails);

public sealed record SendMessageRequest(string Content, Guid? ReplyToId, List<Guid> AttachmentIds);

public sealed record ReactRequest(string Emoji);

public sealed record UploadResultDto(Guid Id, string FileName, string ContentType, long Size, bool IsImage);

public sealed record SearchResultDto(MessageDto Message, string Location, double Score);

// ---------- Servers ----------
public sealed record ChannelPermissionDto(Guid? RoleId, string RoleName, string RoleColor, bool CanSendMessages, bool CanDeleteMessages, bool CanReact);

public sealed record ChannelDto(
    Guid Id,
    Guid ServerId,
    string Name,
    string? Description,
    List<RoleDto> VisibleToRoles,
    List<ChannelPermissionDto> Permissions,
    bool CanSendMessages,
    bool CanDeleteMessages,
    bool CanReact);

public sealed record ServerDto(Guid Id, string Name, bool CanManage, List<ChannelDto> Channels);

public sealed record ChannelPermissionInput(Guid? RoleId, bool CanSendMessages, bool CanDeleteMessages, bool CanReact);

public sealed record SaveChannelRequest(string Name, string? Description, List<Guid> VisibleToRoleIds, List<ChannelPermissionInput> Permissions);

// ---------- AI ----------
public sealed record AiMessageDto(Guid Id, string Role, string Content, DateTimeOffset CreatedAt);

public sealed record AiAskRequest(string Message);

// ---------- Reports ----------
public sealed record CreateReportRequest(string ReportedEmail, string Reason);

public sealed record ReportDto(Guid Id, UserSummaryDto Reporter, UserSummaryDto Reported, string Reason, DateTimeOffset CreatedAt);

// ---------- Stats ----------
public sealed record StatsDto(
    long Money,
    long Shards,
    long PlaytimeSeconds,
    long MessagesSent,
    long BoxesOpened,
    int ShardMultiplier,
    bool IsAfk);

public sealed record LeaderboardEntryDto(UserSummaryDto User, long Money, long Shards, long MessagesSent);

// ---------- Shop ----------
public sealed record WalletDto(long Money, long Shards, int ShardMultiplier, string? ActiveEffect, List<string> OwnedEffects, List<string> OwnedBoosters, List<UnopenedBoxDto> UnopenedBoxes);

public sealed record ExchangeRequest(long Amount);

public sealed record ExchangeRatesDto(long MoneyPerShardBuy, long MoneyPerShardSell);

public sealed record UnopenedBoxDto(Guid Id, string BoxKey, DateTimeOffset PurchasedAt);

public sealed record BoxOpenResultDto(Guid BoxId, string BoxKey, long Amount, Currency Currency, WalletDto Wallet);

public sealed record ShopDto(ExchangeRatesDto Rates, WalletDto Wallet, long CommonPrice, long RarePrice, long ShardPrice);

// ---------- AFK ----------
public sealed record AfkStatusDto(bool IsAfk, DateTimeOffset? Since, long ShardsEarnedThisSession, int Multiplier, int ShardsPerMinute, long TotalShards);

// ---------- Admin ----------
public sealed record AppSettingsDto(
    string AppName,
    string? Announcement,
    bool AllowCompanyCreation,
    long StartingMoney,
    long StartingShards,
    int AfkShardsPerMinute,
    int AfkMaxHours,
    long MoneyPerShardBuy,
    long MoneyPerShardSell,
    long CommonBoxPrice,
    long RareBoxPrice,
    long ShardBoxPrice);

public sealed record PublicSettingsDto(string AppName, string? Announcement, bool AllowCompanyCreation);

public sealed record AdminCompanyDto(Guid Id, string Name, string OwnerEmail, int MemberCount, DateTimeOffset CreatedAt);

public sealed record GrantRequest(string Email, long Money, long Shards);

public sealed record ApiError(string Message);
