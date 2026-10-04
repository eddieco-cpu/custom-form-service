using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace CustomFormService.IntegrationTests;

public sealed partial class FormEndpointsTests
{
    [TestMethod]
    [DataRow("missing-content-type")]
    [DataRow("wrong-content-type")]
    [DataRow("empty-body")]
    [DataRow("malformed-json")]
    public async Task R7_review_visibility_transport_errors_use_contract_envelope_without_writes(string scenario)
    {
        var owner = await CreateMemberAsync("R7 Transport Review Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        var before = await ReadDefinitionRowsAsync(id);
        using var content = scenario switch
        {
            "empty-body" => R7Json(""),
            "malformed-json" => R7Json("{"),
            _ => new StringContent("{\"resultVisibility\":\"public\"}", Encoding.UTF8, "text/plain")
        };
        if (scenario == "missing-content-type") content.Headers.ContentType = null;
        using var response = await SendWithCookieAsync(HttpMethod.Patch,
            $"/api/v1/forms/{id}/result-visibility", owner.Cookie, content);
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertR7DetailAsync(response, "body", "INVALID_VALUE");
        using var body = await ReadJsonAsync(response);
        Assert.AreEqual(JsonValueKind.String, body.RootElement.GetProperty("error").GetProperty("message").ValueKind);
        Assert.IsFalse(body.RootElement.TryGetProperty("type", out _), "ProblemDetails must not replace the error envelope.");
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
    }

    [TestMethod]
    [DataRow("1")]
    [DataRow("false")]
    public async Task R7_review_visibility_non_string_values_are_rejected(string value)
    {
        var owner = await CreateMemberAsync("R7 Type Review Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        var before = await ReadDefinitionRowsAsync(id);
        using var response = await SendWithCookieAsync(HttpMethod.Patch,
            $"/api/v1/forms/{id}/result-visibility", owner.Cookie, R7Json($"{{\"resultVisibility\":{value}}}"));
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertR7DetailAsync(response, "resultVisibility", "INVALID_VALUE");
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
    }

    [TestMethod]
    [DataRow("publish")]
    [DataRow("close")]
    [DataRow("result-visibility")]
    public async Task R7_review_owner_cannot_access_deleted_form(string action)
    {
        var owner = await CreateMemberAsync("R7 Deleted Review Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        Assert.AreEqual(HttpStatusCode.NoContent, (await SendWithCookieAsync(HttpMethod.Delete,
            $"/api/v1/forms/{id}", owner.Cookie)).StatusCode);
        var before = await ReadDefinitionRowsAsync(id);
        await AssertErrorAsync(await R7ActionAsync(action, id, owner.Cookie), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
        Assert.AreEqual(before, await ReadDefinitionRowsAsync(id));
    }

    [TestMethod]
    [DataRow("publish")]
    [DataRow("close")]
    [DataRow("result-visibility")]
    public async Task R7_review_delete_waiting_on_r7_write_preserves_transition_and_children(string action)
    {
        var gate = new R7SaveGate();
        await UseCandidateInterceptorsAsync(gate);
        var owner = await CreateMemberAsync("R7 Delete Race Review Owner");
        var id = await CreateR7FormAsync(owner.Cookie);
        if (action == "close")
            await ExecuteAsync("UPDATE forms SET status = 'published' WHERE id = @id;", new NpgsqlParameter("id", id));
        var before = await ReadDefinitionRowsAsync(id);
        gate.FormId = id;
        var writer = R7ActionAsync(action, id, owner.Cookie);
        Task<HttpResponseMessage>? deletion = null;
        try
        {
            var blockerPid = await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            deletion = SendWithCookieAsync(HttpMethod.Delete, $"/api/v1/forms/{id}", owner.Cookie);
            await WaitForR7BlockedAsync(blockerPid);
            Assert.IsFalse(deletion.IsCompleted);
        }
        finally
        {
            gate.Release.TrySetResult();
            gate.FormId = null;
        }
        Assert.AreEqual(HttpStatusCode.OK, (await writer).StatusCode);
        Assert.IsNotNull(deletion);
        Assert.AreEqual(HttpStatusCode.NoContent, (await deletion).StatusCode);
        AssertOnlyFormColumnsChanged(before, await ReadDefinitionRowsAsync(id),
            action == "result-visibility" ? ["result_visibility", "updated_at", "deleted_at"] : ["status", "updated_at", "deleted_at"]);
        await AssertErrorAsync(await R7ActionAsync(action, id, owner.Cookie), HttpStatusCode.NotFound, "FORM_NOT_FOUND");
    }
}
