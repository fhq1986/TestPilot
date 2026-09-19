using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AI.TestPlatform.Api.Hubs;

[Authorize]
public class ExecutionHub : Hub
{
    public async Task JoinExecutionGroup(string executionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, executionId);
    }
}
