using NpgsqlTypes;

namespace CustomFormService.Api.Domain.Enums;

public enum UserStatus
{
    [PgName("active")]
    Active,

    [PgName("disabled")]
    Disabled
}

public enum IdentityProvider
{
    [PgName("google")]
    Google
}

public enum FormStatus
{
    [PgName("draft")]
    Draft,

    [PgName("published")]
    Published,

    [PgName("closed")]
    Closed
}

public enum ResultVisibility
{
    [PgName("owner_only")]
    OwnerOnly,

    [PgName("respondents")]
    Respondents,

    [PgName("public")]
    Public
}

public enum ResponseMode
{
    [PgName("single")]
    Single,

    [PgName("multiple")]
    Multiple
}

public enum ResponseIdentity
{
    [PgName("anonymous")]
    Anonymous,

    [PgName("identified")]
    Identified
}

public enum QuestionType
{
    [PgName("text")]
    Text,

    [PgName("number")]
    Number,

    [PgName("single_choice")]
    SingleChoice,

    [PgName("multiple_choice")]
    MultipleChoice,

    [PgName("rating")]
    Rating
}
