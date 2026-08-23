namespace HubServer.Hubs;

using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    #region Methods

    public async Task SendMessage(string sender, string framework, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", sender, framework, message);
    }

    #endregion
}

