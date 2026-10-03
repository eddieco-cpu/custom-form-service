using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    [DataRow("2026-12-31T00:00:00Z")]
    [DataRow("2026-12-31T00:00:00.123456Z")]
    [DataRow("2026-12-31T00:00:00.1234567Z")]
    [DataRow("2026-12-31T00:00:00.12345678Z")]
    [DataRow("1999-12-31T00:00:00.1234567Z")]
    [DataRow("1999-12-31T23:59:59.9999999Z")]
    [DataRow("2000-01-01T00:00:00.0000001Z")]
    [DataRow("2026-12-31 00:00:00.1234567Z")]
    [DataRow("0001-01-01T00:00:00Z")]
    [DataRow("0001-01-01T00:00:00.0000001Z")]
    [DataRow("9999-12-31T23:59:59.9999999Z")]
    [DataRow("9999-12-31T23:59:59.9999998Z")]
    public async Task Patch_deadline_uses_actual_storage_precision_for_no_op(string input)
    {
        var capture = new SqlCaptureInterceptor();
        await UseCandidateInterceptorsAsync(capture);
        var owner = await CreateMemberAsync("R6 Deadline Precision Owner");
        var formId = await CreateMixedOperationFormAsync(owner.Cookie);
        using var originalRows = JsonDocument.Parse(await ReadDefinitionRowsAsync(formId));
        var parsed = DateTimeOffset.Parse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        // Independent oracle: round-trip through the installed driver and real DB,
        // not a copy of the application's storage-precision comparison.
        var stored = await ScalarAsync<DateTime>("SELECT @deadline::timestamptz;",
            new NpgsqlParameter("deadline", NpgsqlDbType.TimestampTz) { Value = parsed });

        capture.Clear();
        var first = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { submissionDeadlineAt = input }));
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        Assert.IsTrue(capture.Commands.Any(command => command.Contains("UPDATE forms", StringComparison.OrdinalIgnoreCase)));
        using var firstBody = await ReadJsonAsync(first);
        using var firstGet = await GetFormJsonAsync(formId, owner.Cookie);
        Assert.AreEqual(firstBody.RootElement.GetRawText(), firstGet.RootElement.GetRawText());
        Assert.AreEqual(stored.Ticks, firstBody.RootElement.GetProperty("submissionDeadlineAt").GetDateTimeOffset().UtcTicks);
        var savedRowsText = await ReadDefinitionRowsAsync(formId);
        using var savedRows = JsonDocument.Parse(savedRowsText);
        Assert.AreEqual(originalRows.RootElement.GetProperty("form").GetProperty("created_at").GetString(),
            savedRows.RootElement.GetProperty("form").GetProperty("created_at").GetString());
        Assert.IsTrue(savedRows.RootElement.GetProperty("form").GetProperty("updated_at").GetDateTimeOffset() >
            originalRows.RootElement.GetProperty("form").GetProperty("updated_at").GetDateTimeOffset());
        foreach (var table in new[] { "questions", "options" })
            Assert.AreEqual(originalRows.RootElement.GetProperty(table).GetRawText(), savedRows.RootElement.GetProperty(table).GetRawText());

        var repeats = new List<string> { input, firstBody.RootElement.GetProperty("submissionDeadlineAt").GetDateTimeOffset().UtcDateTime.ToString("O") };
        if (stored.Ticks != DateTime.MinValue.Ticks && stored.Ticks != DateTime.MaxValue.Ticks)
        {
            // A different 100ns value that the real provider stores identically.
            var nearby = new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc))
                .AddTicks(stored < new DateTime(2000, 1, 1) ? -1 : 1);
            var roundTripped = await ScalarAsync<DateTime>("SELECT @deadline::timestamptz;",
                new NpgsqlParameter("deadline", NpgsqlDbType.TimestampTz) { Value = nearby });
            Assert.AreEqual(stored.Ticks, roundTripped.Ticks);
            repeats.Add(nearby.UtcDateTime.ToString("O"));
        }
        foreach (var repeat in repeats)
        {
            capture.Clear();
            var patch = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
                JsonContent.Create(new { submissionDeadlineAt = repeat }));
            Assert.AreEqual(HttpStatusCode.OK, patch.StatusCode, await patch.Content.ReadAsStringAsync());
            var updated = capture.Commands.Any(command => command.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(savedRowsText, await ReadDefinitionRowsAsync(formId), $"Same stored deadline ({repeat}) must retain every row, ID and timestamp; UPDATE emitted: {updated}.");
            Assert.IsFalse(updated, "Storage-equivalent deadline must not emit UPDATE.");
            using var body = await ReadJsonAsync(patch);
            using var get = await GetFormJsonAsync(formId, owner.Cookie);
            Assert.AreEqual(firstBody.RootElement.GetRawText(), body.RootElement.GetRawText());
            Assert.AreEqual(body.RootElement.GetRawText(), get.RootElement.GetRawText());
        }

        if (stored.Ticks != DateTime.MinValue.Ticks && stored.Ticks != DateTime.MaxValue.Ticks)
        {
            var different = new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc)).AddMicroseconds(-1);
            capture.Clear();
            var changed = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
                JsonContent.Create(new { submissionDeadlineAt = different.UtcDateTime.ToString("O") }));
            Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, await changed.Content.ReadAsStringAsync());
            Assert.IsTrue(capture.Commands.Any(command => command.Contains("UPDATE forms", StringComparison.OrdinalIgnoreCase)));
            using var changedBody = await ReadJsonAsync(changed);
            Assert.AreEqual(different.UtcTicks, changedBody.RootElement.GetProperty("submissionDeadlineAt").GetDateTimeOffset().UtcTicks);
            Assert.IsTrue(changedBody.RootElement.GetProperty("updatedAt").GetDateTimeOffset() > firstBody.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
        }

        var cleared = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { submissionDeadlineAt = (string?)null }));
        Assert.AreEqual(HttpStatusCode.OK, cleared.StatusCode);
        using var clearBody = await ReadJsonAsync(cleared);
        Assert.AreEqual(JsonValueKind.Null, clearBody.RootElement.GetProperty("submissionDeadlineAt").ValueKind);
        var clearedRows = await ReadDefinitionRowsAsync(formId);
        capture.Clear();
        var clearAgain = await SendWithCookieAsync(HttpMethod.Patch, $"/api/v1/forms/{formId}", owner.Cookie,
            JsonContent.Create(new { submissionDeadlineAt = (string?)null }));
        Assert.AreEqual(HttpStatusCode.OK, clearAgain.StatusCode);
        Assert.IsFalse(capture.Commands.Any(command => command.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(clearedRows, await ReadDefinitionRowsAsync(formId));
    }
}
