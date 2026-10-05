using System.Globalization;
using System.Data;
using System.Text.Json;
using CustomFormService.Api.Authentication;
using CustomFormService.Api.Contracts.Errors;
using CustomFormService.Api.Contracts.Forms;
using CustomFormService.Api.Contracts.Responses;
using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Domain.Enums;
using CustomFormService.Api.Infrastructure.Persistence;
using CustomFormService.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CustomFormService.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/forms")]
public sealed class FormsController(
    ApplicationDbContext database,
    CurrentUserContext currentUser,
    TimeProvider timeProvider) : ControllerBase
{
    private static readonly HashSet<string> CreateProperties =
        new(
        [
            "title",
            "description",
            "responseMode",
            "responseIdentity",
            "resultVisibility",
            "submissionDeadlineAt",
            "questions"
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> RequiredCreateProperties =
        new(["title"], StringComparer.Ordinal);

    private static readonly HashSet<string> PatchProperties =
        new(
        [
            "title",
            "description",
            "responseMode",
            "responseIdentity",
            "submissionDeadlineAt",
            "questions"
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> RequiredPatchProperties =
        new(StringComparer.Ordinal);

    private static readonly HashSet<string> ListQueryProperties =
        new(["page", "pageSize", "status"], StringComparer.Ordinal);

    private static readonly HashSet<string> VisibilityProperties =
        new(["resultVisibility"], StringComparer.Ordinal);

    // The same sole property is required; Publish and Close have no body binding.
    private static readonly HashSet<string> RequiredVisibilityProperties =
        new(["resultVisibility"], StringComparer.Ordinal);

    [HttpPost]
    [ProducesResponseType<FormDetailResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(JsonElement body, CancellationToken cancellationToken)
    {
        if (!RequestJson.TryReadObject(
                body,
                CreateProperties,
                RequiredCreateProperties,
                out var properties,
                out var shapeErrors))
        {
            return ApiErrors.Validation(shapeErrors);
        }

        if (!TryReadCreateForm(properties, out var candidate, out var validationErrors))
        {
            return ApiErrors.Validation(validationErrors);
        }

        var questionsElement = properties.TryGetValue("questions", out var suppliedQuestions)
            ? suppliedQuestions
            : EmptyArray;
        if (!FormDefinitionValidator.TryCreateQuestions(questionsElement, out var questionDefinitions, out var definitionErrors))
        {
            return ApiErrors.Result(
                StatusCodes.Status400BadRequest,
                "FORM_DEFINITION_INVALID",
                "The form definition is invalid.",
                definitionErrors);
        }

        var owner = RequiredCurrentUser();
        var now = timeProvider.GetUtcNow();
        var form = new Form
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            User = owner,
            Title = candidate.Title,
            Description = candidate.Description,
            Status = FormStatus.Draft,
            ResponseMode = candidate.ResponseMode,
            ResponseIdentity = candidate.ResponseIdentity,
            ResultVisibility = candidate.ResultVisibility,
            SubmissionDeadlineAt = candidate.SubmissionDeadlineAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var questionDefinition in questionDefinitions)
        {
            var question = new Question
            {
                Id = Guid.NewGuid(),
                FormId = form.Id,
                Type = questionDefinition.Type,
                Title = questionDefinition.Title,
                Description = questionDefinition.Description,
                Required = questionDefinition.Required,
                SortOrder = questionDefinition.SortOrder,
                Config = questionDefinition.Config,
                CreatedAt = now,
                UpdatedAt = now
            };
            foreach (var optionDefinition in questionDefinition.Options)
            {
                question.Options.Add(new Option
                {
                    Id = Guid.NewGuid(),
                    QuestionId = question.Id,
                    Label = optionDefinition.Label,
                    Value = optionDefinition.Value,
                    SortOrder = optionDefinition.SortOrder,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            form.Questions.Add(question);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Attach(owner);
        database.Forms.Add(form);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, FormDetailResponse.From(form));
    }

    [HttpGet]
    [ProducesResponseType<FormListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryReadListQuery(out var query, out var validationErrors))
        {
            return ApiErrors.Validation(validationErrors);
        }

        var forms = database.Forms
            .AsNoTracking()
            .Where(form => form.UserId == RequiredCurrentUser().Id && form.DeletedAt == null);
        if (query.Status is not null)
        {
            forms = forms.Where(form => form.Status == query.Status);
        }

        var totalItems = await forms.CountAsync(cancellationToken);
        var rows = await forms
            .OrderByDescending(form => form.UpdatedAt)
            .ThenByDescending(form => form.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(form => new
            {
                form.Id,
                form.Title,
                form.Status,
                form.ResponseMode,
                form.ResponseIdentity,
                form.ResultVisibility,
                form.SubmissionDeadlineAt,
                Count = form.Responses.Select(response => response.UserId).Distinct().Count(),
                form.CreatedAt,
                form.UpdatedAt
            })
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(form => new FormSummaryResponse(
                form.Id,
                form.Title,
                FormDetailResponse.ToApiValue(form.Status),
                FormDetailResponse.ToApiValue(form.ResponseMode),
                FormDetailResponse.ToApiValue(form.ResponseIdentity),
                FormDetailResponse.ToApiValue(form.ResultVisibility),
                form.SubmissionDeadlineAt,
                form.Count,
                form.CreatedAt,
                form.UpdatedAt))
            .ToArray();

        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)query.PageSize);
        return Ok(new FormListResponse(items, new FormPaginationResponse(
            query.Page,
            query.PageSize,
            totalItems,
            totalPages)));
    }

    [HttpGet("{formId}/respondent-count")]
    [ProducesResponseType<RespondentCountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRespondentCount(string formId, CancellationToken cancellationToken)
    {
        if (!TryReadFormId(formId, out var id, out var validationError))
            return ApiErrors.Validation([validationError]);

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var form = await database.Forms.AsNoTracking()
            .Where(candidate => candidate.Id == id && candidate.DeletedAt == null)
            .Select(candidate => new { candidate.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (form is null) return FormNotFound();
        if (form.UserId != RequiredCurrentUser().Id)
            return ApiErrors.Result(StatusCodes.Status403Forbidden, "FORM_ACCESS_DENIED",
                "You do not have permission to access this form.");

        var count = await database.Responses.AsNoTracking()
            .Where(response => response.FormId == id)
            .Select(response => response.UserId).Distinct().CountAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new RespondentCountResponse(count));
    }

    [HttpGet("{formId}")]
    [ProducesResponseType<FormDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDetail(string formId, CancellationToken cancellationToken)
    {
        if (!TryReadFormId(formId, out var id, out var validationError))
        {
            return ApiErrors.Validation([validationError]);
        }

        var form = await LoadAvailableFormAsync(id, cancellationToken);
        if (form is null || (form.Status == FormStatus.Draft && form.UserId != RequiredCurrentUser().Id))
        {
            return FormNotFound();
        }

        return Ok(FormDetailResponse.From(form));
    }

    [HttpPatch("{formId}")]
    [ProducesResponseType<FormDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Patch(string formId, JsonElement body, CancellationToken cancellationToken)
    {
        if (!TryReadFormId(formId, out var id, out var validationError))
        {
            return ApiErrors.Validation([validationError]);
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return ApiErrors.Validation([RequestJson.Invalid("body", "The request body must be a JSON object.")]);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (!await FormAggregateLock.TryAcquireActiveFormAsync(database, id, cancellationToken))
        {
            return FormNotFound();
        }

        var form = await LoadEditableFormAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The locked form could not be loaded.");

        if (form.UserId != RequiredCurrentUser().Id)
        {
            return ApiErrors.Result(
                StatusCodes.Status403Forbidden,
                "FORM_ACCESS_DENIED",
                "You do not have permission to access this form.");
        }

        if (form.Status != FormStatus.Draft)
        {
            return ApiErrors.Result(
                StatusCodes.Status409Conflict,
                "FORM_NOT_EDITABLE",
                "Only draft forms can be edited.");
        }

        if (!RequestJson.TryReadObject(body, PatchProperties, RequiredPatchProperties, out var properties, out var shapeErrors))
        {
            return ApiErrors.Validation(shapeErrors);
        }

        if (properties.TryGetValue("questions", out var suppliedQuestions) && suppliedQuestions.ValueKind != JsonValueKind.Array)
        {
            return ApiErrors.Validation([RequestJson.Invalid("questions", "Questions must be an array.")]);
        }

        var now = timeProvider.GetUtcNow();
        var candidate = FormDefinitionCandidate.From(form);
        if (!TryApplyFormPatch(properties, candidate, out var fieldErrors))
        {
            return ApiErrors.Validation(fieldErrors);
        }

        if (properties.TryGetValue("questions", out var questionsElement) &&
            !DraftDefinitionDelta.TryApplyQuestions(
                candidate,
                questionsElement,
                out var definitionErrors))
        {
            return ApiErrors.Result(
                StatusCodes.Status400BadRequest,
                "FORM_DEFINITION_INVALID",
                "The form definition is invalid.",
                definitionErrors);
        }

        if (!FormDefinitionValidator.TryNormalizeQuestions(candidate.Questions, out var candidateErrors))
        {
            return ApiErrors.Result(
                StatusCodes.Status400BadRequest,
                "FORM_DEFINITION_INVALID",
                "The form definition is invalid.",
                candidateErrors);
        }

        var changes = DraftDefinitionChangeSet.Between(form, candidate);
        if (changes.HasChanges)
        {
            await DraftDefinitionWriter.SaveAsync(database, form, changes, now, cancellationToken);
        }

        // Read actual persisted JSONB/timestamp values, not identity-map entities.
        // Still inside the locked transaction; never return before commit succeeds.
        var persisted = await LoadAvailableFormAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The saved form could not be loaded.");
        var response = FormDetailResponse.From(persisted);
        await transaction.CommitAsync(cancellationToken);
        return Ok(response);
    }

    [HttpPost("{formId}/publish")]
    [ProducesResponseType<FormDetailResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> Publish(string formId, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(formId, publish: true, cancellationToken);

    [HttpPost("{formId}/close")]
    [ProducesResponseType<FormDetailResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> Close(string formId, CancellationToken cancellationToken) =>
        ChangeLifecycleAsync(formId, publish: false, cancellationToken);

    private async Task<IActionResult> ChangeLifecycleAsync(
        string formId, bool publish, CancellationToken cancellationToken)
    {
        if (!TryReadFormId(formId, out var id, out var validationError))
        {
            return ApiErrors.Validation([validationError]);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (!await FormAggregateLock.TryAcquireActiveFormAsync(database, id, cancellationToken))
        {
            return FormNotFound();
        }

        var form = await LoadEditableFormAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The locked form could not be loaded.");
        if (form.UserId != RequiredCurrentUser().Id)
        {
            return FormAccessDenied();
        }

        var expectedStatus = publish ? FormStatus.Draft : FormStatus.Published;
        if (form.Status != expectedStatus)
        {
            var code = publish && form.Status == FormStatus.Published
                ? "FORM_ALREADY_PUBLISHED"
                : !publish && form.Status == FormStatus.Closed
                    ? "FORM_ALREADY_CLOSED"
                    : "FORM_INVALID_STATE_TRANSITION";
            return ApiErrors.Result(StatusCodes.Status409Conflict, code, "The requested lifecycle transition is not allowed.");
        }

        // Sample time only after the shared lock and current state have been read.
        var now = timeProvider.GetUtcNow();
        if (publish && !FormDefinitionValidator.TryValidateForPublish(
                FormDefinitionCandidate.From(form), now, out var definitionErrors))
        {
            return ApiErrors.Result(StatusCodes.Status400BadRequest,
                "FORM_NOT_PUBLISHABLE", "The form is not ready to publish.", definitionErrors);
        }

        // Publish normalization changes the detached candidate only, never this graph.
        form.Status = publish ? FormStatus.Published : FormStatus.Closed;
        form.UpdatedAt = now;
        await database.SaveChangesAsync(cancellationToken);
        return await CommitFormDetailAsync(id, transaction, cancellationToken);
    }

    [HttpPatch("{formId}/result-visibility")]
    [ProducesResponseType<FormDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeResultVisibility(
        string formId, CancellationToken cancellationToken)
    {
        if (!TryReadFormId(formId, out var id, out var validationError))
        {
            return ApiErrors.Validation([validationError]);
        }

        if (!Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return ApiErrors.Validation([RequestJson.Invalid("body", "The request Content-Type must be application/json.")]);
        }

        // Only this policy endpoint reads JSON. Parse errors (including an absent
        // body) use the contract envelope before resource/owner checks, not MVC 415.
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return ApiErrors.Validation([RequestJson.Invalid("body", "The request body must be valid JSON.")]);
        }
        using var parsedBody = document;
        var body = parsedBody.RootElement;

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (!await FormAggregateLock.TryAcquireActiveFormAsync(database, id, cancellationToken))
        {
            return FormNotFound();
        }

        var form = await LoadEditableFormAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The locked form could not be loaded.");
        if (form.UserId != RequiredCurrentUser().Id)
        {
            return FormAccessDenied();
        }

        if (!RequestJson.TryReadObject(body, VisibilityProperties, RequiredVisibilityProperties,
                out var properties, out var shapeErrors))
        {
            return ApiErrors.Validation(shapeErrors);
        }

        var fieldErrors = new List<ApiErrorDetail>();
        if (!TryReadResultVisibility(properties, fieldErrors, out var visibility))
        {
            return ApiErrors.Validation(fieldErrors);
        }

        if (form.ResultVisibility != visibility)
        {
            form.ResultVisibility = visibility;
            form.UpdatedAt = timeProvider.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken);
        }

        return await CommitFormDetailAsync(id, transaction, cancellationToken);
    }

    private async Task<IActionResult> CommitFormDetailAsync(
        Guid id, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var persisted = await LoadAvailableFormAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The saved form could not be loaded.");
        var response = FormDetailResponse.From(persisted);
        await transaction.CommitAsync(cancellationToken);
        return Ok(response);
    }

    private static ObjectResult FormAccessDenied() =>
        ApiErrors.Result(StatusCodes.Status403Forbidden, "FORM_ACCESS_DENIED",
            "You do not have permission to access this form.");

    [HttpDelete("{formId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string formId, CancellationToken cancellationToken)
    {
        if (!TryReadFormId(formId, out var id, out var validationError))
        {
            return ApiErrors.Validation([validationError]);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        if (!await FormAggregateLock.TryAcquireActiveFormAsync(database, id, cancellationToken))
        {
            return FormNotFound();
        }

        var form = await database.Forms.SingleOrDefaultAsync(
            candidate => candidate.Id == id && candidate.DeletedAt == null,
            cancellationToken);
        if (form is null)
        {
            return FormNotFound();
        }

        if (form.UserId != RequiredCurrentUser().Id)
        {
            return ApiErrors.Result(
                StatusCodes.Status403Forbidden,
                "FORM_ACCESS_DENIED",
                "You do not have permission to access this form.");
        }

        var deletedAt = timeProvider.GetUtcNow();
        form.DeletedAt = deletedAt;
        form.UpdatedAt = deletedAt;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Form?> LoadAvailableFormAsync(Guid formId, CancellationToken cancellationToken) =>
        await database.Forms
            .AsNoTracking()
            .AsSplitQuery()
            .Include(form => form.User)
            .Include(form => form.Questions)
                .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(
                form => form.Id == formId && form.DeletedAt == null,
                cancellationToken);

    private async Task<Form?> LoadEditableFormAsync(Guid formId, CancellationToken cancellationToken) =>
        await database.Forms
            .AsSplitQuery()
            .Include(form => form.User)
            .Include(form => form.Questions)
                .ThenInclude(question => question.Options)
            .SingleOrDefaultAsync(
                form => form.Id == formId && form.DeletedAt == null,
                cancellationToken);

    private bool TryReadListQuery(out FormListQuery query, out IReadOnlyList<ApiErrorDetail> errors)
    {
        var validationErrors = new List<ApiErrorDetail>();
        foreach (var parameter in Request.Query)
        {
            if (!ListQueryProperties.Contains(parameter.Key))
            {
                validationErrors.Add(RequestJson.Invalid(parameter.Key, "This query parameter is not allowed."));
            }
            else if (parameter.Value.Count != 1)
            {
                validationErrors.Add(RequestJson.Invalid(parameter.Key, "This query parameter must appear once."));
            }
        }

        var page = TryReadPositiveQueryInteger("page", 1, validationErrors);
        var pageSize = TryReadPositiveQueryInteger("pageSize", 20, validationErrors);
        if (pageSize is < 1 or > 100)
        {
            validationErrors.Add(RequestJson.Invalid("pageSize", "pageSize must be between 1 and 100."));
        }

        FormStatus? status = null;
        if (Request.Query.TryGetValue("status", out var suppliedStatus) && suppliedStatus.Count == 1)
        {
            status = suppliedStatus[0] switch
            {
                "draft" => FormStatus.Draft,
                "published" => FormStatus.Published,
                "closed" => FormStatus.Closed,
                _ => null
            };
            if (status is null)
            {
                validationErrors.Add(RequestJson.Invalid("status", "status must be draft, published, or closed."));
            }
        }

        query = new FormListQuery(page, pageSize, status);
        errors = validationErrors;
        return validationErrors.Count == 0;
    }

    private int TryReadPositiveQueryInteger(string name, int defaultValue, List<ApiErrorDetail> errors)
    {
        if (!Request.Query.TryGetValue(name, out var values) || values.Count != 1)
        {
            return defaultValue;
        }

        if (!int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            errors.Add(RequestJson.Invalid(name, $"{name} must be a positive integer."));
            return defaultValue;
        }

        return value;
    }

    private static bool TryReadCreateForm(
        IReadOnlyDictionary<string, JsonElement> properties,
        out NewFormDefinition candidate,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        var validationErrors = new List<ApiErrorDetail>();
        candidate = default!;

        if (!TryReadRequiredTitle(properties, validationErrors, out var title) ||
            !TryReadDescription(properties, validationErrors, out var description) ||
            !TryReadResponseMode(properties, validationErrors, out var responseMode) ||
            !TryReadResponseIdentity(properties, validationErrors, out var responseIdentity) ||
            !TryReadResultVisibility(properties, validationErrors, out var resultVisibility) ||
            !TryReadSubmissionDeadline(properties, validationErrors, out var submissionDeadlineAt))
        {
            errors = validationErrors;
            return false;
        }

        if (properties.TryGetValue("questions", out var questions) && questions.ValueKind != JsonValueKind.Array)
        {
            validationErrors.Add(RequestJson.Invalid("questions", "Questions must be an array."));
        }

        if (validationErrors.Count > 0)
        {
            errors = validationErrors;
            return false;
        }

        candidate = new NewFormDefinition(title, description, responseMode, responseIdentity, resultVisibility, submissionDeadlineAt);
        errors = [];
        return true;
    }

    private static bool TryApplyFormPatch(
        IReadOnlyDictionary<string, JsonElement> properties,
        FormDefinitionCandidate form,
        out IReadOnlyList<ApiErrorDetail> errors)
    {
        var validationErrors = new List<ApiErrorDetail>();
        if (properties.TryGetValue("title", out var titleElement))
        {
            if (titleElement.ValueKind != JsonValueKind.String)
            {
                validationErrors.Add(RequestJson.Invalid("title", "Title must be a non-null string."));
            }
            else
            {
                var title = TextRules.Trim(titleElement.GetString()!);
                if (TextRules.CodePointCount(title) is 0 or > 200)
                {
                    validationErrors.Add(RequestJson.Invalid("title", "Title must contain 1 to 200 characters."));
                }
                else
                {
                    form.Title = title;
                }
            }
        }

        if (properties.TryGetValue("description", out var descriptionElement))
        {
            if (descriptionElement.ValueKind == JsonValueKind.Null)
            {
                form.Description = null;
            }
            else if (descriptionElement.ValueKind == JsonValueKind.String)
            {
                var description = TextRules.Trim(descriptionElement.GetString()!);
                form.Description = description.Length == 0 ? null : description;
            }
            else
            {
                validationErrors.Add(RequestJson.Invalid("description", "Description must be a string or null."));
            }
        }

        if (properties.TryGetValue("responseMode", out var responseModeElement))
        {
            form.ResponseMode = responseModeElement.ValueKind == JsonValueKind.String
                ? responseModeElement.GetString() switch
                {
                    "single" => ResponseMode.Single,
                    "multiple" => ResponseMode.Multiple,
                    _ => (ResponseMode)(-1)
                }
                : (ResponseMode)(-1);
            if (form.ResponseMode == (ResponseMode)(-1))
            {
                validationErrors.Add(RequestJson.Invalid("responseMode", "responseMode must be single or multiple."));
            }
        }

        if (properties.TryGetValue("responseIdentity", out var responseIdentityElement))
        {
            form.ResponseIdentity = responseIdentityElement.ValueKind == JsonValueKind.String
                ? responseIdentityElement.GetString() switch
                {
                    "anonymous" => ResponseIdentity.Anonymous,
                    "identified" => ResponseIdentity.Identified,
                    _ => (ResponseIdentity)(-1)
                }
                : (ResponseIdentity)(-1);
            if (form.ResponseIdentity == (ResponseIdentity)(-1))
            {
                validationErrors.Add(RequestJson.Invalid("responseIdentity", "responseIdentity must be anonymous or identified."));
            }
        }

        if (properties.TryGetValue("submissionDeadlineAt", out var deadlineElement))
        {
            if (deadlineElement.ValueKind == JsonValueKind.Null)
            {
                form.SubmissionDeadlineAt = null;
            }
            else if (deadlineElement.ValueKind == JsonValueKind.String &&
                     deadlineElement.GetString()!.EndsWith('Z') &&
                     DateTimeOffset.TryParse(
                         deadlineElement.GetString(),
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.RoundtripKind,
                         out var deadline) &&
                     deadline.Offset == TimeSpan.Zero)
            {
                form.SubmissionDeadlineAt = deadline;
            }
            else
            {
                validationErrors.Add(RequestJson.Invalid("submissionDeadlineAt", "submissionDeadlineAt must be a UTC ISO 8601 datetime or null."));
            }
        }

        errors = validationErrors;
        return validationErrors.Count == 0;
    }

    private static bool TryReadRequiredTitle(
        IReadOnlyDictionary<string, JsonElement> properties,
        List<ApiErrorDetail> errors,
        out string title)
    {
        title = string.Empty;
        if (!properties.TryGetValue("title", out var element) || element.ValueKind != JsonValueKind.String)
        {
            errors.Add(RequestJson.Invalid("title", "Title must be a non-null string."));
            return false;
        }

        title = TextRules.Trim(element.GetString()!);
        var count = TextRules.CodePointCount(title);
        if (count is 0 or > 200)
        {
            errors.Add(RequestJson.Invalid("title", "Title must contain 1 to 200 characters."));
            return false;
        }

        return true;
    }

    private static bool TryReadDescription(
        IReadOnlyDictionary<string, JsonElement> properties,
        List<ApiErrorDetail> errors,
        out string? description)
    {
        description = null;
        if (!properties.TryGetValue("description", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add(RequestJson.Invalid("description", "Description must be a string or null."));
            return false;
        }

        var normalized = TextRules.Trim(element.GetString()!);
        description = normalized.Length == 0 ? null : normalized;
        return true;
    }

    private static bool TryReadResponseMode(
        IReadOnlyDictionary<string, JsonElement> properties,
        List<ApiErrorDetail> errors,
        out ResponseMode responseMode)
    {
        responseMode = ResponseMode.Single;
        if (!properties.TryGetValue("responseMode", out var element))
        {
            return true;
        }

        responseMode = element.ValueKind == JsonValueKind.String
            ? element.GetString() switch
        {
            "single" => ResponseMode.Single,
            "multiple" => ResponseMode.Multiple,
            _ => (ResponseMode)(-1)
        }
            : (ResponseMode)(-1);
        if (responseMode != (ResponseMode)(-1))
        {
            return true;
        }

        errors.Add(RequestJson.Invalid("responseMode", "responseMode must be single or multiple."));
        return false;
    }

    private static bool TryReadResponseIdentity(
        IReadOnlyDictionary<string, JsonElement> properties,
        List<ApiErrorDetail> errors,
        out ResponseIdentity responseIdentity)
    {
        responseIdentity = ResponseIdentity.Anonymous;
        if (!properties.TryGetValue("responseIdentity", out var element))
        {
            return true;
        }

        responseIdentity = element.ValueKind == JsonValueKind.String
            ? element.GetString() switch
        {
            "anonymous" => ResponseIdentity.Anonymous,
            "identified" => ResponseIdentity.Identified,
            _ => (ResponseIdentity)(-1)
        }
            : (ResponseIdentity)(-1);
        if (responseIdentity != (ResponseIdentity)(-1))
        {
            return true;
        }

        errors.Add(RequestJson.Invalid("responseIdentity", "responseIdentity must be anonymous or identified."));
        return false;
    }

    private static bool TryReadResultVisibility(
        IReadOnlyDictionary<string, JsonElement> properties,
        List<ApiErrorDetail> errors,
        out ResultVisibility resultVisibility)
    {
        resultVisibility = ResultVisibility.OwnerOnly;
        if (!properties.TryGetValue("resultVisibility", out var element))
        {
            return true;
        }

        resultVisibility = element.ValueKind == JsonValueKind.String
            ? element.GetString() switch
        {
            "owner_only" => ResultVisibility.OwnerOnly,
            "respondents" => ResultVisibility.Respondents,
            "public" => ResultVisibility.Public,
            _ => (ResultVisibility)(-1)
        }
            : (ResultVisibility)(-1);
        if (resultVisibility != (ResultVisibility)(-1))
        {
            return true;
        }

        errors.Add(RequestJson.Invalid("resultVisibility", "resultVisibility is invalid."));
        return false;
    }

    private static bool TryReadSubmissionDeadline(
        IReadOnlyDictionary<string, JsonElement> properties,
        List<ApiErrorDetail> errors,
        out DateTimeOffset? deadline)
    {
        deadline = null;
        if (!properties.TryGetValue("submissionDeadlineAt", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String ||
            !element.GetString()!.EndsWith('Z') ||
            !DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            parsed.Offset != TimeSpan.Zero)
        {
            errors.Add(RequestJson.Invalid("submissionDeadlineAt", "submissionDeadlineAt must be a UTC ISO 8601 datetime or null."));
            return false;
        }

        deadline = parsed;
        return true;
    }

    private static bool TryReadFormId(string formId, out Guid id, out ApiErrorDetail validationError)
    {
        if (Guid.TryParse(formId, out id))
        {
            validationError = default!;
            return true;
        }

        validationError = RequestJson.Invalid("formId", "formId must be a UUID.");
        return false;
    }

    private static ObjectResult FormNotFound() =>
        ApiErrors.Result(StatusCodes.Status404NotFound, "FORM_NOT_FOUND", "The form was not found.");

    private User RequiredCurrentUser() =>
        currentUser.User ?? throw new InvalidOperationException("Authentication did not resolve the current user.");

    private static JsonElement EmptyArray => JsonDocument.Parse("[]").RootElement.Clone();
}

public sealed record NewFormDefinition(
    string Title,
    string? Description,
    ResponseMode ResponseMode,
    ResponseIdentity ResponseIdentity,
    ResultVisibility ResultVisibility,
    DateTimeOffset? SubmissionDeadlineAt);

public sealed record FormListQuery(int Page, int PageSize, FormStatus? Status);
