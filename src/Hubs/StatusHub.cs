using Microsoft.AspNetCore.SignalR;
using splunk_eventhubs.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace splunk_eventhubs.Hubs
{
    public class StatusHub : Hub
    {

        public StatusHub()
        {

        }

        public async Task SendUpdate(ConcurrentDictionary<string, ConsumersRepository.EhNamespaceData> e)
        {
            await Clients.All.SendAsync("updateData", e);
        }

        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}
