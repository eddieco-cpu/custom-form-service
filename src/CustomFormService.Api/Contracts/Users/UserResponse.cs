using CustomFormService.Api.Domain.Entities;

namespace CustomFormService.Api.Contracts.Users;

public sealed record UserResponse(Guid Id, string Email, string DisplayName, string? AvatarUrl)
{
    public static UserResponse From(User user) =>
        new(user.Id, user.Email, user.DisplayName, user.AvatarUrl);
}
