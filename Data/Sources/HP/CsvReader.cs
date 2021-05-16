using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SAApi.Data.Sources.HP
{
    public sealed class CsvReader : IDisposable
    {
        public const string RangeDateFormat = "yyyy/MM/dd HH:mm";

        private StreamReader Reader;

        public string Name { get; private set; }
        public string SerialNumber { get; private set; }
        public DateTime From { get; private set; }
        public DateTime To { get; private set; }
        public int SamplingRate { get; private set; }

        public CsvReader(Stream stream, bool leaveOpen)
        {
            Reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, -1, leaveOpen);
            ReadHead();
        }

        public CsvReader(StreamReader reader)
        {
            Reader = reader;
            ReadHead();
        }

        private void ReadHead()
        {
            T ReadProp<T>(Func<ReadOnlyMemory<char>, ReadOnlyMemory<char>, T> parser)
            {
                var prop = ParseProperty(ReadLine() ?? throw new EndOfStreamException("Invalid CSV stream."));
                return parser.Invoke(prop.prop, prop.val);
            }
            DateTime ReadPropDate() => ReadProp((_, v) => DateTime.ParseExact(v.Span, RangeDateFormat, null));

            Name = ReadLine() ?? throw new EndOfStreamException("Invalid CSV stream.");
            SerialNumber = ReadProp((k, v) => v.ToString());
            From = ReadPropDate();
            To = ReadPropDate();
            SamplingRate = ReadProp((_, v) => v.IsEmpty ? 1 : int.Parse(v.Span));

            ReadLine(); // Skip empty line
        }

        int LineNumber;
        private string ReadLine()
        {
            ++LineNumber;
            return Reader.ReadLine();
        }

        public IEnumerable<CsvHeader> ReadAllHeaders()
        {
            while (!Reader.EndOfStream)
            {
                var line = ReadLine();

                if (line.StartsWith("\"No.\""))
                    yield return CsvHeader.Parse(LineNumber - 1, line.AsMemory());
            }
        }

        CsvHeader _CurrentHeader = null;
        public IEnumerable<(DateTime Time, Memory<(string Variant, int Data)> Values)> ReadNextBlock()
        {

            while (!Reader.EndOfStream && _CurrentHeader == null)
            {
                var line = ReadLine();

                if (line.StartsWith("\"No.\""))
                    _CurrentHeader = CsvHeader.Parse(LineNumber - 1, line.AsMemory());
            }

            if (_CurrentHeader == null) yield break;

            (string, int)[] row = new (string, int)[_CurrentHeader.Columns.Length - 2];

            while (!Reader.EndOfStream)
            {
                var line = ReadLine();

                if (line.StartsWith("\"No.\""))
                {
                    _CurrentHeader = CsvHeader.Parse(LineNumber - 1, line.AsMemory());
                    break;
                }

                ReadOnlySpan<char> span = line.AsSpan(line.IndexOf(',') + 1); // Skip line number

                int cursor = span.IndexOf(',');
                var time = DateTime.ParseExact(span.Slice(0, cursor).Trim('"'), RangeDateFormat, null);
                span = span.Slice(++cursor);

                int idx = 0;

                while (span.Length > 0)
                {
                    cursor = span.IndexOf(',') switch {
                        int i when i >= 0 => i,
                        _ => span.Length,
                    };

                    row[idx] = (
                        _CurrentHeader.Columns[2 + idx],
                        int.Parse(span.Slice(0, cursor))
                    );

                    span = span.Slice(Math.Min(span.Length, cursor + 1));
                    idx += 1;
                }

                yield return (time, row);
            }
        }

        public void Dispose()
        {
            Reader.Dispose();
        }

        public record CsvHeader(long Line, string[] Columns)
        {
            [JsonIgnore] public ReadOnlySpan<string> VariantsSpan => Columns.AsSpan(2);
            [JsonIgnore] public ReadOnlyMemory<string> Variants => Columns.AsMemory(2);

            public static CsvHeader Parse(long lineNumber, ReadOnlyMemory<char> line)
            {
                IEnumerable<string> Split()
                {
                    for (int i = 0; i < line.Length;)
                    for (int j = i; j < line.Length; ++j)
                    {
                        if (line.Span[j] == ',' || j == line.Length - 1)
                        {
                            yield return line.Slice(i, j - i).Trim('"').ToString();
                            i = j + 1;
                            break;
                        }
                    }
                }

                return new CsvHeader(lineNumber, Split().ToArray());
            }

            public void Serialize(BinaryWriter writer)
            {
                writer.Write7BitEncodedInt64(Line);
                writer.Write7BitEncodedInt(Columns.Length);
                foreach (var col in Columns) writer.Write(col);
            }

            public static CsvHeader Deserialize(BinaryReader reader)
            {
                return new CsvHeader(
                    reader.Read7BitEncodedInt64(),
                    Enumerable.Range(0, reader.Read7BitEncodedInt())
                        .Select(i => reader.ReadString())
                        .ToArray()
                );
            }
        }

#region Static
        public static (ReadOnlyMemory<char> prop, ReadOnlyMemory<char> val) ParseProperty(string line)
        {
            var idx = line.IndexOf(':');
            return (line.AsMemory(0, idx).Trim(), line.AsMemory(idx + 1).Trim());
        }
#endregion
    }

}