using Talkie.Data;
using Talkie.Shared;

namespace Talkie.Api.Infrastructure;

public static class Mapping
{
    public static string? AvatarUrl(User u) =>
        u.AvatarContentType is null ? null : $"/api/users/{u.Id}/avatar?v={u.AvatarVersion}";

    public static RoleDto ToDto(this Role r) => new(r.Id, r.Name, r.Color);

    public static UserSummaryDto ToSummary(this User u) =>
        new(u.Id, u.Email, u.DisplayName, AvatarUrl(u), u.ActiveEffect, u.Company?.Name ?? "");

    public static MemberDto ToMember(this User u) => new(
        u.Id, u.Email, u.Username, u.DisplayName, u.FirstName, u.LastName, u.IsOwner, AvatarUrl(u), u.ActiveEffect, u.CreatedAt,
        u.Roles.Where(r => r.Role is not null).Select(r => r.Role!.ToDto()).OrderBy(r => r.Name).ToList());

    public static UserProfileDto ToProfile(this User u) => new(
        u.Id, u.Email, u.Username, u.DisplayName, u.FirstName, u.LastName, u.CompanyId, u.Company?.Name ?? "",
        u.IsOwner, CurrentUser.IsAdmin(u), AvatarUrl(u), u.ActiveEffect,
        u.Roles.Where(r => r.Role is not null).Select(r => r.Role!.ToDto()).ToList());

    public static bool IsImage(string contentType) => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static AttachmentDto ToDto(this Attachment a) =>
        new(a.Id, a.FileName, a.ContentType, a.Size, $"/api/attachments/{a.Id}", IsImage(a.ContentType));
}
