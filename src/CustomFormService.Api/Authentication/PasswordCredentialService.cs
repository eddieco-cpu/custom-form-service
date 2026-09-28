using CustomFormService.Api.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CustomFormService.Api.Authentication;

public sealed class PasswordCredentialService
{
    private readonly PasswordHasher<User> _hasher;
    private readonly string _dummyHash;

    public PasswordCredentialService(IOptions<PasswordHasherOptions> options)
    {
        _hasher = new PasswordHasher<User>(options);
        _dummyHash = _hasher.HashPassword(new User(), "R4 synthetic dummy credential");
    }

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User? user, string password)
    {
        var candidate = user ?? new User();
        var hash = user?.PasswordHash ?? _dummyHash;
        var result = _hasher.VerifyHashedPassword(candidate, hash, password);
        return user?.PasswordHash is not null && result != PasswordVerificationResult.Failed;
    }
}
