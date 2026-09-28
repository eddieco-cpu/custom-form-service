using CustomFormService.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomFormService.Api.Infrastructure.Persistence.Configurations;

public sealed class UserIdentityConfiguration : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> builder)
    {
        builder.ToTable("user_identities");
        builder.HasKey(identity => identity.Id).HasName("pk_user_identities");
        builder.HasAlternateKey(identity => new { identity.Provider, identity.ProviderUserId })
            .HasName("uq_user_identities_provider_provider_user_id");
        builder.HasAlternateKey(identity => new { identity.UserId, identity.Provider })
            .HasName("uq_user_identities_user_id_provider");

        builder.Property(identity => identity.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(identity => identity.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
        builder.Property(identity => identity.Provider).HasColumnName("provider").HasColumnType("identity_provider").IsRequired();
        builder.Property(identity => identity.ProviderUserId).HasColumnName("provider_user_id").HasColumnType("character varying(255)").HasMaxLength(255).IsRequired();
        builder.Property(identity => identity.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();
        builder.Property(identity => identity.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").IsRequired();

        builder.HasOne(identity => identity.User)
            .WithMany(user => user.Identities)
            .HasForeignKey(identity => identity.UserId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_user_identities_users_user_id");
    }
}
