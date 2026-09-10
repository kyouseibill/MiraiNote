using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services.ChatModels;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/ai/models")]
public sealed class AiModelsController : ControllerBase
{
    private readonly IChatModelRegistry _models;

    public AiModelsController(IChatModelRegistry models) => _models = models;

    [HttpGet]
    public ActionResult<ApiResponse<List<AiModelDto>>> GetModels()
    {
        var catalog = _models.GetPublicCatalog()
            .Select(model => new AiModelDto
            {
                Key = model.Key,
                Provider = model.Provider,
                ProviderDisplayName = model.ProviderDisplayName,
                ModelId = model.ModelId,
                DisplayName = model.DisplayName,
                SupportsChat = model.SupportsChat,
                SupportsWork = model.SupportsWork,
                SupportsTools = model.SupportsTools,
            })
            .ToList();
        return Ok(ApiResponse<List<AiModelDto>>.Ok(catalog));
    }
}
