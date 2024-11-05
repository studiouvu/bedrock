using Bedrock.Models;
using Microsoft.AspNetCore.SignalR;
namespace Bedrock.Controllers;

public class MatchmakingService
{
    private readonly IHubContext<MatchHub> _hubContext;

    public MatchmakingService(IHubContext<MatchHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyPlayersOfMatch(string player1ConnectionId, string player2ConnectionId)
    {
        await _hubContext.Clients.Client(player1ConnectionId).SendAsync("MatchFound", "OpponentPlayerId");
        await _hubContext.Clients.Client(player2ConnectionId).SendAsync("MatchFound", "OpponentPlayerId");
    }
}
