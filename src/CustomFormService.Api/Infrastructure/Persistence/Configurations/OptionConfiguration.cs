using CustomFormService.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomFormService.Api.Infrastructure.Persistence.Configurations;

public sealed class OptionConfiguration : IEntityTypeConfiguration<Option>
{
    public void Configure(EntityTypeBuilder<Option> builder)
    {
        builder.ToTable("options", table =>
        {
            table.HasCheckConstraint("ck_options_sort_order_nonnegative", "sort_order >= 0");
        });
        builder.HasKey(option => option.Id).HasName("pk_options");
        builder.HasAlternateKey(option => new { option.QuestionId, option.Value })
            .HasName("uq_options_question_id_value");
        builder.HasIndex(option => new { option.QuestionId, option.SortOrder })
            .HasDatabaseName("ix_options_question_id_sort_order");

        builder.Property(option => option.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(option => option.QuestionId).HasColumnName("question_id").HasColumnType("uuid").IsRequired();
        builder.Property(option => option.Label).HasColumnName("label").HasColumnType("text").IsRequired();
        builder.Property(option => option.Value).HasColumnName("value").HasColumnType("character varying(255)").HasMaxLength(255).IsRequired();
        builder.Property(option => option.SortOrder).HasColumnName("sort_order").HasColumnType("integer").IsRequired();
        builder.Property(option => option.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
        builder.Property(option => option.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();

        builder.HasOne(option => option.Question)
            .WithMany(question => question.Options)
            .HasForeignKey(option => option.QuestionId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_options_questions_question_id");
    }
}
