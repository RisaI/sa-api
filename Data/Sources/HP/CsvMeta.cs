using System;
using System.IO;
using System.Collections.Generic;

namespace SAApi.Data.Sources.HP
{
    public record CsvMeta(string Name, string SerialNumber, DateTime From, DateTime To, int SamplingRate, string[][] Headers)
    {
        public const string RangeDateFormat = "yyyy/MM/dd HH:mm";

        public static CsvMeta FromStream(Stream csv)
        {
            string name, serialNumber;
            DateTime from, to;
            int samplingRate;

            // csv.Position = 0;
            using var reader = new StreamReader(csv, null, true, -1, true);
            T ReadProp<T>(Func<string, string, T> parser)
            {
                var prop = ReadProperty(reader.ReadLine() ?? throw new EndOfStreamException("Invalid CSV stream."));
                return parser.Invoke(prop.prop, prop.val);
            }
            DateTime ReadPropDate() => ReadProp((_, v) => DateTime.ParseExact(v, RangeDateFormat, null));

            name = reader.ReadLine() ?? throw new EndOfStreamException("Invalid CSV stream.");
            serialNumber = ReadProp((k, v) => v);
            from = ReadPropDate();
            to = ReadPropDate();
            samplingRate = ReadProp((_, v) => string.IsNullOrWhiteSpace(v) ? 1 : int.Parse(v));

            reader.ReadLine(); // Skip empty line

            var headerList = new List<string[]>(8);

            int lineIdx = 6;
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line == null) break;

                ++lineIdx;

                if (line.StartsWith("\"No.\""))
                    headerList.Add(line.Split(','));
            }

            return new CsvMeta(name, serialNumber, from, to, samplingRate, headerList.ToArray());
        }

        static (string prop, string val) ReadProperty(string line)
        {
            var idx = line.IndexOf(':');
            return (line.Substring(0, idx - 1).Trim(), line.Substring(idx + 1).Trim());
        }

        public bool CompatibleWith(CsvMeta meta)
        {
            if (meta.Headers.Length != Headers.Length)
                return false;

            for (int j = 0; j < Headers.Length; ++j)
            {
                if (Headers[j].Length != meta.Headers[j].Length)
                    return false;

                for (int i = 0; i < Headers[j].Length; ++i)
                {
                    if (Headers[j][i] != meta.Headers[j][i])
                        return false;
                }
            }

            return meta.SamplingRate == SamplingRate;
        }

        public void WriteHead(TextWriter writer)
        {
            writer.WriteLine(Name);
            writer.WriteLine($"Serial number : {SerialNumber}");
            writer.WriteLine($"From : {From.ToString(RangeDateFormat)}");
            writer.WriteLine($"To   : {To.ToString(RangeDateFormat)}");
            writer.WriteLine($"sampling rate : {SamplingRate}");
            writer.WriteLine();
        }

        public void WriteHeader(TextWriter writer, int headerIdx)
        {
            writer.WriteLine(string.Join(',', Headers[headerIdx]));
        }
    }
}