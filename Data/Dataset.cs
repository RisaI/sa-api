
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace SAApi.Data
{
    public class Dataset : IIdentified
    {
        public string Id { get; set; }
        public string[] Category { get; set; }

        // public string Name { get; set; }
        // public string Description { get; set; }

        public string Source { get; set; }

        [JsonConverter(typeof(AxisTypeConverter))]
        public Type XType { get; set; }
        [JsonConverter(typeof(AxisTypeConverter))]
        public Type YType { get; set; }

        [JsonConverter(typeof(RangeTupleConverter))]
        public IEnumerable<DataRange> DataRange { get; set; }

        [JsonIgnore]
        public string[] Variants { get; set; }

        public int VariantCount => Variants?.Length ?? 1;

        public Dataset(string id, string[] category, IIdentified source, Type xType, Type yType, IEnumerable<DataRange> xRange, params string[] variants)
        {
            Id = id;
            Category = category;
            // Name = name;
            // Description = description;
            Source = source.Id;
            XType = xType;
            YType = yType;

            DataRange = xRange;

            Variants = variants;

            // TODO: custom exception
            if (xRange.Any(r => r.Type != XType))
                throw new Exception("Invalid data type in range specification");
        }
    }

    public class RangeTupleConverter : JsonConverter<IEnumerable<DataRange>>
    {
        public override IEnumerable<DataRange> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Enumerable.Empty<DataRange>();
        }

        public override void Write(Utf8JsonWriter writer, IEnumerable<DataRange> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            
            foreach (var val in value)
            {
                writer.WriteStartArray();

                if (val.From == null)
                    writer.WriteNullValue();
                else
                    WriteValue(writer, val.From, options);

                if (val.To == null)
                    writer.WriteNullValue();
                else
                    WriteValue(writer, val.To, options);

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        void WriteValue(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value is DateTime date)
            {
                writer.WriteNumberValue(((DateTimeOffset)date).ToUnixTimeSeconds());
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    public class AxisTypeConverter : JsonConverter<Type>
    {
        public static readonly Dictionary<Type, string> KnownTypes = new Dictionary<Type, string>() {
            { typeof(DateTime), "datetime" },

            { typeof(byte), "byte" },
            { typeof(bool), "boolean" },
            
            { typeof(short), "short" },
            { typeof(int), "int" },
            // { typeof(long), "long" },

            { typeof(ushort), "ushort" },
            { typeof(uint), "uint" },
            // { typeof(ulong), "ulong" },

            { typeof(float), "float" },
            // { typeof(double), "double" },
        };

        public override Type Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var val = reader.GetString();

            return KnownTypes.FirstOrDefault(kv => kv.Value == val).Key;
        }

        public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
        {
            if (KnownTypes.ContainsKey(value))
                writer.WriteStringValue(KnownTypes[value]);
            else
                writer.WriteNullValue();
        }
    }
}