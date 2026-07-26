using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rustex.Api.Dtos;
using Rustex.Domain.Templating;

namespace Rustex.Api.Controllers;

[ApiController]
[Route("api/chat-templates")]
[Authorize]
public class ChatTemplateMetadataController : ControllerBase
{
    [HttpGet("metadata")]
    public ActionResult<ChatTemplateMetadataResponse> Metadata() =>
        new ChatTemplateMetadataResponse(ChatEventTypes.All, TemplateRenderer.SupportedPlaceholders);

    [HttpPost("preview")]
    public ActionResult<PreviewTemplateResponse> Preview([FromBody] PreviewTemplateRequest request)
    {
        var eventType = string.IsNullOrWhiteSpace(request.EventType) ? "RaidDetected" : request.EventType;
        var rendered = TemplateRenderer.Render(request.TemplateText, TemplateRenderer.SampleValues(eventType));
        return new PreviewTemplateResponse(rendered);
    }
}
