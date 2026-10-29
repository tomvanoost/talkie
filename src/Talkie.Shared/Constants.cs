namespace Talkie.Shared;

public static class TalkieConstants
{
    /// <summary>The built-in company. No other company may use this name.</summary>
    public const string DefaultCompanyName = "Talkie";

    /// <summary>The account that can configure the whole app (pencil button) and remove reports.</summary>
    public const string AdminEmail = "admin@talkie.com";

    public const string OwnerEmail = "owner@talkie.com";

    public const int EmbeddingDimensions = 256;

    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
    public const long MaxAvatarBytes = 2 * 1024 * 1024;
}

public static class ClaimNames
{
    public const string CompanyId = "company_id";
    public const string IsAdmin = "is_admin";
    public const string IsOwner = "is_owner";
    public const string DisplayName = "display_name";
}
