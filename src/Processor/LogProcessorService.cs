using Microsoft.Azure.EventHubs;
using Microsoft.Azure.EventHubs.Processor;
using Microsoft.Azure.Management.EventHub;
using Microsoft.Azure.Management.EventHub.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using Newtonsoft.Json;
using splunk_eventhubs.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace splunk_eventhubs.Processor
{
    public class LogProcessorService : IHostedService
    {
        private readonly ConsumersRepository _consumersRepository;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly List<EventProcessorHost> _eventProcessorHosts;

        public LogProcessorService(ConsumersRepository consumersRepository, IConfiguration configuration)
        {
            _consumersRepository = consumersRepository;
            _configuration = configuration;
            var httpMessageHandler = new HttpClientHandler();
            httpMessageHandler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) =>
            {
                if (cert.Thumbprint == _configuration["Consumer:splunk-cert"])
                    return true;
                return true;
            };
            _httpClient = new HttpClient(httpMessageHandler);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Splunk {_configuration["Consumer:splunk-token"]}");
            _httpClient.BaseAddress = new Uri(_configuration["Consumer:splunk-url"]);
            _eventProcessorHosts = new List<EventProcessorHost>();
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var consumerGroupName = _configuration["Consumer:consumer-group"];
            var authorizationRuleName = _configuration["Consumer:authorization-rule"];
            var prefix = _configuration["Consumer:prefix"];
            List<Task> processors = new List<Task>();
            var ehDinifitions = await _consumersRepository.GetMonitoringEh();
            foreach (var ehDefinition in ehDinifitions)
            {
                EventHubManagementClient ehClient = await GetEhClient(ehDefinition.Subscription);
                var ehs = await GetConnectionStrings(consumerGroupName, authorizationRuleName, prefix, ehClient, ehDefinition);
                var storage = GetStorageConnection();
                foreach (var eh in ehs)
                {
                    EventProcessorHost processorHost =
                        new EventProcessorHost(
                            "splunk-host-processor",
                            eh.ehName,
                            eh.consumerGroup,
                            eh.connectionString,
                            storage.storageConnectionString,
                            storage.container,
                            $"{ehDefinition.Subscription}/{eh.ehName}/"
                            );
                    processors.Add(processorHost.RegisterEventProcessorFactoryAsync(
                        new LogEventProcessorFactory(
                            _consumersRepository,
                            ehDefinition.Subscription,
                            eh.resourceGroup,
                            eh.ehNamespace,
                            _httpClient)));
                    _eventProcessorHosts.Add(processorHost);
                }
            }
            Task.WaitAll(processors.ToArray());
        }


        private (string storageConnectionString, string container) GetStorageConnection()
        {
            string accountName = _configuration["Data:account-name"];
            string accountKey = _configuration["Data:account-key"];
            string storageEndpoint = _configuration["Data:storage-endpoint"];
            string connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix={storageEndpoint}";
            return (connectionString, _configuration["Consumer:container"]);
        }

        private async Task<IEnumerable<(string resourceGroup, string ehNamespace, string ehName, string consumerGroup, string connectionString)>>
            GetConnectionStrings(string consumerGroupName, string authorizationRuleName, string prefix, EventHubManagementClient ehClient, (string Subscription, string Namespace, string ResourceGroup) ehDefinition)
        {
            List<(string resourceGroup, string ehNamespace, string ehName, string consumerGroup, string connectionString)> connectionStrings =
                new List<(string resourceGroup, string ehNamespace, string ehName, string consumerGroup, string connectionString)>();
            try
            {
                var ehs = await ehClient.EventHubs.ListByNamespaceAsync(ehDefinition.ResourceGroup, ehDefinition.Namespace);
                foreach (var eh in ehs)
                {
                    if (!string.IsNullOrEmpty(prefix) && !eh.Name.StartsWith(prefix))
                    {
                        continue;
                    }
                    var consumerGroup =
                    await ehClient.ConsumerGroups.CreateOrUpdateAsync(
                        ehDefinition.ResourceGroup,
                        ehDefinition.Namespace,
                        eh.Name, consumerGroupName,
                        new ConsumerGroup()
                        {
                            UserMetadata = "automatically created croup for user consumption"
                        });
                    var authorizationRule = await ehClient.EventHubs.CreateOrUpdateAuthorizationRuleAsync(
                        ehDefinition.ResourceGroup,
                        ehDefinition.Namespace,
                        eh.Name, authorizationRuleName, new AuthorizationRule() { Rights = new string[] { "Listen" } });
                    var keys = await ehClient.EventHubs.ListKeysAsync(ehDefinition.ResourceGroup, ehDefinition.Namespace, eh.Name, authorizationRuleName);
                    var connectionString = keys.PrimaryConnectionString;
                    connectionStrings.Add((ehDefinition.ResourceGroup, ehDefinition.Namespace, eh.Name, consumerGroup.Name, connectionString));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return connectionStrings;
        }

        private async Task<EventHubManagementClient> GetEhClient(string subscription)
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
                SubscriptionId = subscription
            };
            return ehClient;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            List<Task> closing = new List<Task>();
            foreach (var eventProcessorHost in _eventProcessorHosts)
            {
                closing.Add(eventProcessorHost.UnregisterEventProcessorAsync());
            }
            Task.WaitAll(closing.ToArray());
            return Task.CompletedTask;
        }
    }


    public class LogEventProcessorFactory : IEventProcessorFactory
    {
        private readonly ConsumersRepository _consumersRepository;
        private readonly string subscription;
        private readonly string resourceGroup;
        private readonly string ehnamespace;
        private readonly HttpClient httpClient;

        public LogEventProcessorFactory(ConsumersRepository consumersRepository, string subscription, string resourceGroup, string ehnamespace, HttpClient httpClient)
        {
            _consumersRepository = consumersRepository;
            this.subscription = subscription;
            this.resourceGroup = resourceGroup;
            this.ehnamespace = ehnamespace;
            this.httpClient = httpClient;
        }
        public IEventProcessor CreateEventProcessor(PartitionContext context)
        {
            return new LogEventProcessor(_consumersRepository, subscription, resourceGroup, ehnamespace, httpClient);
        }
    }
    public class LogEventProcessor : IEventProcessor
    {
        private readonly ConsumersRepository _consumersRepository;
        private readonly string subscription;
        private readonly string resourceGroup;
        private readonly string ehnamespace;
        private readonly HttpClient httpClient;

        public LogEventProcessor(ConsumersRepository consumersRepository, string subscription, string resourceGroup, string ehnamespace, HttpClient httpClient)
        {
            _consumersRepository = consumersRepository;
            this.subscription = subscription;
            this.resourceGroup = resourceGroup;
            this.ehnamespace = ehnamespace;
            this.httpClient = httpClient;
        }
        public Task CloseAsync(PartitionContext context, CloseReason reason)
        {
            return Task.CompletedTask;
        }

        public async Task OpenAsync(PartitionContext context)
        {
            await _consumersRepository.OpenConnection(subscription, resourceGroup, ehnamespace, context);
        }

        public Task ProcessErrorAsync(PartitionContext context, Exception error)
        {
            return Task.CompletedTask;
        }

        public async Task ProcessEventsAsync(PartitionContext context, IEnumerable<EventData> messages)
        {
            EventData lastMessage = null;
            StringBuilder sb = new StringBuilder();
            foreach (var message in messages)
            {
                dynamic content = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(message.Body));
                if (content.records != null)
                {
                    foreach (var record in content.records)
                    {
                        string sourcetype = "unknown";
                        if (record["category"] != null)
                        {
                            sourcetype = ((string)record.category).ToLowerInvariant();
                        }
                        else if (record["metricName"] != null)
                        {
                            sourcetype = "metric";
                        }
                        SplunkEvent splunkEvent = new SplunkEvent(sourcetype, record);
                        if (record["resourceId"] != null)
                        {
                            splunkEvent = ParseClientId(splunkEvent, (string)record.resourceId);
                        }
                        string postContent = JsonConvert.SerializeObject(splunkEvent);
                        sb.AppendLine(postContent);
                    }
                }
                else if (content.resourceId != null)
                {
                    SplunkEvent splunkEvent = new SplunkEvent("machine-diagnostics", content);
                    splunkEvent = ParseClientId(splunkEvent, (string)content.resourceId);
                    string postContent = JsonConvert.SerializeObject(splunkEvent);
                    sb.AppendLine(postContent);
                }
                lastMessage = message;
            }
            if (sb.Length > 0)
            {
                try
                {
                    var response = await httpClient.PostAsync("/services/collector", new StringContent(sb.ToString()));
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine(response);
                        Console.WriteLine(await response.Content.ReadAsStringAsync());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            if (lastMessage != null)
            {
                await _consumersRepository.UpdateCount(subscription, resourceGroup, ehnamespace, context, lastMessage);
            }
        }

        private SplunkEvent ParseClientId(SplunkEvent splunkEvent, string resourceId)
        {
            string[] current = resourceId.Split('/');
            if (current.Length < 8)
            {
                return splunkEvent;
            }
            splunkEvent.@event.subscription = current[2];
            splunkEvent.@event.resourceGroup = current[4];
            splunkEvent.@event.providerNamespace = current[6];
            if (current.Length > 8)
            {
                splunkEvent.@event.resourceType = current[7];
            }
            if (current.Length > 9)
            {
                splunkEvent.@event.childNamespace = current[8];
            }
            if (current.Length > 10)
            {
                splunkEvent.@event.childTopic = current[9];
            }
            splunkEvent.@event.resourceName = current[current.Length - 1];
            return splunkEvent;
        }

        private class SplunkEvent
        {
            public SplunkEvent(string sourcetype, dynamic eventcontent)
            {
                this.sourcetype = sourcetype;
                this.@event = eventcontent;
            }
            public string sourcetype { get; set; }
            public dynamic @event { get; set; }
        }
    }
}
