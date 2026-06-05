using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiraiNote.API.Services;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/upload")]
public class UploadController : ControllerBase
{
    private readonly UploadOptions _options;
    private readonly IWebHostEnvironment _env;
    private readonly ICurrentUserService _currentUser;

    public UploadController(IOptions<UploadOptions> options, IWebHostEnvironment env, ICurrentUserService currentUser)
    {
        _options = options.Value;
        _env = env;
        _currentUser = currentUser;
    }

    /// <summary>上传图片，返回可访问的相对路径（用于 LifeLog.ImagePath）。</summary>
    [HttpPost("image")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<string>>> UploadImage(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("请选择文件"));

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse.Fail("图片大小不能超过 5MB"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        if (!allowed.Contains(ext))
            return BadRequest(ApiResponse.Fail("只支持 jpg/png/gif/webp 格式"));

        var userId = _currentUser.UserId;

        // 物理存储目录：优先使用 PhysicalPath（生产），否则回退到 wwwroot/{BasePath}
        // 目录结构：{storageRoot}/{userId}/images/
        var storageRoot = !string.IsNullOrEmpty(_options.PhysicalPath)
            ? _options.PhysicalPath
            : Path.Combine(_env.WebRootPath ?? "wwwroot", _options.BasePath);
        var dir = Path.Combine(storageRoot, userId.ToString(), "images");
        Directory.CreateDirectory(dir);

        var savedName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(dir, savedName);

        await using var stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream, ct);

        // 返回相对 URL 路径：/{BasePath}/{userId}/images/{fileName}
        var relativePath = $"/{_options.BasePath}/{userId}/images/{savedName}";
        return Ok(ApiResponse<string>.Ok(relativePath, "上传成功"));
    }
}
