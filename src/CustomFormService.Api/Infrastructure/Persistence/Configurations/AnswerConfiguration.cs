using CustomFormService.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomFormService.Api.Infrastructure.Persistence.Configurations;

public sealed class AnswerConfiguration : IEntityTypeConfiguration<Answer>
{
    public void Configure(EntityTypeBuilder<Answer> builder)
    {
        builder.ToTable("answers");
        builder.HasKey(answer => answer.Id).HasName("pk_answers");
        builder.HasAlternateKey(answer => new { answer.ResponseId, answer.QuestionId })
            .HasName("uq_answers_response_id_question_id");
        builder.HasIndex(answer => answer.QuestionId)
            .HasDatabaseName("ix_answers_question_id");

        builder.Property(answer => answer.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(answer => answer.ResponseId).HasColumnName("response_id").HasColumnType("uuid").IsRequired();
        builder.Property(answer => answer.QuestionId).HasColumnName("question_id").HasColumnType("uuid").IsRequired();
        builder.Property(answer => answer.Value).HasColumnName("value").HasColumnType("jsonb").IsRequired();

        builder.HasOne(answer => answer.Response)
            .WithMany(response => response.Answers)
            .HasForeignKey(answer => answer.ResponseId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_answers_responses_response_id");
        builder.HasOne(answer => answer.Question)
            .WithMany(question => question.Answers)
            .HasForeignKey(answer => answer.QuestionId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_answers_questions_question_id");
    }
}
