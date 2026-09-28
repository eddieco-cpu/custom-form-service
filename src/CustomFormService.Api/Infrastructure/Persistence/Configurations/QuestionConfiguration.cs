using CustomFormService.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomFormService.Api.Infrastructure.Persistence.Configurations;

public sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable("questions", table =>
        {
            table.HasCheckConstraint("ck_questions_sort_order_nonnegative", "sort_order >= 0");
            table.HasCheckConstraint("ck_questions_config_object", "jsonb_typeof(config) = 'object'");
        });
        builder.HasKey(question => question.Id).HasName("pk_questions");
        builder.HasIndex(question => new { question.FormId, question.SortOrder })
            .HasDatabaseName("ix_questions_form_id_sort_order");

        builder.Property(question => question.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(question => question.FormId).HasColumnName("form_id").HasColumnType("uuid").IsRequired();
        builder.Property(question => question.Type).HasColumnName("type").HasColumnType("question_type").IsRequired();
        builder.Property(question => question.Title).HasColumnName("title").HasColumnType("text").IsRequired();
        builder.Property(question => question.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(question => question.Required).HasColumnName("required").HasColumnType("boolean").HasDefaultValue(false).IsRequired();
        builder.Property(question => question.SortOrder).HasColumnName("sort_order").HasColumnType("integer").IsRequired();
        builder.Property(question => question.Config).HasColumnName("config").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(question => question.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
        builder.Property(question => question.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();

        builder.HasOne(question => question.Form)
            .WithMany(form => form.Questions)
            .HasForeignKey(question => question.FormId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_questions_forms_form_id");
    }
}
