using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SAApi.Data.Sources.HP
{
    public class HPDataSource : DataSource
    {
        public const string DateFormat = "yyyy/MM/dd HH:mm", DirectoryDateFormat = "yyyyMMdd", LdevMapFeature = "ldev_map";
        public static readonly string[] DetectedPatterns = new string[] { "LDEV_*.zip", "Phy*_dat.ZIP", "Port_*.ZIP" };

        string DataPath { get { return _Config["path"]; } }

        public HPDataSource(string id, IConfigurationSection config)
            : base(id, "Disková pole HP", config)
        {

        }

        private List<HPDataset> _Datasets = new List<HPDataset>();
        private string[] _Features = new string[0];

        public override IEnumerable<Dataset> Datasets => _Datasets;
        public override IEnumerable<string> Features => _Features;

        public IEnumerable<DateTime> GetAvailableDates { get { return Directory.GetDirectories(DataPath).Select(d => DateTime.ParseExact(Path.GetFileName(d).Substring(4), DirectoryDateFormat, null)); } }
        public string GetPathFromDate(DateTime date) => Path.Combine(DataPath, $"PFM_{date.ToString(DirectoryDateFormat)}");

        public override Task<Node> GetNode(string id, string variant, Services.ResourceCache resCache)
        {
            var dataset = _Datasets.Find(d => d.Id == id);
            return Task.FromResult<Node>(new HPDataNode(this, dataset, variant, resCache));
        }

        private List<HPDataset> _temp = new List<HPDataset>();
        private List<LDEVInfo> LDEVs = new List<LDEVInfo>();
        public override async Task OnTick(IServiceScope scope)
        {
            var dirs = Directory.GetDirectories(DataPath);
            var dates = dirs.Select(d => DateTime.ParseExact(Path.GetFileName(d).Substring(4), DirectoryDateFormat, null));
            var nearestDate = dates.Max();
            var availableRange = (dates.Min(), nearestDate.AddDays(1));

            // ? config.zip
            // ? capacity.cfg
            // ? LDEVEachOfCU_dat

            var latestDir = Path.Combine(DataPath, $"PFM_{nearestDate.ToString(DirectoryDateFormat)}");

            foreach (var fileName in DetectedPatterns.SelectMany(p => Directory.GetFiles(latestDir, p)))
                await ScanZip(Path.GetDirectoryName(fileName), Path.GetFileName(fileName), _temp, availableRange);

            {
                var dict = new Dictionary<string, List<string>>();
                foreach (var date in dates)
                {
                    var path = GetPathFromDate(date);

                    foreach (var fileName in DetectedPatterns.SelectMany(p => Directory.GetFiles(path, p)))
                        await ScanZipVariants(Path.GetDirectoryName(fileName), Path.GetFileName(fileName), dict);
                }

                foreach (var set in _temp)
                {
                    set.Variants = dict[set.FileEntry].ToArray();
                }
            }


            var configs = new string[] {
                Path.Combine(latestDir, "config.zip"),
                Path.Combine(DataPath, "config.zip"),
            };
            var availableConf = configs.FirstOrDefault(p => File.Exists(p));

            if (availableConf != null)
            {
                this.LDEVs = await LoadConfig(availableConf);
                _Features = new string[] { LdevMapFeature };
            }
            else
            {
                _Features = new string[0];
            }

            // Swapnout temp a ostrej
            {
                var a = _Datasets;
                _Datasets = _temp;
                _temp = a;
                _temp.Clear();
            }
        }

        static async Task<List<LDEVInfo>> LoadConfig(string path)
        {
            List<LDEVInfo> ldevs = new List<LDEVInfo>();

            // Load configuration
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
            {
                using (var csvFile = zip.GetEntry("LdevInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync(); // Skip first two lines

                    while (!reader.EndOfStream)
                        ldevs.Add(new LDEVInfo((await reader.ReadLineAsync()).Split(',')));
                }

                var hostWWNs = new Dictionary<string, List<WWNInfo>>();
                using (var csvFile = zip.GetEntry("LunInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    var ldevHostsMap = ldevs.ToDictionary(k => k.Id, v => new List<HostPort>());

                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync();

                    while (!reader.EndOfStream)
                    {
                        var cols = (await reader.ReadLineAsync()).Split(',');
                        if (string.IsNullOrWhiteSpace(cols[5]) || !ldevHostsMap.ContainsKey(cols[5]))
                            continue;

                        var ldev = ldevHostsMap[cols[5]];
                        var host = new HostPort(cols[1], cols[0]);
                        var hostAlias = $"{host.Hostgroup}:{host.Port}";

                        if (!hostWWNs.ContainsKey(hostAlias))
                            hostWWNs.Add(hostAlias, new List<WWNInfo>());

                        ldev.Add(host);
                    }

                    foreach (var ldev in ldevs)
                        ldev.HostPorts = ldevHostsMap[ldev.Id].Distinct().ToArray();
                }

                var wwns = new List<WWNInfo>();
                using (var csvFile = zip.GetEntry("WwnInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync(); // Skip first two lines

                    while (!reader.EndOfStream)
                    {
                        var wwn = new WWNInfo((await reader.ReadLineAsync()).Split(','));
                        var host = $"{wwn.Hostgroup}:{wwn.Port}";
                        wwns.Add(wwn);

                        if (hostWWNs.ContainsKey(host))
                            hostWWNs[host].Add(wwn);
                    }
                }

                foreach (var ldev in ldevs)
                    ldev.Wwns = ldev.HostPorts.SelectMany(hp => hostWWNs[$"{hp.Hostgroup}:{hp.Port}"]).ToArray();
            }

            return ldevs;
        }

        Task ScanZip(string directory, string zipPath, IList<HPDataset> output, (DateTime, DateTime) range)
        {
            using (var stream = new FileStream(Path.Combine(directory, zipPath), FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                foreach (var entry in zip.Entries)
                {
                    var id = Path.GetFileNameWithoutExtension(entry.FullName);

                    output.Add(
                        new HPDataset(
                        id,
                        id.Replace('_', ' '),
                        string.Empty,
                        this,
                        typeof(DateTime),
                        typeof(int),
                        range,
                        null
                        )
                        {
                            ZipPath = zipPath,
                            FileEntry = entry.FullName,
                        });
                }
            }

            return Task.CompletedTask;
        }

        async Task ScanZipVariants(string directory, string zipPath, Dictionary<string, List<string>> output)
        {
            using (var stream = new FileStream(Path.Combine(directory, zipPath), FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!output.ContainsKey(entry.FullName))
                        output.Add(entry.FullName, new List<string>());

                    var list = output[entry.FullName];

                    list.AddRange((await ScanCsvHeader(entry.Open())).Skip(2).Where(v => !list.Contains(v)));
                }
            }
        }

        async Task<IEnumerable<string>> ScanCsvHeader(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                for (int i = 0; i < 6; ++i)
                    await reader.ReadLineAsync();

                return (await reader.ReadLineAsync()).Split(',').Select(a => a.Trim('"'));
            }
        }

        public override async Task<object> ActivateFeatureAsync(string feature, Stream body)
        {
            if (feature == "ldev_map")
            {
                var @params = await JsonSerializer.DeserializeAsync<LDEVMapRequest>(body);
                var ldev = LDEVs.Find(ldev => ldev.Id.Equals(@params.Id.Substring(0, 8), StringComparison.InvariantCultureIgnoreCase));
                return ldev;
            }
            else
                throw new NotImplementedException();
        }

        public override async Task GetBulkData(string id, IEnumerable<string> variant, DataRange range, Stream stream)
        {
            var dataset = this.Datasets.First(d => d.Id == id);
            var bound = DataRange.BoundingBox(dataset.DataRange);

            if (!bound.Contains(range)) return;


        }
    }

    record LDEVMapRequest
    {
        [JsonPropertyName("id")] public string Id { get; init; }
    }

    public class HPDataset : Dataset
    {
        public string ZipPath { get; init; }
        public string FileEntry { get; init; }
        public bool IsLdevData { get; init; }

        public HPDataset(
            
            string id,
            string name,
            string description,
            IIdentified source,
            Type xType,
            Type yType,
            (DateTime From, DateTime To) xRange,
            params string[] variants
            
            ) : base(id, name, description, source, xType, yType, new [] { new DataRange(typeof(DateTime), xRange.From, xRange.To) }, variants)
        {
        }
    }
}