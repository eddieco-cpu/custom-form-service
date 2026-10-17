using Microsoft.AspNetCore.Mvc;

namespace CustomFormService.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse("healthy"));
    }
}

public sealed record HealthResponse(string Status, string Message = "Custom Form Service API is running.");
