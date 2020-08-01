
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAApi.Data
{
    public class Dataset : IIdentified
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public string Source { get; set; }

        [JsonConverter(typeof(AxisTypeConverter))]
        public Type XType { get; set; }
        [JsonConverter(typeof(AxisTypeConverter))]
        public Type YType { get; set; }

        [JsonConverter(typeof(RangeTupleConverter))]
        public (object, object) AvailableXRange { get; set; }

        public string[] Variants { get; set; }

        public Dataset(string id, string name, string description, IIdentified source, Type xType, Type yType, (object, object) xRange, params string[] variants)
        {
            Id = id;
            Name = name;
            Description = description;
            Source = source.Id;
            XType = xType;
            YType = yType;

            AvailableXRange = xRange;

            Variants = variants;

            if (xRange.Item1?.GetType() != XType || xRange.Item2?.GetType() != XType)
                throw new Exception("Invalid data type in range specification");

            // TODO: custom exception
        }
    }

    public class RangeTupleConverter : JsonConverter<(object, object)>
    {
        public override (object, object) Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return (null, null);
        }

        public override void Write(Utf8JsonWriter writer, (object, object) value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            
            if (value.Item1 == null)
                writer.WriteNullValue();
            else
                WriteValue(writer, value.Item1, options);

            if (value.Item2 == null)
                writer.WriteNullValue();
            else
                WriteValue(writer, value.Item2, options);

            writer.WriteEndArray();
        }

        void WriteValue(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value is DateTime date)
            {
                writer.WriteNumberValue(date.Ticks);
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
            { typeof(long), "long" },

            { typeof(ushort), "ushort" },
            { typeof(uint), "uint" },
            { typeof(ulong), "ulong" },

            { typeof(float), "float" },
            { typeof(double), "double" },
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