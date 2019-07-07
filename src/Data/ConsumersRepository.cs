using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.EventHubs.Processor;
using Microsoft.Extensions.Configuration;
using splunk_eventhubs.Hubs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace splunk_eventhubs.Data
{
    public class ConsumersRepository
    {
        private ConcurrentDictionary<string, EhNamespaceData> _content;
        private readonly IHubContext<StatusHub> hub;
        private readonly IConfiguration _configuration;
        private readonly CloudTableClient _tableClient;

        public ConsumersRepository(IHubContext<StatusHub> hub, IConfiguration configuration)
        {
            _content = new ConcurrentDictionary<string, EhNamespaceData>();
            this.hub = hub;
            this._configuration = configuration;
            string accountName = _configuration["Data:account-name"];
            string accountKey = _configuration["Data:account-key"];
            string storageEndpoint = _configuration["Data:storage-endpoint"];
            var account = CloudStorageAccount.Parse($"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix={storageEndpoint}");
            _tableClient = account.CreateCloudTableClient();
        }
        public async Task<(string Subscription, string Namespace, string ResourceGroup)[]> GetMonitoringEh()
        {
            var table = _tableClient.GetTableReference("ehsplunkstorage");
            await table.CreateIfNotExistsAsync();
            var contents = await table.ExecuteQuerySegmentedAsync(new TableQuery<EhProcessorNamespacesEntity>(), null);
            var result = new List<(string Subscription, string Namespace, string ResourceGroup)>();
            foreach (var record in contents)
            {
                result.Add((record.Subscription, record.Namespace, record.ResourceGroup));
            }
            return result.ToArray();
        }
        public async Task OpenConnection(string subscription, string resourceGroup, string ehNamespace, PartitionContext partitionContext)
        {
            var partitionData = new PartitionData()
            {
                Name = partitionContext.PartitionId,
                Sequence = partitionContext.RuntimeInformation.LastSequenceNumber,
                RetrievalTime = partitionContext.RuntimeInformation.RetrievalTime,
                LastEnqueueTime = partitionContext.RuntimeInformation.LastEnqueuedTimeUtc
            };
            _ = _content.AddOrUpdate(ehNamespace, new EhNamespaceData()
            {
                Namespace = ehNamespace,
                Subscription = subscription,
                ResourceGroup = resourceGroup,
                Ehs = new Dictionary<string, EhData>()
                {
                    { partitionContext.EventHubPath , new EhData() {
                        Name = partitionContext.EventHubPath,
                        Partitions = new Dictionary<string, PartitionData>()
                        {
                            { partitionContext.PartitionId, partitionData }
                        }
                    } }
                }
            }, (e, newValue) =>
            {
                var b = partitionData;
                if (newValue.Ehs.ContainsKey(partitionContext.EventHubPath))
                {
                    if (newValue.Ehs[partitionContext.EventHubPath].Partitions.ContainsKey(partitionContext.PartitionId))
                    {
                        newValue.Ehs[partitionContext.EventHubPath].Partitions[partitionContext.PartitionId] = partitionData;
                    }
                    else
                    {
                        newValue.Ehs[partitionContext.EventHubPath].Partitions.Add(partitionContext.PartitionId, partitionData);
                    }
                }
                else
                {
                    newValue.Ehs.Add(partitionContext.EventHubPath, new EhData()
                    {
                        Name = partitionContext.EventHubPath,
                        Partitions = new Dictionary<string, PartitionData>()
                        {
                            { partitionContext.PartitionId, partitionData }
                        }
                    });
                }
                return newValue;
            });
            await hub.Clients.All.SendAsync("updateData", _content);
        }
        public class EhProcessorNamespacesEntity : TableEntity
        {
            public EhProcessorNamespacesEntity()
            {
            }

            public EhProcessorNamespacesEntity(string ehnamespace, string subscription)
            {
                PartitionKey = subscription;
                RowKey = ehnamespace;
                Namespace = ehnamespace;
                Subscription = subscription;
            }
            public string Subscription { get { return PartitionKey; } set { PartitionKey = value; } }
            public string Namespace { get { return RowKey; } set { RowKey = value; } }
            public string ResourceGroup { get; set; }

        }

        public class EhNamespaceData
        {
            public string Namespace { get; set; }
            public string Subscription { get; set; }
            public string ResourceGroup { get; set; }
            public Dictionary<string, EhData> Ehs { get; set; }

        }
        public class EhData
        {
            public string Name { get; set; }
            public Dictionary<string, PartitionData> Partitions { get; set; }
        }
        public class PartitionData
        {
            public string Name { get; set; }
            public long Sequence { get; internal set; }
            public DateTime RetrievalTime { get; internal set; }
            public DateTime LastEnqueueTime { get; internal set; }
            public DateTime LastReadEnqueueTime { get; internal set; }
            public string LastReadOffset { get; internal set; }
            public long LastReadSequenceNumber { get; internal set; }
        }

        public async Task UpdateCount(string subscription, string resourceGroup, string ehnamespace, PartitionContext context, EventData lastMessage)
        {
            if (!_content.ContainsKey(ehnamespace))
            {
                var partitionData = new PartitionData()
                {
                    Name = context.PartitionId,
                    Sequence = context.RuntimeInformation.LastSequenceNumber,
                    RetrievalTime = context.RuntimeInformation.RetrievalTime,
                    LastEnqueueTime = context.RuntimeInformation.LastEnqueuedTimeUtc
                };
                _content.AddOrUpdate(ehnamespace, new EhNamespaceData()
                {
                    Namespace = ehnamespace,
                    Subscription = subscription,
                    ResourceGroup = resourceGroup,
                    Ehs = new Dictionary<string, EhData>()
                {
                    { context.EventHubPath , new EhData() {
                        Name = context.EventHubPath,
                        Partitions = new Dictionary<string, PartitionData>()
                        {
                            { context.PartitionId, partitionData }
                        }
                    } }
                }
                }, (e, newValue) =>
                {
                    var b = partitionData;
                    if (newValue.Ehs.ContainsKey(context.EventHubPath))
                    {
                        if (newValue.Ehs[context.EventHubPath].Partitions.ContainsKey(context.PartitionId))
                        {
                            newValue.Ehs[context.EventHubPath].Partitions[context.PartitionId] = partitionData;
                        }
                        else
                        {
                            newValue.Ehs[context.EventHubPath].Partitions.Add(context.PartitionId, partitionData);
                        }
                    }
                    else
                    {
                        newValue.Ehs.Add(context.EventHubPath, new EhData()
                        {
                            Name = context.EventHubPath,
                            Partitions = new Dictionary<string, PartitionData>()
                        {
                            { context.PartitionId, partitionData }
                        }
                        });
                    }
                    return newValue;
                });
            }

            if (!_content[ehnamespace].Ehs.ContainsKey(context.EventHubPath))
            {
                var partitionData = new PartitionData()
                {
                    Name = context.PartitionId,
                    Sequence = context.RuntimeInformation.LastSequenceNumber,
                    RetrievalTime = context.RuntimeInformation.RetrievalTime,
                    LastEnqueueTime = context.RuntimeInformation.LastEnqueuedTimeUtc
                };
                _content[ehnamespace].Ehs.Add(
                        context.EventHubPath, new EhData()
                        {
                            Name = context.EventHubPath,
                            Partitions = new Dictionary<string, PartitionData>()
                                {
                                    { context.PartitionId, partitionData }
                                }
                        }
                    );
            }
            if (!_content[ehnamespace].Ehs[context.EventHubPath].Partitions.ContainsKey(context.PartitionId))
            {
                var partitionData = new PartitionData()
                {
                    Name = context.PartitionId,
                    Sequence = context.RuntimeInformation.LastSequenceNumber,
                    RetrievalTime = context.RuntimeInformation.RetrievalTime,
                    LastEnqueueTime = context.RuntimeInformation.LastEnqueuedTimeUtc
                };
                _content[ehnamespace].Ehs[context.EventHubPath].Partitions.Add(context.PartitionId, partitionData);
            }
            _content[ehnamespace].Ehs[context.EventHubPath].Partitions[context.PartitionId].LastReadOffset = lastMessage.SystemProperties.Offset;
            _content[ehnamespace].Ehs[context.EventHubPath].Partitions[context.PartitionId].LastReadEnqueueTime = lastMessage.SystemProperties.EnqueuedTimeUtc;
            _content[ehnamespace].Ehs[context.EventHubPath].Partitions[context.PartitionId].LastReadSequenceNumber = lastMessage.SystemProperties.SequenceNumber;
            await hub.Clients.All.SendAsync("updateData", _content);
        }

    }
}
