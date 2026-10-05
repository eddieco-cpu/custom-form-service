using System.Data;
using System.Text.Json;
using CustomFormService.Api.Authentication;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Contracts.Responses;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Infrastructure.Persistence;
using CustomFormService.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace CustomFormService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/forms/{formId}/responses")]
public sealed class ResponsesController(
    ApplicationDbContext database,
    CurrentUserContext currentUser,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("me/latest")]
    [ProducesResponseType<ResponseReceipt>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyLatest(string formId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(formId, out var id))
            return ApiErrors.Validation([RequestJson.Invalid("formId", "formId must be a UUID.")]);

        var userId = currentUser.User?.Id
            ?? throw new InvalidOperationException("Authentication must resolve the current user.");
        // The first SELECT establishes the snapshot, shared by the Form, Response and Answers reads.
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var form = await database.Forms.AsNoTracking()
            .Where(candidate => candidate.Id == id && candidate.DeletedAt == null)
            .Select(candidate => new { candidate.UserId, candidate.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (form is null) return FormNotFound();
        if (form.Status == FormStatus.Draft)
            return form.UserId == userId ? ResponseNotFound() : FormNotFound();

        var response = await database.Responses.AsNoTracking()
            .Where(candidate => candidate.FormId == id && candidate.UserId == userId)
            .OrderByDescending(candidate => candidate.SubmittedAt).ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id, candidate.FormId, candidate.SubmittedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (response is null) return ResponseNotFound();

        var answers = await database.Answers.AsNoTracking()
            .Where(answer => answer.ResponseId == response.Id)
            .OrderBy(answer => answer.Question.SortOrder).ThenBy(answer => answer.QuestionId)
            .Select(answer => new ResponseAnswerReceipt(answer.QuestionId, answer.Value))
            .ToArrayAsync(cancellationToken);
        var receipt = new ResponseReceipt(response.Id, response.FormId, response.SubmittedAt,
            answers.Select(answer => answer with { Value = answer.Value.Clone() }).ToArray());
        await transaction.CommitAsync(cancellationToken);
        return Ok(receipt);
    }

    [HttpPost]
    [ProducesResponseType<ResponseReceipt>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Submit(string formId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(formId, out var id))
            return ApiErrors.Validation([RequestJson.Invalid("formId", "formId must be a UUID.")]);

        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase))
            return ApiErrors.Validation([RequestJson.Invalid("body", "The request body must be application/json.")]);

        JsonDocument parsedBody;
        try
        {
            parsedBody = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return ApiErrors.Validation([RequestJson.Invalid("body", "The request body must be valid JSON.")]);
        }

        using (parsedBody)
        {
            // Only transport/unknown-field checks precede availability. Answers shape stays at stage 8.
            if (!SubmissionAnswerValidator.TryReadEnvelope(parsedBody.RootElement, out var answers, out var errors))
                return ApiErrors.Validation(errors);

            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            if (!await FormAggregateLock.TryAcquireActiveFormAsync(database, id, cancellationToken))
                return FormNotFound();

            var form = await database.Forms.AsNoTracking().AsSplitQuery()
                .Include(candidate => candidate.Questions).ThenInclude(question => question.Options)
                .SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.DeletedAt == null, cancellationToken);
            if (CheckAvailability(form) is { } availabilityError) return availabilityError;

            // Exactly one sample, after lock waiting and the current Published-state read.
            var submissionTime = timeProvider.GetUtcNow();
            var userId = currentUser.User?.Id
                ?? throw new InvalidOperationException("Authentication must resolve the current user.");
            if (await CheckSubmissionRulesAsync(form!, userId, submissionTime, cancellationToken) is { } ruleError)
                return ruleError;

            var questions = form!.Questions.Select(question => new SubmissionQuestionDefinition(
                question.Id, question.Type, question.Required, question.Config,
                question.Options.Select(option => option.Id).ToHashSet())).ToArray();
            if (!SubmissionAnswerValidator.TryNormalizeAnswers(answers, questions, out var normalized, out errors))
                return ApiErrors.Validation(errors);

            // Recheck stage 14 under the same lock, using the same timestamp rather than sampling again.
            var currentForm = await database.Forms.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (CheckAvailability(currentForm) is { } prewriteAvailabilityError) return prewriteAvailabilityError;
            if (await CheckSubmissionRulesAsync(currentForm!, userId, submissionTime, cancellationToken) is { } prewriteRuleError)
                return prewriteRuleError;

            var response = new Response
            {
                Id = Guid.NewGuid(), FormId = id, UserId = userId, SubmittedAt = submissionTime
            };
            foreach (var answer in normalized)
                response.Answers.Add(new Answer
                {
                    Id = Guid.NewGuid(), ResponseId = response.Id, QuestionId = answer.QuestionId,
                    Value = answer.Value.Clone()
                });
            database.Responses.Add(response);
            await database.SaveChangesAsync(cancellationToken);

            // Read what PostgreSQL actually stored (including numeric tokens and timestamp precision).
            var persisted = await database.Responses.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == response.Id, cancellationToken);
            var persistedAnswers = await database.Answers.AsNoTracking()
                .Where(answer => answer.ResponseId == response.Id)
                .OrderBy(answer => answer.Question.SortOrder).ThenBy(answer => answer.QuestionId)
                .Select(answer => new ResponseAnswerReceipt(answer.QuestionId, answer.Value))
                .ToArrayAsync(cancellationToken);
            var receipt = new ResponseReceipt(persisted.Id, persisted.FormId, persisted.SubmittedAt,
                persistedAnswers.Select(answer => answer with { Value = answer.Value.Clone() }).ToArray());
            await transaction.CommitAsync(cancellationToken);
            return StatusCode(StatusCodes.Status201Created, receipt);
        }
    }

    private async Task<ObjectResult?> CheckSubmissionRulesAsync(
        Form form, Guid userId, DateTimeOffset submissionTime, CancellationToken cancellationToken)
    {
        if (form.SubmissionDeadlineAt is { } deadline && submissionTime >= deadline)
            return ApiErrors.Result(StatusCodes.Status409Conflict, "SUBMISSION_DEADLINE_EXCEEDED",
                "The submission deadline has been reached.");
        if (form.ResponseMode == ResponseMode.Single && await database.Responses.AnyAsync(
            response => response.FormId == form.Id && response.UserId == userId, cancellationToken))
            return ApiErrors.Result(StatusCodes.Status409Conflict, "RESPONSE_ALREADY_SUBMITTED",
                "You have already submitted a response to this form.");
        return null;
    }

    private static ObjectResult? CheckAvailability(Form? form) =>
        form is null || form.DeletedAt is not null ? FormNotFound()
        : form.Status != FormStatus.Published
            ? ApiErrors.Result(StatusCodes.Status409Conflict, "FORM_NOT_SUBMITTABLE", "This form is not accepting responses.")
            : null;

    private static ObjectResult FormNotFound() =>
        ApiErrors.Result(StatusCodes.Status404NotFound, "FORM_NOT_FOUND", "The form was not found.");

    private static ObjectResult ResponseNotFound() =>
        ApiErrors.Result(StatusCodes.Status404NotFound, "RESPONSE_NOT_FOUND", "The response was not found.");
}
