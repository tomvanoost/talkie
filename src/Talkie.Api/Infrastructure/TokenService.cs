using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Talkie.Data;
using Talkie.Shared;

namespace Talkie.Api.Infrastructure;

public sealed class AuthOptions
{
    public string Issuer { get; set; } = "talkie";
    public string Audience { get; set; } = "talkie";
    public string SigningKey { get; set; } = "";
    public int TokenLifetimeHours { get; set; } = 168;

    public SymmetricSecurityKey GetKey()
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be configured and at least 32 bytes long.");
        }
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
    }
}

public sealed class TokenService(AuthOptions options)
{
    public string CreateToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimNames.DisplayName, user.DisplayName),
            new(ClaimNames.CompanyId, user.CompanyId.ToString()),
        };
        if (user.IsOwner) claims.Add(new Claim(ClaimNames.IsOwner, "true"));
        if (CurrentUser.IsAdmin(user)) claims.Add(new Claim(ClaimNames.IsAdmin, "true"));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(options.TokenLifetimeHours),
            SigningCredentials = new SigningCredentials(options.GetKey(), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
