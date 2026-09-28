using CustomFormService.Api.Domain.Enums;

namespace CustomFormService.Api.Domain.Entities;

public sealed class Form
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public FormStatus Status { get; set; }

    public ResultVisibility ResultVisibility { get; set; }

    public ResponseMode ResponseMode { get; set; }

    public ResponseIdentity ResponseIdentity { get; set; }

    public DateTimeOffset? SubmissionDeadlineAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public User User { get; set; } = null!;

    public ICollection<Question> Questions { get; } = [];

    public ICollection<Response> Responses { get; } = [];
}
