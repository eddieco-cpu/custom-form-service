namespace CustomFormService.Api.Domain.Entities;

public sealed class Response
{
    public Guid Id { get; set; }

    public Guid FormId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public Form Form { get; set; } = null!;

    public User User { get; set; } = null!;

    public ICollection<Answer> Answers { get; } = [];
}
