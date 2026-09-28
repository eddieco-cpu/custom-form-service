using CustomFormService.Api.Domain.Entities;
using CustomFormService.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomFormService.Api.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id).HasName("pk_users");
        builder.HasAlternateKey(user => user.Email).HasName("uq_users_email");

        builder.Property(user => user.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(user => user.Email).HasColumnName("email").HasColumnType("character varying(254)").HasMaxLength(254).IsRequired();
        builder.Property(user => user.DisplayName).HasColumnName("display_name").HasColumnType("character varying(100)").HasMaxLength(100).IsRequired();
        builder.Property(user => user.AvatarUrl).HasColumnName("avatar_url").HasColumnType("text");
        builder.Property(user => user.Status).HasColumnName("status").HasColumnType("user_status").HasDefaultValueSql("'active'::user_status").HasSentinel((UserStatus)(-1)).IsRequired();
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasColumnType("character varying(255)").HasMaxLength(255);
        builder.Property(user => user.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
        builder.Property(user => user.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
    }
}
