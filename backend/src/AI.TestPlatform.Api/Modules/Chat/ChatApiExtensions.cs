using System.Text;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;
using FluentValidation;

namespace AI.TestPlatform.Api.Modules.Chat;

public static class ChatApiExtensions
{
    public static RouteGroupBuilder MapChatApi(this RouteGroupBuilder group)
    {
        // 流式对话：把 AI Worker 的 SSE 原样转发给浏览器（逐字返回）
        group.MapPost("/stream", async (
            ChatStreamRequestDto request,
            IValidator<ChatStreamRequestDto> validator,
            AIClient aiClient,
            HttpContext http,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            try
            {
                using var upstream = await aiClient.StreamChatAsync(request, ct);
                await using var stream = await upstream.Content.ReadAsStreamAsync(ct);
                await stream.CopyToAsync(http.Response.Body, ct);
                await http.Response.Body.FlushAsync(ct);
            }
            catch (AIWorkerException ex)
            {
                // 流已经开始，无法再改状态码：以 SSE error 帧告知前端
                var frame = $"data: {{\"type\":\"error\",\"message\":\"{ex.Message.Replace("\"", "'")}\"}}\n\n";
                await http.Response.WriteAsync(frame, Encoding.UTF8, ct);
            }
            return Results.Empty;
        }).WithPermission(Permission.ManageTestCases);

        return group;
    }
}
