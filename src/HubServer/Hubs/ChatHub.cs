namespace HubServer.Hubs;

using HubServer.Services;
using Microsoft.AspNetCore.SignalR;

public class ChatHub(HistoryServiceImpl historyService) : Hub
{
    #region Methods

    public async Task SendMessage(string sender, string framework, string message)
    {
        historyService.AddMessage(sender, framework, message);
        await Clients.All.SendAsync("ReceiveMessage", sender, framework, message);
    }

    #endregion
}

