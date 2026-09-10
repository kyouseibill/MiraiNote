using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services.ChatModels;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;
using Xunit;

namespace MiraiNote.Tests;

public class AiModelsControllerTests
{
    [Fact]
    public void GetModels_returns_only_available_public_model_metadata()
    {
        var registry = new ChatModelRegistry(Options.Create(new AiOptions
        {
            DefaultModelKey = "minimax:MiniMax-M2.7",
            Providers =
            [
                new AiProviderOptions
                {
                    Key = "minimax",
                    DisplayName = "MiniMax",
                    ApiKey = "server-only-key",
                    BaseUrl = "https://api.minimax.io/v1",
                    Models =
                    [
                        new AiModelOptions { Key = "minimax:MiniMax-M2.7", ModelId = "MiniMax-M2.7", DisplayName = "MiniMax M2.7" },
                    ],
                },
            ],
        }));
        var controller = new AiModelsController(registry);

        var result = controller.GetModels();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<ApiResponse<List<AiModelDto>>>(ok.Value);
        var model = Assert.Single(payload.Data!);
        Assert.Equal("minimax:MiniMax-M2.7", model.Key);
        Assert.Equal("MiniMax", model.ProviderDisplayName);
        Assert.True(model.SupportsWork);
    }
}
