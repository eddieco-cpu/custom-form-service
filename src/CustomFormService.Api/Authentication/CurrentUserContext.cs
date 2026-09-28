using CustomFormService.Api.Domain.Entities;

namespace CustomFormService.Api.Authentication;

public sealed class CurrentUserContext
{
    public User? User { get; set; }
}
