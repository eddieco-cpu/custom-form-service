using System.Text.Json;
using CustomFormService.Api.Authentication;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Contracts.Users;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Infrastructure.Persistence;
using CustomFormService.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomFormService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/users")]
public sealed class UsersController(
    ApplicationDbContext database,
    CurrentUserContext currentUser,
    TimeProvider timeProvider) : ControllerBase
{
    private static readonly HashSet<string> PatchProperties =
        new(["displayName"], StringComparer.Ordinal);

    [HttpGet("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public IActionResult GetMe()
    {
        return Ok(UserResponse.From(RequiredCurrentUser()));
    }

    [HttpPatch("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PatchMe(JsonElement body, CancellationToken cancellationToken)
    {
        if (!RequestJson.TryReadObject(
                body,
                PatchProperties,
                new HashSet<string>(StringComparer.Ordinal),
                out var properties,
                out var shapeErrors))
        {
            return ApiErrors.Validation(shapeErrors);
        }

        var authenticatedUser = RequiredCurrentUser();
        if (!properties.TryGetValue("displayName", out _))
        {
            return Ok(UserResponse.From(authenticatedUser));
        }

        if (!RequestJson.TryGetString(properties, "displayName", out var rawDisplayName, out var typeError))
        {
            return ApiErrors.Validation([typeError!]);
        }

        var displayName = TextRules.Trim(rawDisplayName);
        var length = TextRules.CodePointCount(displayName);
        if (length is 0 or > 100)
        {
            return ApiErrors.Validation(
            [
                RequestJson.Invalid("displayName", "Display name must contain 1 to 100 characters.")
            ]);
        }

        if (string.Equals(displayName, authenticatedUser.DisplayName, StringComparison.Ordinal))
        {
            return Ok(UserResponse.From(authenticatedUser));
        }

        var user = await database.Users.SingleAsync(
            candidate => candidate.Id == authenticatedUser.Id,
            cancellationToken);
        user.DisplayName = displayName;
        user.UpdatedAt = timeProvider.GetUtcNow();
        await database.SaveChangesAsync(cancellationToken);

        return Ok(UserResponse.From(user));
    }

    private User RequiredCurrentUser() =>
        currentUser.User ?? throw new InvalidOperationException("Authentication did not resolve the current user.");
}
