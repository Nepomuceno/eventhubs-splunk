using Microsoft.Azure.Management.EventHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using splunk_eventhubs.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace splunk_eventhubs.Processor
{
    public class LogProcessorService : IHostedService
    {
        private readonly ConsumersRepository _consumersRepository;
        private readonly IConfiguration _configuration;

        public LogProcessorService(ConsumersRepository consumersRepository, IConfiguration configuration)
        {
            _consumersRepository = consumersRepository;
            _configuration = configuration;
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            EventHubManagementClient ehClient = await GetEhClient();
            ehClient.Namespaces.Get("resource-group", "namespace-name");
            throw new NotImplementedException();
        }

        private async Task<EventHubManagementClient> GetEhClient()
        {
            var tenantId = _configuration.GetValue<string>("Azure:Tenant");
            var context = new AuthenticationContext($"https://login.microsoftonline.com/{tenantId}");
            var clientId = _configuration.GetValue<string>("Azure:ClientId");
            var clientSecret = _configuration.GetValue<string>("Azure:ClientSecret");
            var result = await context.AcquireTokenAsync(
                "https://management.core.windows.net/",
                new ClientCredential(clientId, clientSecret)
            );

            var creds = new TokenCredentials(result.AccessToken);
            var ehClient = new EventHubManagementClient(creds)
            {
                SubscriptionId = _configuration.GetValue<string>("Azure:Subscription")
            };
            return ehClient;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
    
}
