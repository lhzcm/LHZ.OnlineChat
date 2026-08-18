using LHZ.OnlineChat.Server.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>
/// 文件上传控制器（图片消息等）
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadsController : ControllerBase
{
    private static readonly string[] AllowedImageExts = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private const long MaxImageSize = 5 * 1024 * 1024; // 5MB

    private readonly IWebHostEnvironment _env;

    public UploadsController(IWebHostEnvironment env)
    {
        _env = env;
    }

    /// <summary>
    /// 上传聊天图片，返回可访问的 URL
    /// </summary>
    [HttpPost("image")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("请选择图片文件"));
        if (file.Length > MaxImageSize)
            return BadRequest(ApiResponse.Fail("图片大小不能超过 5MB"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExts.Contains(ext))
            return BadRequest(ApiResponse.Fail("仅支持 jpg / png / gif / webp 格式图片"));

        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads", "images");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var savePath = Path.Combine(uploadsDir, fileName);
        await using (var stream = System.IO.File.Create(savePath))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(ApiResponse<UploadResponse>.Ok(new UploadResponse
        {
            Url = $"/uploads/images/{fileName}"
        }, "上传成功"));
    }
}
