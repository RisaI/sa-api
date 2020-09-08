using System;
using System.Text.Json.Serialization;
using SAApi.Data;

namespace SAApi.Models
{
    public sealed class PipelineSpecs
    {
        [JsonConverter(typeof(AxisTypeConverter))]
        public Type XType { get; set; }
        [JsonConverter(typeof(AxisTypeConverter))]
        public Type YType { get; set; }
    }
}