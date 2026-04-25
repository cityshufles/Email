using Microsoft.AspNetCore.SignalR;

namespace WhatsAppBusinessAPI.Hubs
{
    public class CheckfrontHub : Hub
    {
        public async Task SendWebhookNotification(string message)
        {
            await Clients.All.SendAsync("ReceiveWebhook", message);
        }
    }
} 