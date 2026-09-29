using System.Text.Json;
using CustomFormService.Api.Authentication;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Contracts.Users;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Infrastructure.Persistence;
using CustomFormService.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CustomFormService.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    ApplicationDbContext database,
    PasswordCredentialService passwords,
    JwtTokenService tokens,
    TimeProvider timeProvider,
    IOptions<AccessTokenCookieOptions> cookieOptions) : ControllerBase
{
    private static readonly HashSet<string> RegisterProperties =
        new(["email", "password", "displayName"], StringComparer.Ordinal);

    private static readonly HashSet<string> LoginProperties =
        new(["email", "password"], StringComparer.Ordinal);

    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(JsonElement body, CancellationToken cancellationToken)
    {
        if (!RequestJson.TryReadObject(
                body,
                RegisterProperties,
                RegisterProperties,
                out var properties,
                out var shapeErrors))
        {
            return ApiErrors.Validation(shapeErrors);
        }

        var typeErrors = new List<ApiErrorDetail>();
        var emailIsString = RequestJson.TryGetString(properties, "email", out var rawEmail, out var emailTypeError);
        var passwordIsString = RequestJson.TryGetString(properties, "password", out var password, out var passwordTypeError);
        var displayNameIsString = RequestJson.TryGetString(properties, "displayName", out var rawDisplayName, out var displayNameTypeError);
        AddError(typeErrors, emailTypeError);
        AddError(typeErrors, passwordTypeError);
        AddError(typeErrors, displayNameTypeError);
        if (!emailIsString || !passwordIsString || !displayNameIsString)
        {
            return ApiErrors.Validation(typeErrors);
        }

        var email = TextRules.NormalizeEmail(rawEmail);
        var displayName = TextRules.Trim(rawDisplayName);
        var valueErrors = ValidateRegistration(email, password, displayName);
        if (valueErrors.Count > 0)
        {
            return ApiErrors.Validation(valueErrors);
        }

        if (await database.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return ApiErrors.EmailAlreadyExists();
        }

        var now = timeProvider.GetUtcNow();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwords.Hash(user, password);
        database.Users.Add(user);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEmailUniqueViolation(exception))
        {
            return ApiErrors.EmailAlreadyExists();
        }

        return StatusCode(StatusCodes.Status201Created, UserResponse.From(user));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(JsonElement body, CancellationToken cancellationToken)
    {
        if (!RequestJson.TryReadObject(
                body,
                LoginProperties,
                LoginProperties,
                out var properties,
                out var shapeErrors))
        {
            return ApiErrors.Validation(shapeErrors);
        }

        var typeErrors = new List<ApiErrorDetail>();
        var emailIsString = RequestJson.TryGetString(properties, "email", out var rawEmail, out var emailTypeError);
        var passwordIsString = RequestJson.TryGetString(properties, "password", out var password, out var passwordTypeError);
        AddError(typeErrors, emailTypeError);
        AddError(typeErrors, passwordTypeError);
        if (!emailIsString || !passwordIsString)
        {
            return ApiErrors.Validation(typeErrors);
        }

        var requiredErrors = new List<ApiErrorDetail>();
        if (rawEmail.Length == 0)
        {
            requiredErrors.Add(RequestJson.Invalid("email", "Email must not be empty."));
        }

        if (password.Length == 0)
        {
            requiredErrors.Add(RequestJson.Invalid("password", "Password must not be empty."));
        }

        if (requiredErrors.Count > 0)
        {
            return ApiErrors.Validation(requiredErrors);
        }

        var email = TextRules.NormalizeEmail(rawEmail);
        var user = await database.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == email,
            cancellationToken);
        if (!passwords.Verify(user, password))
        {
            return ApiErrors.InvalidCredentials();
        }

        if (user!.Status != UserStatus.Active)
        {
            return ApiErrors.UserDisabled();
        }

        AccessTokenCookie.Append(Response, tokens.Create(user), cookieOptions.Value);
        return Ok(UserResponse.From(user));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        AccessTokenCookie.Delete(Response, cookieOptions.Value);
        return NoContent();
    }

    private static List<ApiErrorDetail> ValidateRegistration(
        string email,
        string password,
        string displayName)
    {
        var errors = new List<ApiErrorDetail>();
        if (TextRules.CodePointCount(email) > 254 || !TextRules.IsValidEmail(email))
        {
            errors.Add(RequestJson.Invalid("email", "Email must be valid and no longer than 254 characters."));
        }

        var displayNameLength = TextRules.CodePointCount(displayName);
        if (displayNameLength is 0 or > 100)
        {
            errors.Add(RequestJson.Invalid("displayName", "Display name must contain 1 to 100 characters."));
        }

        var passwordLength = TextRules.CodePointCount(password);
        if (passwordLength is < 10 or > 128)
        {
            errors.Add(RequestJson.Invalid("password", "Password must contain 10 to 128 characters."));
        }

        return errors;
    }

    private static bool IsEmailUniqueViolation(DbUpdateException exception) =>
        exception.GetBaseException() is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "uq_users_email"
        };

    private static void AddError(List<ApiErrorDetail> errors, ApiErrorDetail? error)
    {
        if (error is not null)
        {
            errors.Add(error);
        }
    }
}
