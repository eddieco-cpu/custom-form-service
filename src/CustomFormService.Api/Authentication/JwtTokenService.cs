using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomFormService.Api.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomFormService.Api.Authentication;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private readonly JwtOptions _options = options.Value;

    public IssuedAccessToken Create(User user)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(_options.LifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())
            ]),
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        var value = handler.WriteToken(handler.CreateToken(descriptor));
        return new IssuedAccessToken(value, issuedAt, expiresAt);
    }
}

public sealed record IssuedAccessToken(string Value, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
