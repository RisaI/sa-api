
using System;
using System.Text.Json.Serialization;

namespace SAApi.Data
{
    public class DataSet : IIdentified
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public string Source { get; set; }

        [JsonIgnore]
        public Type XType { get; set; }
        [JsonIgnore]
        public Type YType { get; set; }

        public (object, object) AvailableXRange { get; set; }

        public DataSet(string id, string name, string description, IIdentified source, Type xType, Type yType, (object, object) xRange)
        {
            Id = id;
            Name = name;
            Description = description;
            Source = source.Id;
            XType = xType;
            YType = yType;

            AvailableXRange = xRange;

            if (xRange.Item1?.GetType() != XType || xRange.Item2?.GetType() != XType)
                throw new Exception("Invalid data type in range specification");

            // TODO: custom exception
        }
    }
}