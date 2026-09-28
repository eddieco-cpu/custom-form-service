using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Domain.Entities;

public sealed class UserIdentity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public IdentityProvider Provider { get; set; }

    public string ProviderUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
