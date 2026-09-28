using Npgsql;

namespace CustomFormService.Api.Infrastructure.Persistence;

public sealed class PostgreSqlOptions
{
    public const string SectionName = "PostgreSql";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Database { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string BuildConnectionString()
    {
        return new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            IncludeErrorDetail = false
        }.ConnectionString;
    }
}
