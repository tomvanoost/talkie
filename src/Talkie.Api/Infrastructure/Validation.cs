using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Talkie.Api.Infrastructure;

public static partial class Validation
{
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256) return false;
        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email.Trim();
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValidColor(string? color) => color is not null && HexColor().IsMatch(color);

    /// <summary>At least 8 characters with a letter and a digit.</summary>
    public static string? PasswordProblem(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8) return "Password must be at least 8 characters.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "Password must contain a letter and a digit.";
        return null;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
