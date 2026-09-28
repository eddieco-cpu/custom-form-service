using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public UserStatus Status { get; set; }

    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<UserIdentity> Identities { get; } = [];

    public ICollection<Form> Forms { get; } = [];

    public ICollection<Response> Responses { get; } = [];
}
