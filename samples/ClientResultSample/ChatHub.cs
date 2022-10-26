﻿using Microsoft.AspNetCore.SignalR;

namespace ClientResultSample
{
    public class ChatHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            return Clients.All.SendAsync("Connect", $"Connection '{Context.ConnectionId}' is connected.");
        }

        public async Task<string> GetMessage(string ID)
        {
            try
            {
                var res = await Clients.Client(ID).InvokeAsync<string>("GetMessage", default);
                return $"From {ID}: {res}";
            }
            catch(Exception e)
            {
                return $"From {ID}: {e.Message}";
            }
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            return Clients.All.SendAsync("Connect", $"Connection '{Context.ConnectionId}' is disconnected.");
        }
    }
}
