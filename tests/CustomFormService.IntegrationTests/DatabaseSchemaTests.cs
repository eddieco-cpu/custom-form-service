using Npgsql;

namespace CustomFormService.IntegrationTests;

[TestClass]
public sealed class DatabaseSchemaTests
{
    [TestMethod]
    public async Task Tables_and_columns_match_v1_contract()
    {
        var tables = await QueryAsync(
            """
            SELECT tablename
            FROM pg_tables
            WHERE schemaname = 'public'
            ORDER BY tablename;
            """);

        CollectionAssert.AreEqual(
            new[]
            {
                "__EFMigrationsHistory",
                "answers",
                "forms",
                "options",
                "questions",
                "responses",
                "user_identities",
                "users"
            },
            tables);

        var columns = await QueryAsync(
            """
            SELECT concat_ws('|',
                table_name,
                ordinal_position,
                column_name,
                CASE WHEN data_type = 'USER-DEFINED' THEN udt_name ELSE data_type END,
                COALESCE(character_maximum_length::text, ''),
                is_nullable,
                COALESCE(column_default, ''))
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name IN ('users', 'user_identities', 'forms', 'questions', 'options', 'responses', 'answers')
            ORDER BY table_name, ordinal_position;
            """);

        CollectionAssert.AreEqual(ExpectedColumns, columns);
    }

    [TestMethod]
    public async Task PostgreSql_enums_match_v1_contract()
    {
        var enums = await QueryAsync(
            """
            SELECT t.typname || '|' || string_agg(e.enumlabel, ',' ORDER BY e.enumlabel)
            FROM pg_type t
            JOIN pg_enum e ON e.enumtypid = t.oid
            JOIN pg_namespace n ON n.oid = t.typnamespace
            WHERE n.nspname = 'public'
            GROUP BY t.typname
            ORDER BY t.typname;
            """);

        CollectionAssert.AreEqual(
            new[]
            {
                "form_status|closed,draft,published",
                "identity_provider|google",
                "question_type|multiple_choice,number,rating,single_choice,text",
                "response_identity|anonymous,identified",
                "response_mode|multiple,single",
                "result_visibility|owner_only,public,respondents",
                "user_status|active,disabled"
            },
            enums);
    }

    [TestMethod]
    public async Task Keys_checks_indexes_and_delete_behaviors_match_v1_contract()
    {
        var primaryKeys = await QueryAsync(KeyQuery('p'));
        CollectionAssert.AreEqual(
            new[]
            {
                "answers|pk_answers|id",
                "forms|pk_forms|id",
                "options|pk_options|id",
                "questions|pk_questions|id",
                "responses|pk_responses|id",
                "user_identities|pk_user_identities|id",
                "users|pk_users|id"
            },
            primaryKeys);

        var uniqueConstraints = await QueryAsync(KeyQuery('u'));
        CollectionAssert.AreEqual(
            new[]
            {
                "answers|uq_answers_response_id_question_id|response_id,question_id",
                "options|uq_options_question_id_value|question_id,value",
                "user_identities|uq_user_identities_provider_provider_user_id|provider,provider_user_id",
                "user_identities|uq_user_identities_user_id_provider|user_id,provider",
                "users|uq_users_email|email"
            },
            uniqueConstraints);

        var checks = await QueryAsync(
            """
            SELECT child.relname || '|' || c.conname || '|' || pg_get_constraintdef(c.oid)
            FROM pg_constraint c
            JOIN pg_class child ON child.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = child.relnamespace
            WHERE n.nspname = 'public' AND c.contype = 'c'
            ORDER BY child.relname, c.conname;
            """);
        CollectionAssert.AreEqual(
            new[]
            {
                "options|ck_options_sort_order_nonnegative|CHECK ((sort_order >= 0))",
                "questions|ck_questions_config_object|CHECK ((jsonb_typeof(config) = 'object'::text))",
                "questions|ck_questions_sort_order_nonnegative|CHECK ((sort_order >= 0))"
            },
            checks);

        var foreignKeys = await QueryAsync(
            """
            SELECT child.relname || '|' || c.conname || '|' || child_columns.names || '|' ||
                   parent.relname || '|' || parent_columns.names || '|' ||
                   CASE c.confdeltype
                       WHEN 'a' THEN 'NO ACTION'
                       WHEN 'r' THEN 'RESTRICT'
                       WHEN 'c' THEN 'CASCADE'
                       WHEN 'n' THEN 'SET NULL'
                       WHEN 'd' THEN 'SET DEFAULT'
                   END
            FROM pg_constraint c
            JOIN pg_class child ON child.oid = c.conrelid
            JOIN pg_class parent ON parent.oid = c.confrelid
            JOIN pg_namespace n ON n.oid = child.relnamespace
            JOIN LATERAL (
                SELECT string_agg(attribute.attname, ',' ORDER BY key_column.ordinality) AS names
                FROM unnest(c.conkey) WITH ORDINALITY AS key_column(attnum, ordinality)
                JOIN pg_attribute attribute
                  ON attribute.attrelid = c.conrelid AND attribute.attnum = key_column.attnum
            ) child_columns ON true
            JOIN LATERAL (
                SELECT string_agg(attribute.attname, ',' ORDER BY key_column.ordinality) AS names
                FROM unnest(c.confkey) WITH ORDINALITY AS key_column(attnum, ordinality)
                JOIN pg_attribute attribute
                  ON attribute.attrelid = c.confrelid AND attribute.attnum = key_column.attnum
            ) parent_columns ON true
            WHERE n.nspname = 'public' AND c.contype = 'f'
            ORDER BY child.relname, c.conname;
            """);
        CollectionAssert.AreEqual(
            new[]
            {
                "answers|fk_answers_questions_question_id|question_id|questions|id|NO ACTION",
                "answers|fk_answers_responses_response_id|response_id|responses|id|NO ACTION",
                "forms|fk_forms_users_user_id|user_id|users|id|NO ACTION",
                "options|fk_options_questions_question_id|question_id|questions|id|CASCADE",
                "questions|fk_questions_forms_form_id|form_id|forms|id|NO ACTION",
                "responses|fk_responses_forms_form_id|form_id|forms|id|NO ACTION",
                "responses|fk_responses_users_user_id|user_id|users|id|NO ACTION",
                "user_identities|fk_user_identities_users_user_id|user_id|users|id|NO ACTION"
            },
            foreignKeys);

        var indexes = await QueryAsync(
            """
            SELECT table_entry.relname || '|' || index_entry.relname || '|' ||
                   string_agg(pg_get_indexdef(index_data.indexrelid, key_position, true), ',' ORDER BY key_position)
            FROM pg_index index_data
            JOIN pg_class table_entry ON table_entry.oid = index_data.indrelid
            JOIN pg_class index_entry ON index_entry.oid = index_data.indexrelid
            JOIN pg_namespace n ON n.oid = table_entry.relnamespace
            LEFT JOIN pg_constraint constraint_entry ON constraint_entry.conindid = index_data.indexrelid
            CROSS JOIN LATERAL generate_series(1, index_data.indnkeyatts) AS key_position
            WHERE n.nspname = 'public'
              AND table_entry.relname IN ('users', 'user_identities', 'forms', 'questions', 'options', 'responses', 'answers')
              AND constraint_entry.oid IS NULL
              AND NOT index_data.indisunique
            GROUP BY table_entry.relname, index_entry.relname, index_data.indexrelid
            ORDER BY table_entry.relname, index_entry.relname;
            """);
        CollectionAssert.AreEqual(
            new[]
            {
                "answers|ix_answers_question_id|question_id",
                "options|ix_options_question_id_sort_order|question_id,sort_order",
                "questions|ix_questions_form_id_sort_order|form_id,sort_order",
                "responses|ix_responses_form_id_submitted_at|form_id,submitted_at",
                "responses|ix_responses_user_id_form_id_submitted_at|user_id,form_id,submitted_at"
            },
            indexes);
    }

    private static string KeyQuery(char constraintType)
    {
        return $$"""
            SELECT child.relname || '|' || c.conname || '|' ||
                   string_agg(attribute.attname, ',' ORDER BY key_column.ordinality)
            FROM pg_constraint c
            JOIN pg_class child ON child.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = child.relnamespace
            JOIN unnest(c.conkey) WITH ORDINALITY AS key_column(attnum, ordinality) ON true
            JOIN pg_attribute attribute
              ON attribute.attrelid = c.conrelid AND attribute.attnum = key_column.attnum
            WHERE n.nspname = 'public'
              AND child.relname IN ('users', 'user_identities', 'forms', 'questions', 'options', 'responses', 'answers')
              AND c.contype = '{{constraintType}}'
            GROUP BY child.relname, c.conname
            ORDER BY child.relname, c.conname;
            """;
    }

    private static async Task<string[]> QueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var results = new List<string>();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return [.. results];
    }

    private static string BuildConnectionString()
    {
        return new NpgsqlConnectionStringBuilder
        {
            Host = RequiredEnvironmentVariable("PostgreSql__Host"),
            Port = int.Parse(RequiredEnvironmentVariable("PostgreSql__Port"), System.Globalization.CultureInfo.InvariantCulture),
            Database = RequiredEnvironmentVariable("PostgreSql__Database"),
            Username = RequiredEnvironmentVariable("PostgreSql__Username"),
            Password = RequiredEnvironmentVariable("PostgreSql__Password"),
            IncludeErrorDetail = false
        }.ConnectionString;
    }

    private static string RequiredEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Environment variable {name} must be configured for schema integration tests.");
    }

    private static readonly string[] ExpectedColumns =
    [
        "answers|1|id|uuid||NO|",
        "answers|2|response_id|uuid||NO|",
        "answers|3|question_id|uuid||NO|",
        "answers|4|value|jsonb||NO|",
        "forms|1|id|uuid||NO|",
        "forms|2|user_id|uuid||NO|",
        "forms|3|title|character varying|200|NO|",
        "forms|4|description|text||YES|",
        "forms|5|status|form_status||NO|'draft'::form_status",
        "forms|6|result_visibility|result_visibility||NO|'owner_only'::result_visibility",
        "forms|7|response_mode|response_mode||NO|'single'::response_mode",
        "forms|8|response_identity|response_identity||NO|'anonymous'::response_identity",
        "forms|9|submission_deadline_at|timestamp with time zone||YES|",
        "forms|10|created_at|timestamp with time zone||NO|now()",
        "forms|11|updated_at|timestamp with time zone||NO|now()",
        "forms|12|deleted_at|timestamp with time zone||YES|",
        "options|1|id|uuid||NO|",
        "options|2|question_id|uuid||NO|",
        "options|3|label|text||NO|",
        "options|4|value|character varying|255|NO|",
        "options|5|sort_order|integer||NO|",
        "options|6|created_at|timestamp with time zone||NO|now()",
        "options|7|updated_at|timestamp with time zone||NO|now()",
        "questions|1|id|uuid||NO|",
        "questions|2|form_id|uuid||NO|",
        "questions|3|type|question_type||NO|",
        "questions|4|title|text||NO|",
        "questions|5|description|text||YES|",
        "questions|6|required|boolean||NO|false",
        "questions|7|sort_order|integer||NO|",
        "questions|8|config|jsonb||NO|'{}'::jsonb",
        "questions|9|created_at|timestamp with time zone||NO|now()",
        "questions|10|updated_at|timestamp with time zone||NO|now()",
        "responses|1|id|uuid||NO|",
        "responses|2|form_id|uuid||NO|",
        "responses|3|user_id|uuid||NO|",
        "responses|4|submitted_at|timestamp with time zone||NO|now()",
        "user_identities|1|id|uuid||NO|",
        "user_identities|2|user_id|uuid||NO|",
        "user_identities|3|provider|identity_provider||NO|",
        "user_identities|4|provider_user_id|character varying|255|NO|",
        "user_identities|5|created_at|timestamp with time zone||NO|now()",
        "user_identities|6|updated_at|timestamp with time zone||NO|now()",
        "users|1|id|uuid||NO|",
        "users|2|email|character varying|254|NO|",
        "users|3|display_name|character varying|100|NO|",
        "users|4|avatar_url|text||YES|",
        "users|5|status|user_status||NO|'active'::user_status",
        "users|6|password_hash|character varying|255|YES|",
        "users|7|created_at|timestamp with time zone||NO|now()",
        "users|8|updated_at|timestamp with time zone||NO|now()"
    ];
}
