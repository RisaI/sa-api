
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

        [JsonPropertyName("units")]
        public string Units{ get; init; }

        // public string Name { get; set; }
        // public string Description { get; set; }

        public string Source { get; set; }

        [JsonConverter(typeof(AxisTypeConverter))]
        public Type XType { get; set; }
        [JsonConverter(typeof(AxisTypeConverter))]
        public Type YType { get; set; }

        [JsonConverter(typeof(RangeTupleConverter<List<DataRange>>))]
        public List<DataRange> DataRange { get; set; }

        [JsonIgnore]
        public HashSet<string> Variants { get; set; }

        public int VariantCount => Variants.Count;

        public Dataset(string id, string[] category, string? units, IIdentified source, Type xType, Type yType, IEnumerable<DataRange> xRange, IEnumerable<string>? variants)
        {
            Id = id;
            Category = category;
            Units = units ?? "s^-1";
            Source = source.Id;
            XType = xType;
            YType = yType;

            DataRange = xRange.ToList();

            Variants = variants?.Any() == true ? variants.ToHashSet() : new () { id };

            // TODO: custom exception
            if (xRange.Any(r => r.Type != XType))
                throw new Exception("Invalid data type in range specification");
        }
    }

    public class RangeTupleConverter<T> : JsonConverter<T?> where T : IEnumerable<DataRange>
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return default(T);
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            
            if (value != null)
            {
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
            }

            writer.WriteEndArray();
        }

        void WriteValue(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value is DateTime date)
            {
                writer.WriteNumberValue(date.ToMinuteRepre());
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
            { typeof(HighPrecTime), "time64" },

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