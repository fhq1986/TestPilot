using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AI.TestPlatform.Api.Hubs;

/// <summary>
/// 站内消息推送 Hub。
///
/// **为什么不并进 <see cref="ExecutionHub"/>**：那个 Hub 的分组语义是「谁正在看这个执行」
/// （组名 = 执行 ID），这里是「这条消息归谁」（组名 = 用户 ID）。两种语义混在一个 Hub 里，
/// 分组名就没有统一含义了，将来加事件时很容易把消息推错人。
///
/// 组名取自 token 里的当前用户，**不接受客户端传入用户 ID**——否则任何登录用户
/// 都能加入别人的组，等于把所有人的消息都收走。
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    /// <summary>组名前缀：避免与执行组的裸 GUID 组名撞车（两者共用同一个 SignalR 实例）</summary>
    private const string UserGroupPrefix = "user:";

    public static string UserGroup(Guid userId) => $"{UserGroupPrefix}{userId:N}";

    /// <summary>加入自己的消息组。前端连接成功后调用一次</summary>
    public async Task JoinUserGroup()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(raw, out var userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
    }
}
