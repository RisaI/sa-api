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
        public override string Type => "hp";

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
        private List<(DataRange Range, string Path)> Ranges = new List<(DataRange Range, string Path)>();
        public override async Task OnTick(IServiceScope scope)
        {
            // ? LDEVEachOfCU_dat

            Ranges = Directory.GetDirectories(DataPath, "PFM_*")
                .Select(d => {
                    try {
                        var range = DirectoryMap.DetermineTimeRange(d);
                        return (DataRange.Create(range), d) as (DataRange, string)?;
                    } catch {
                        return null;
                    }
                })
                .Where(d => d != null)
                .Select(d => ((DataRange, string))d)
                .OrderBy(r => r.Item1.From)
                .ToList();

            var availableRange = Ranges.Select(r => r.Range).BoundingBox()?.ToTuple<DateTime>() ?? throw new Exception("Unexpected range type");

            var maps = Ranges.Select(r => r.Path).Select(d => DirectoryMap.BuildDirectoryMap(d)).ToArray();

            foreach (var map in maps)
            {
                Ranges.Add((DataRange.Create(map.TimeRange), map.Root));

                foreach (var zip in map.PhysicalFiles)
                {
                    if (zip.StartsWith("LDEVEachOfCU_dat"))
                        continue;

                    var category = Path.GetDirectoryName(zip) switch {
                        string path when !string.IsNullOrWhiteSpace(path) => path.Split(),
                        _ => new string[] { Path.GetFileNameWithoutExtension(zip) }
                    };

                    map.OpenLocalZip(zip, archive => {
                        foreach (var entry in archive.Entries) {
                            var range = map.TimeRange;
                            var variants = map.Metas[$"{zip}::{entry.FullName}"].Headers.SelectMany(h => h.VariantsSpan.ToArray());
                            var id = Path.GetFileNameWithoutExtension(entry.FullName);

                            var prev = _temp.FirstOrDefault(ds => ds.Id == id);

                            var units = HPDataset.UnitTable.FirstOrDefault(u => id.Contains(u.Item1, StringComparison.InvariantCultureIgnoreCase));

                            if (prev == null) {
                                lock (_temp) {
                                    _temp.Add(new HPDataset(
                                        id,
                                        category,
                                        units.Item2 ?? "s^-1",
                                        this,
                                        typeof(DateTime),
                                        typeof(int),
                                        map.TimeRange,
                                        variants.ToArray()
                                    ) { ZipPath = zip, FileEntry = entry.FullName });
                                }
                            } else {
                                prev.DataRange = prev.DataRange.Append(DataRange.Create(range));
                                prev.Variants = prev.Variants.Concat(variants).Distinct().ToArray();
                            }
                        }
                    });
                }
            }

            _temp.ForEach(t => t.DataRange = DataRange.Simplify(t.DataRange));

            {
                var recentDate = maps.Max(m => m.TimeRange.To);
                var latestPath = maps.First(m => m.TimeRange.To == recentDate).Root;

                var configs = new string[] {
                    Path.Combine(latestPath, "config.zip"),
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
                var ids = string.IsNullOrWhiteSpace(@params.Id) ? @params.Ids : new string[] { @params.Id };

                switch (@params.Mode)
                {
                    case "port":
                        return null;
                    case "mpu":
                        return null;
                    case "pool":
                        return null;
                    case "wwn":
                        return null;
                    case "ldev":
                    default:
                        return @params.Ids
                            .Select(i =>
                                LDEVs.FirstOrDefault(l => 
                                    l.Id.Equals(i.Substring(0, Math.Min(i.Length, 8)), StringComparison.InvariantCultureIgnoreCase)
                                )
                            )
                            .Where(l => l != null);
                }

                
            }
            else
                throw new NotImplementedException();
        }

        public override async Task GetBulkData(string id, IEnumerable<string> variant, DataRange range, Stream stream)
        {
            var dataset = this.Datasets.First(d => d.Id == id) as HPDataset;
            var bound = DataRange.BoundingBox(dataset.DataRange);

            if (!bound.Contains(range)) return;

            int i = 0;
            var variantMap = variant.ToDictionary(v => v, v => ++i);

            var cursor = (DateTime)range.From;
            var end    = (DateTime)range.To;

            var buffer = new byte[(variant.Count() + 1) * sizeof(int)];

            void SerializeInt (int data,      int idx) => BitConverter.TryWriteBytes(buffer.AsSpan(idx, sizeof(int)), data);
            void SerializeDate(DateTime date, int idx) => SerializeInt(date.ToMinuteRepre(), idx);
            
            foreach (var dir in Ranges.Where(r => r.Range.Intersection(range) != null))
            {
                var zipPath = Path.Combine(dir.Path, dataset.ZipPath);

                using var zipStream = File.Open(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
                using var csvReader = new CsvReader(zip.GetEntry(dataset.FileEntry).Open(), false);

                while (cursor < csvReader.From)
                {
                    SerializeDate(cursor, 0);

                    for (i = 1; i <= variant.Count(); ++i)
                        SerializeInt(-4, i * sizeof(int));

                    cursor = cursor.AddMinutes(1);
                }

                var query = csvReader.ReadNextBlock()
                    .SkipWhile(row => row.Time < cursor)
                    .TakeWhile(row => row.Time <= end);

                foreach (var row in query)
                {
                    // Clear da buffa!
                    for (int j = 0; j < buffer.Length / sizeof(int); ++j)
                        SerializeInt(-1, j * sizeof(int));

                    cursor = row.Time;
                    SerializeDate(cursor, 0);
                    
                    var cols = row.Values;
                    for (i = 0; i < cols.Length; ++i)
                    {
                        var col = cols.Span[i];
                        SerializeInt(col.Data, variantMap[col.Variant] * sizeof(int));
                    }

                    await stream.WriteAsync(buffer);
                }
            }
        }
    }

    record LDEVMapRequest
    {
        [JsonPropertyName("id")] public string Id { get; init; }
        [JsonPropertyName("ids")] public string[] Ids { get; init; }

        [JsonPropertyName("mode")] public string Mode { get; init; }
    }

    public class HPDataset : Dataset
    {
        public string ZipPath { get; init; }
        public string FileEntry { get; init; }
        public bool IsLdevData { get; init; }

        public HPDataset(
            
            string id,
            string[] category,
            string units,
            IIdentified source,
            Type xType,
            Type yType,
            (DateTime From, DateTime To) xRange,
            params string[] variants
            
            ) : base(id, category, units, source, xType, yType, new [] { Data.DataRange.Create(xRange) }, variants)
        {
        }

        public static readonly (string, string)[] UnitTable = new [] {
            ("PG_C2D_Trans", "DTO"),
            ("PG_D2CS_Trans", "DTO"),
            ("PG_D2CR_Trans", "DTO"),
            ("LDEV_BackTrans", "DTO"),
            ("LDEV_C2D_Trans", "DTO"),
            ("LDEV_D2CS_Trans", "DTO"),
            ("LDEV_D2CR_Trans", "DTO"),
            ("PHY_ExG_Read_Response", "ms"),
            ("PHY_ExG_Read_Response", "ms"),
            ("PHY_ExG_Response", "ms"),
            ("PHY_ExLDEV_Read_Response", "ms"),
            ("PHY_ExLDEV_Response", "ms"),
            ("PHY_ExLDEV_Write_Response", "ms"),
            ("_Update_Copy_Response", "ms"),
            ("_Initial_Copy_Response", "ms"),
            ("_Pair_Synchronized", "percent"),
            ("_Update_Copy_RIO", "IOP/s"),
            ("PhyMPPK", "percent"),
            ("iops", "IOP/s"),
            ("mb/s", "MB/s"),
            ("mb", "MB"),
            ("kbps", "kB/s"),
            ("kb", "kB"),
            ("TRANS", "kB/s"),
            ("MICROSEC.", "us"),
            ("COUNT", "1"),
            ("(percent)", "percent"),
            ("EXG_RESPONSE", "ms"),
            ("RESPONSE", "us"),
            ("HIT", "percent"),
            ("_RATE", "percent"),
            ("PHY_SHORT_", "percent"),
            ("PHY_LONG_", "percent"),
            ("PHY_Cache_Allocate", "MB"),
            ("PHY_MP", "percent"),
            ("PHY_PG", "percent"),
            ("_BlockSize", "kB"),
            ("timeseriesutilpercentagerx", "percent"),
            ("timeseriesutilpercentagetx", "percent"),
            ("timeseriestrafficrx", "MB/s"),
            ("timeseriestraffictx", "MB/s"),
        };
    }
}