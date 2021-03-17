using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SAApi.Data.Sources
{
    public class HPDataSource : DataSource
    {
        public const string DateFormat = "yyyy/MM/dd HH:mm", DirectoryDateFormat = "yyyyMMdd";
        public static readonly string[] DetectedPatterns = new string[] { "LDEV_*.zip", "Phy*_dat.ZIP", "Port_*.ZIP" };

        string DataPath { get { return _Config["path"]; } }

        public HPDataSource(string id, IConfigurationSection config)
            : base(id, "Disková pole HP", config)
        {

        }

        private List<HPDataset> _Datasets = new List<HPDataset>();
        public override IEnumerable<Dataset> Datasets => _Datasets;
        private string[] _Features = new string[] { "ldev_map" };
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

            // Load configuration
            using (var file = new FileStream(Path.Combine(latestDir, "config.zip"), FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
            {
                LDEVs.Clear();
                using (var csvFile = zip.GetEntry("LdevInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync(); // Skip first two lines

                    while (!reader.EndOfStream)
                        LDEVs.Add(new LDEVInfo((await reader.ReadLineAsync()).Split(',')));
                }

                var hostWWNs = new Dictionary<string, List<WWNInfo>>();
                using (var csvFile = zip.GetEntry("LunInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    var ldevHostsMap = LDEVs.ToDictionary(k => k.Id, v => new List<HostPort>());

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

                    foreach (var ldev in LDEVs)
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

                foreach (var ldev in LDEVs)
                    ldev.Wwns = ldev.HostPorts.SelectMany(hp => hostWWNs[$"{hp.Hostgroup}:{hp.Port}"]).ToArray();
            }

            // Swapnout temp a ostrej
            {
                var a = _Datasets;
                _Datasets = _temp;
                _temp = a;
                _temp.Clear();
            }
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
                        ) {
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
                var @params = await System.Text.Json.JsonSerializer.DeserializeAsync<LDEVMapRequest>(body);
                var ldev = LDEVs.Find(ldev => ldev.Id.Equals(@params.Id.Substring(0, 8), StringComparison.InvariantCultureIgnoreCase));
                return ldev;
            }
            else
                throw new NotImplementedException();
        }
    }

    class LDEVMapRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public string Id { get; set; }
    }

    public class HPDataset : Dataset
    {
        public string ZipPath;
        public string FileEntry;

        public HPDataset(string id, string name, string description, IIdentified source, Type xType, Type yType, (object, object) xRange, params string[] variants) : base(id, name, description, source, xType, yType, xRange, variants)
        {
        }
    }

    public sealed class HPDataNode : Node
    {
        public HPDataSource Source { get; private set; }
        public HPDataset Dataset { get; private set; }
        public string Variant { get; private set; }

        private (DateTime, DateTime) SelectedRange;
        private Services.ResourceCache resourceCache;

        public HPDataNode(HPDataSource source, HPDataset set, string variant, Services.ResourceCache resCache)
            : base(set.XType, set.YType)
        {
            Source = source;
            Dataset = set;
            Variant = variant;
            resourceCache = resCache;
        }

        private List<DateTime> AvailableDates;
        public override void ApplyXRange((object, object) xRange)
        {
            SelectedRange = Helper.IntersectDateTimes(Dataset.AvailableXRange, xRange);
            AvailableDates = Source.GetAvailableDates.Where(
                    d =>
                        d >= SelectedRange.Item1.Date.AddDays(-1) &&
                        d <= SelectedRange.Item2.Date.AddDays(1)
                )
                .Where(d => File.Exists(Path.Combine(Source.GetPathFromDate(d), Dataset.ZipPath)))
                .OrderBy(d => d.Ticks).ToList();
        }

        const int MinCached = 100;
        List<(DateTime, int)> _Cached = new List<(DateTime, int)>(10_000);
        public override async Task<bool> HasNextAsync()
        {
            while (_Cached.Count < MinCached && AvailableDates.Count > 0)
                await PullToCache();

            return _Cached.Count > 0;
        }

        public override Task<(object, object)> NextAsync()
        {
            var val = PeekAsync();

            _Cached.RemoveAt(0);

            return val;
        }

        public override Task<(object, object)> PeekAsync()
        {
            return Task.FromResult<(object, object)>(_Cached[0]);
        }

        private Task PullToCache()
        {
            var csv = resourceCache.GetCSVFileFromZipAsync(Path.Combine(Source.GetPathFromDate(AvailableDates[0]), Dataset.ZipPath), Dataset.FileEntry, ',', 6);

            var colIdx = Array.IndexOf(csv.First(), $"\"{Variant}\"");

            foreach (var line in csv.Skip(1))
            {
                var date = DateTime.ParseExact(line[1].Trim('"'), HPDataSource.DateFormat, null);

                if (date < SelectedRange.Item1)
                    continue;
                if (date > SelectedRange.Item2)
                    break;

                _Cached.Add((date, int.Parse(line[colIdx].Trim('"'))));
            }

            AvailableDates.RemoveAt(0);

            return Task.CompletedTask;
        }
    }

    public class LDEVInfo
    {
        public string ECCGroup { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public float Size { get; set; }
        public string MPU { get; set; }
        public string PoolName { get; set; }

        // public IEnumerable<string> Hostnames { get { return HostPorts.Select(h => h.Hostname).Distinct(); } }
        // public IEnumerable<string> Ports { get { return HostPorts.Select(w => w.Port); } }
        // public IEnumerable<string> WwnNames { get { return Wwns.Select(w => w.WWN); } }
        // public IEnumerable<string> WwnNicknames { get { return Wwns.Select(w => w.Nickname); } }

        public HostPort[] HostPorts { get; set; }
        public WWNInfo[] Wwns { get; set; }

        public LDEVInfo(string[] csvColumns)
        {
            ECCGroup = csvColumns[0];
            Id = csvColumns[1];
            Name = csvColumns[2];
            Size = float.Parse(csvColumns[7]);
            MPU = csvColumns[15];
            PoolName = csvColumns[18];
        }
    }

    public class HostPort
    {
        public string Hostgroup { get; set; }
        public string Port { get; set; }

        public HostPort() { }
        public HostPort(string hostgroup, string port)
        {
            Hostgroup = hostgroup;
            Port = port;
        }
    }

    public class WWNInfo
    {
        public string Port { get; set; }
        public string Hostgroup { get; set; }
        public string Wwn { get; set; }
        public string Nickname { get; set; }
        public string Location { get; set; }

        public WWNInfo(string[] csvColumns)
        {
            Port = csvColumns[0];
            Hostgroup = csvColumns[1];
            Wwn = csvColumns[4];
            Nickname = csvColumns[5];
            Location = csvColumns[7];
        }
    }
}