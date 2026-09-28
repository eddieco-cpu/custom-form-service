using CustomFormService.Api.Domain.Enums;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace CustomFormService.Api.Infrastructure.Persistence;

public static class PostgreSqlEnumMappings
{
    public static void Configure(NpgsqlDbContextOptionsBuilder options)
    {
        options
            .MapEnum<UserStatus>("user_status")
            .MapEnum<IdentityProvider>("identity_provider")
            .MapEnum<FormStatus>("form_status")
            .MapEnum<ResultVisibility>("result_visibility")
            .MapEnum<ResponseMode>("response_mode")
            .MapEnum<ResponseIdentity>("response_identity")
            .MapEnum<QuestionType>("question_type");
    }
}
