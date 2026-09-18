using Microsoft.AspNetCore.SignalR;

namespace CityPulseAI.Services.Hubs;

public class CityPulseHub : Hub
{
    public async Task SendBroadcast(string method, object data)
    {
        await Clients.All.SendAsync(method, data);
    }
}
