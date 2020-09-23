using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO.Compression;

namespace SAApi.Services
{
    public class ResourceCache : IDisposable
    {
        public ResourceCache()
        {

        }

        private Dictionary<string, IEnumerable<string[]>> CSVs = new Dictionary<string, IEnumerable<string[]>>();
        public async Task<IEnumerable<string[]>> GetCSVFileAsync(string filename, char delimiter, int skipLines = 0)
        {
            if (CSVs.ContainsKey(filename))
                return CSVs[filename];

            using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (var reader = new StreamReader(file))
            {
                var list = new List<string[]>();

                for (int i = 0; i < skipLines && !reader.EndOfStream; ++i)
                    await reader.ReadLineAsync();

                while (!reader.EndOfStream)
                {
                    var line = (await reader.ReadLineAsync()).Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                        continue;

                    list.Add(line.Split(delimiter));
                }

                CSVs.Add(filename, list);
                return list;
            }
        }
        public async Task<IEnumerable<string[]>> GetCSVFileFromZipAsync(string zipFile, string filename, char delimiter, int skipLines = 0)
        {
            if (CSVs.ContainsKey($"{zipFile}:{filename}"))
                return CSVs[$"{zipFile}:{filename}"];

            using (var stream = new FileStream(zipFile, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                var entry = zip.GetEntry(filename);

                using (var fStream = entry.Open())
                using (var reader = new StreamReader(fStream))
                {
                    var list = new List<string[]>();

                    for (int i = 0; i < skipLines && !reader.EndOfStream; ++i)
                        await reader.ReadLineAsync();

                    while (!reader.EndOfStream)
                    {
                        var line = (await reader.ReadLineAsync()).Trim();
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                            continue;

                        list.Add(line.Split(delimiter));
                    }

                    CSVs.Add($"{zipFile}:{filename}", list);
                    return list;
                }
            }
        }

        public void Dispose()
        {
            
        }
    }
}