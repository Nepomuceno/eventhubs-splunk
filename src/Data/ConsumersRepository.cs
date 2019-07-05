using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace splunk_eventhubs.Data
{
    public class ConsumersRepository
    {
        public EhDefinition[] GetMonitoringEh()
        {
            return new EhDefinition[]
            {

            };
        }
        public class EhDefinition
        {
            public string Name { get; set; }
            public string ResourceGroup { get; set; }
        }
    }
}
