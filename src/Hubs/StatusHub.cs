using Microsoft.AspNetCore.SignalR;
using splunk_eventhubs.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace splunk_eventhubs.Hubs
{
    public class StatusHub : Hub
    {
        private readonly ConsumersRepository _consumersRepository;

        public StatusHub(ConsumersRepository consumersRepository)
        {
            _consumersRepository = consumersRepository;
        }
    }
}
