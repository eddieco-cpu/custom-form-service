using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomFormService.Api.Authentication;

// AllowAnonymous deliberately skips middleware authentication. Resolve only after the
// Form lookup, using the configured scheme's validators and this request's DB snapshot.
public sealed class StatisticsCredentialResolver(IOptionsMonitor<JwtBearerOptions> options)
{
    public async Task<StatisticsCredential> ResolveAsync(
        HttpRequest request, ApplicationDbContext database, CancellationToken cancellationToken)
    {
        if (!request.Cookies.TryGetValue("access_token", out var token))
            return new(StatisticsCredentialState.Missing);
        if (string.IsNullOrEmpty(token)) return new(StatisticsCredentialState.Invalid);

        var configured = options.Get(JwtBearerDefaults.AuthenticationScheme);
        ClaimsPrincipal? principal = null;
        var expired = false;
        foreach (var handler in configured.TokenHandlers)
        {
            var result = await handler.ValidateTokenAsync(token, configured.TokenValidationParameters.Clone());
            if (result.IsValid)
            {
                principal = new ClaimsPrincipal(result.ClaimsIdentity);
                break;
            }
            expired |= result.Exception is SecurityTokenExpiredException;
        }

        if (principal is null)
            return new(expired ? StatisticsCredentialState.Expired : StatisticsCredentialState.Invalid);
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id))
            return new(StatisticsCredentialState.Invalid);
        var user = await database.Users.AsNoTracking().Where(user => user.Id == id)
            .Select(user => new { user.Status }).SingleOrDefaultAsync(cancellationToken);
        if (user is null) return new(StatisticsCredentialState.Invalid);
        return new(user.Status == UserStatus.Active ? StatisticsCredentialState.Active : StatisticsCredentialState.Disabled, id);
    }
}

public enum StatisticsCredentialState { Active, Missing, Invalid, Expired, Disabled }
public sealed record StatisticsCredential(StatisticsCredentialState State, Guid? UserId = null);
