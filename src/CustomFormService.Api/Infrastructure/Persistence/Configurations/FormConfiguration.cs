using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomFormService.Api.Infrastructure.Persistence.Configurations;

public sealed class FormConfiguration : IEntityTypeConfiguration<Form>
{
    public void Configure(EntityTypeBuilder<Form> builder)
    {
        builder.ToTable("forms");
        builder.HasKey(form => form.Id).HasName("pk_forms");

        builder.Property(form => form.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(form => form.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
        builder.Property(form => form.Title).HasColumnName("title").HasColumnType("character varying(200)").HasMaxLength(200).IsRequired();
        builder.Property(form => form.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(form => form.Status).HasColumnName("status").HasColumnType("form_status").HasDefaultValueSql("'draft'::form_status").HasSentinel((FormStatus)(-1)).IsRequired();
        builder.Property(form => form.ResultVisibility).HasColumnName("result_visibility").HasColumnType("result_visibility").HasDefaultValueSql("'owner_only'::result_visibility").HasSentinel((ResultVisibility)(-1)).IsRequired();
        builder.Property(form => form.ResponseMode).HasColumnName("response_mode").HasColumnType("response_mode").HasDefaultValueSql("'single'::response_mode").HasSentinel((ResponseMode)(-1)).IsRequired();
        builder.Property(form => form.ResponseIdentity).HasColumnName("response_identity").HasColumnType("response_identity").HasDefaultValueSql("'anonymous'::response_identity").HasSentinel((ResponseIdentity)(-1)).IsRequired();
        builder.Property(form => form.SubmissionDeadlineAt).HasColumnName("submission_deadline_at").HasColumnType("timestamp with time zone");
        builder.Property(form => form.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
        builder.Property(form => form.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
        builder.Property(form => form.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        builder.HasOne(form => form.User)
            .WithMany(user => user.Forms)
            .HasForeignKey(form => form.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_forms_users_user_id");
    }
}
