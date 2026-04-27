using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace backend.Controllers;

public sealed record ApiStatusResponse(
    string Message,
    string Environment,
    string Framework,
    DateTimeOffset ServerTimeUtc);

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    /// <summary>
    /// Returns a lightweight health snapshot for the Mission Control API.
    /// </summary>
    [HttpGet]
    [SwaggerOperation(Summary = "Returns a lightweight health snapshot for the Mission Control API.")]
    public ActionResult<ApiStatusResponse> Get()
    {
        return Ok(new ApiStatusResponse(
            Message: "ASP.NET Core API is running.",
            Environment: HttpContext.RequestServices
                .GetRequiredService<IWebHostEnvironment>()
                .EnvironmentName,
            Framework: $".NET {Environment.Version}",
            ServerTimeUtc: DateTimeOffset.UtcNow));
    }
}
