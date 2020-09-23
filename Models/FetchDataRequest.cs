using System;
using System.Collections.Generic;

namespace SAApi.Models
{
    public sealed class FetchDataRequest
    {
        public string From { get; set; }
        public string To   { get; set; }

        public NodeDescriptor[] Pipelines { get; set; }
    }

    public sealed class NodeDescriptor
    {
        public string Type { get; set; }
        public DatasetDescriptor Dataset { get; set; }
        public Dictionary<string, object> Options { get; set; }

        public NodeDescriptor Child { get; set; }
        public NodeDescriptor[] Children { get; set; }
    }

    public sealed class DatasetDescriptor
    {
        public string Source { get; set; }
        public string Id { get; set; }
        public string Variant { get; set; }
    }
}