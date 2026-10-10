using System.Data;
using CustomFormService.Api.Authentication;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Contracts.Statistics;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Infrastructure.Persistence;
using CustomFormService.Api.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomFormService.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/forms")]
public sealed class StatisticsController(ApplicationDbContext database, StatisticsQuery query,
    StatisticsCredentialResolver credentials) : ControllerBase
{
    [HttpGet("{formId}/statistics")]
    [ProducesResponseType<StatisticsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatistics(string formId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(formId, out var id))
            return ApiErrors.Validation([new("formId", "INVALID_VALUE", "formId must be a UUID.")]);

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var form = await query.FindFormAsync(id, cancellationToken);
        if (form is null) return NotAvailable();
        if (form.Status is FormStatus.Draft or FormStatus.Published)
        {
            var credential = await credentials.ResolveAsync(Request, database, cancellationToken);
            return credential.State == StatisticsCredentialState.Active &&
                (form.Status == FormStatus.Published || credential.UserId == form.UserId)
                ? ApiErrors.Result(409, "FORM_STATISTICS_NOT_AVAILABLE", "Statistics are not available for this form.")
                : NotAvailable();
        }
        if (form.Status != FormStatus.Closed) throw new InvalidOperationException("Unknown Form status.");
        if (form.ResultVisibility != ResultVisibility.Public)
        {
            var credential = await credentials.ResolveAsync(Request, database, cancellationToken);
            var error = credential.State switch
            {
                StatisticsCredentialState.Missing => ApiErrors.Result(401, "AUTH_REQUIRED", "Authentication is required."),
                StatisticsCredentialState.Invalid => ApiErrors.Result(401, "AUTH_TOKEN_INVALID", "The access token is invalid."),
                StatisticsCredentialState.Expired => ApiErrors.Result(401, "AUTH_TOKEN_EXPIRED", "The access token has expired."),
                StatisticsCredentialState.Disabled => ApiErrors.UserDisabled(),
                StatisticsCredentialState.Active => null,
                _ => throw new InvalidOperationException("Unknown credential state.")
            };
            if (error is not null) return error;
            var allowed = credential.UserId == form.UserId ||
                (form.ResultVisibility == ResultVisibility.Respondents && await database.Responses.AsNoTracking()
                    .AnyAsync(response => response.FormId == id && response.UserId == credential.UserId, cancellationToken));
            if (!allowed) return ApiErrors.Result(403, "STATISTICS_ACCESS_DENIED", "Access to these statistics is denied.");
        }
        var result = await query.ReadAsync(form, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(result);
    }

    private static ObjectResult NotAvailable() => ApiErrors.Result(404, "FORM_NOT_FOUND", "The form was not found.");
}
