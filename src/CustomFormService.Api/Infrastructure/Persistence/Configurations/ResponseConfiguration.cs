using CustomFormService.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomFormService.Api.Infrastructure.Persistence.Configurations;

public sealed class ResponseConfiguration : IEntityTypeConfiguration<Response>
{
    public void Configure(EntityTypeBuilder<Response> builder)
    {
        builder.ToTable("responses");
        builder.HasKey(response => response.Id).HasName("pk_responses");
        builder.HasIndex(response => new { response.FormId, response.SubmittedAt })
            .HasDatabaseName("ix_responses_form_id_submitted_at");
        builder.HasIndex(response => new { response.UserId, response.FormId, response.SubmittedAt })
            .HasDatabaseName("ix_responses_user_id_form_id_submitted_at");

        builder.Property(response => response.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(response => response.FormId).HasColumnName("form_id").HasColumnType("uuid").IsRequired();
        builder.Property(response => response.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
        builder.Property(response => response.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();

        builder.HasOne(response => response.Form)
            .WithMany(form => form.Responses)
            .HasForeignKey(response => response.FormId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_responses_forms_form_id");
        builder.HasOne(response => response.User)
            .WithMany(user => user.Responses)
            .HasForeignKey(response => response.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_responses_users_user_id");
    }
}
