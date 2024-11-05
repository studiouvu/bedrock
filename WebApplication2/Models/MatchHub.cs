using Microsoft.AspNetCore.SignalR;
namespace WebApplication2.Models;

public class MatchHub : Hub
{
    public MatchHub()
    {
        Console.WriteLine($"Create MatchHub");
    }

    ~MatchHub()
    {
        Console.WriteLine($"Dispose MatchHub");
    }
    
    public override async Task OnConnectedAsync()
    {
        // 클라이언트가 연결되었을 때 실행할 코드
        var connectionId = Context.ConnectionId;
        // 예: 연결된 클라이언트의 정보를 저장하거나 로그를 남길 수 있습니다.

        Console.WriteLine($"OnConnectedAsync {connectionId}");
        
        await base.OnConnectedAsync();
    }

    // 클라이언트에서 호출하여 그룹에 참여하거나 다른 작업을 수행할 수 있습니다.
    public async Task JoinQueue(string playerId)
    {
        Console.WriteLine($"JoinQueue {playerId}");
        // 플레이어를 매칭 대기열에 추가하는 로직
    }
}
