using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace SAApi.Data.Sources.HP
{
    public record CsvMeta(string Name, string SerialNumber, DateTime From, DateTime To, int SamplingRate, CsvReader.CsvHeader[] Headers)
    {

        public static CsvMeta FromStream(Stream csv)
        {
            using var reader = new CsvReader(csv, true);
            var result = new CsvMeta(reader.Name, reader.SerialNumber, reader.From, reader.To, reader.SamplingRate, reader.ReadAllHeaders().ToArray());
            return result;
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
                if (Headers[j].Columns.Length != meta.Headers[j].Columns.Length)
                    return false;

                for (int i = 0; i < Headers[j].Columns.Length; ++i)
                {
                    if (Headers[j].Columns[i] != meta.Headers[j].Columns[i])
                        return false;
                }
            }

            return meta.SamplingRate == SamplingRate;
        }

        public void WriteHead(TextWriter writer)
        {
            writer.WriteLine(Name);
            writer.WriteLine($"Serial number : {SerialNumber}");
            writer.WriteLine($"From : {From.ToString(CsvReader.RangeDateFormat)}");
            writer.WriteLine($"To   : {To.ToString(CsvReader.RangeDateFormat)}");
            writer.WriteLine($"sampling rate : {SamplingRate}");
            writer.WriteLine();
        }

        public void WriteHeader(TextWriter writer, int headerIdx)
        {
            writer.WriteLine(string.Join(',', Headers[headerIdx]));
        }
    }
}