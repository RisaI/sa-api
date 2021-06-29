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
        public const string DateFormat = "yyyy/MM/dd HH:mm", DirectoryDateFormat = "yyyyMMdd";
        public static readonly string[] DetectedPatterns = new string[] { "LDEV_*.zip", "Phy*_dat.ZIP", "Port_*.ZIP" };
        public static readonly string[] MPPKZips = new string[] { "PhyProcDetail_dat.ZIP", "PhyMPPK_dat.ZIP" };
        public static readonly IDictionary<string, string> CategoryMap = new Dictionary<string, string>() {
            { "PhyCMPK_dat", "PhyProc_Cache_dat" },
            { "PhyMPU_dat", "PhyProc_Cache_dat" },
        };

        string DataPath { get { return _Config["path"]; } }
        string TimeZone { get { return _Config["tz"] ?? "UTC"; } }
        public override string Type => "hp";

        public HPDataSource(string id, IConfigurationSection config)
            : base(id, "Disková pole HP", config)
        {
            Metadata.Add("path", DataPath);
            Metadata.Add("tz", TimeZone);

            // Register features
            RegisterFeature<LDEVMapRequest, IEnumerable<LDEVInfo>>(DataSource.FeatureNames.LDEVMap, LdevMapFeature, () => LDEVs.Count > 0);
            RegisterFeature<VariantRecommendRequest, IEnumerable<string>>(DataSource.FeatureNames.VariantRecommend, RecommendVariants, null);
        }

        private List<HPDataset> _Datasets = new List<HPDataset>();
        public override IEnumerable<Dataset> Datasets => _Datasets;
        private List<DataRange> _DataRanges = new List<DataRange>();
        public override IEnumerable<DataRange> Dataranges => _DataRanges;

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

            Metadata["path"] = DataPath;

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
            _DataRanges = DataRange.Simplify(Ranges.Select(r => r.Range)).ToList();

            var maps = Ranges.Select(r => r.Path).Select(d => DirectoryMap.BuildDirectoryMap(d)).ToArray();

            foreach (var map in maps)
            {
                Ranges.Add((DataRange.Create(map.TimeRange), map.Root));

                foreach (var zip in map.ZipFiles)
                {
                    if (zip.StartsWith("LDEVEachOfCU_dat"))
                        continue;

                    if (Array.IndexOf(MPPKZips, zip) >= 0)
                    {
                        map.OpenLocalZip(zip, archive => {
                            foreach (var entry in archive.Entries) {
                                var range = map.TimeRange;
                                var file = Path.GetFileNameWithoutExtension(entry.FullName);

                                foreach (var grouping in Enum.GetValues<HP_MPPKDataset.MPPKGrouping>())
                                {
                                    var id = HP_MPPKDataset.CreateId(file, grouping);
                                    var prev = _temp.FirstOrDefault(ds => ds.Id == id);

                                    if (prev == null) {
                                        lock (_temp) {
                                            _temp.Add(new HP_MPPKDataset(
                                                id,
                                                new [] { Path.GetFileNameWithoutExtension(zip), file },
                                                this,
                                                grouping,
                                                map.TimeRange,
                                                HP_MPPKDataset.DefaultVariants(grouping)
                                            ) { ZipPath = zip, FileEntry = entry.FullName });
                                        }
                                    } else {
                                        prev.DataRange.Add(DataRange.Create(range));
                                        // TODO: add variants for ById
                                        // foreach (var v in variants)
                                        //     prev.Variants.Add(v);
                                    }
                                }
                            }
                        });

                        continue;
                    }

                    var category = (Path.GetDirectoryName(zip) switch {
                        string path when !string.IsNullOrWhiteSpace(path) => path.Split('/', '\\'),
                        _ => new string[] { Path.GetFileNameWithoutExtension(zip) }
                    }).Select(s => CategoryMap.ContainsKey(s) ? CategoryMap[s] : s).ToArray();

                    map.OpenLocalZip(zip, archive => {
                        foreach (var entry in archive.Entries) {
                            var range = map.TimeRange;
                            var variants = map.Zips[zip][entry.FullName].Headers.SelectMany(h => h.VariantsSpan.ToArray());
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
                                        variants
                                    ) { ZipPath = zip, FileEntry = entry.FullName });
                                }
                            } else {
                                prev.DataRange.Add(DataRange.Create(range));
                                foreach (var v in variants)
                                    prev.Variants.Add(v);
                            }
                        }
                    });
                }
            }

            _temp.ForEach(t => t.DataRange = DataRange.Simplify(t.DataRange).ToList());

            {
                var globalConf = Path.Combine(DataPath, "config.zip");
                var availableConf = maps.Select(m => m.ConfigFile).TakeLast(1).Prepend(globalConf).Where(f => File.Exists(f));

                if (availableConf.Any())
                    LDEVs = await LoadConfig(availableConf);
            }

            // Swapnout temp a ostrej
            {
                var a = _Datasets;
                _Datasets = _temp;
                _temp = a;
                _temp.Clear();
            }
        }

        static async Task<List<LDEVInfo>> LoadConfig(IEnumerable<string> paths)
        {
            Dictionary<string, LDEVInfo> ldevs = new();
            Dictionary<int, Pool> pools = new();
            List<LDEVInfo> currentLdevs = new();

            // Load configuration
            foreach (var path in paths)
            {
                currentLdevs.Clear();

                using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var zip = new ZipArchive(file, ZipArchiveMode.Read);
                
                using (var csvFile = zip.GetEntry("LdevInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync(); // Skip first two lines

                    while (!reader.EndOfStream)
                    {
                        var row = (await reader.ReadLineAsync()).Split(',');
                        var ldev = new LDEVInfo(row);
                        var (_poolId, poolName) = LDEVInfo.GetPoolInfo(row);

                        if (_poolId != null)
                        {
                            var poolId = _poolId.Value;

                            if (!pools.ContainsKey(poolId))
                                pools.Add(poolId, new Pool(poolId, poolName));

                            ldev.Pool = pools[poolId];

                            if (ldev.ECCGroup != "-" && !ldev.Pool.EccGroups.Contains(ldev.ECCGroup))
                                ldev.Pool.EccGroups.Add(ldev.ECCGroup);
                        }

                        currentLdevs.Add(ldev);

                        if (ldevs.ContainsKey(ldev.Id))
                            ldevs[ldev.Id] = ldev;
                        else
                            ldevs.Add(ldev.Id, ldev);
                    }
                }

                var hostWWNs = new Dictionary<string, List<WWNInfo>>();
                using (var csvFile = zip.GetEntry("LunInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync();

                    while (!reader.EndOfStream)
                    {
                        var cols = (await reader.ReadLineAsync()).Split(',');
                        if (string.IsNullOrWhiteSpace(cols[5]) || !ldevs.ContainsKey(cols[5]))
                            continue;

                        var ldev = ldevs[cols[5]];
                        var host = new HostPort(cols[1], cols[0]);
                        var hostAlias = $"{host.Hostgroup}:{host.Port}";

                        if (!hostWWNs.ContainsKey(hostAlias))
                            hostWWNs.Add(hostAlias, new List<WWNInfo>());

                        ldev.HostPorts.Add(host);
                    }
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

                foreach (var ldev in currentLdevs)
                    ldev.Wwns = ldev.HostPorts.SelectMany(hp => hostWWNs[$"{hp.Hostgroup}:{hp.Port}"]).ToList();
            }

            return ldevs.Values.ToList();
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

        Task<IEnumerable<LDEVInfo>> LdevMapFeature(LDEVMapRequest @params, Microsoft.AspNetCore.Http.HttpRequest request)
        {
            var ids = @params.Ids.ToHashSet();

            return Task.FromResult(
                LDEVs.Where(@params.Mode switch {
                    "port"      => l => l.HostPorts.Any(h => ids.Contains(h.Port)),
                    "mpu"       => l => ids.Contains(l.MPU),
                    "ecc"       => l => l.Pool != null && l.Pool.EccGroups.Any(e => ids.Contains(e)),
                    "pool"      => l => l.Pool != null && ids.Contains(l.Pool.Name),
                    "wwn"       => l => l.Wwns.Any(w => ids.Contains(w.Wwn)),
                    "hostgroup" => l => l.Wwns.Any(w => ids.Contains(w.Hostgroup)),
                    "ldev" or _ => l => ids.Contains(l.Id)
                })
            );
        }

        Task<IEnumerable<string>> RecommendVariants(VariantRecommendRequest @params, Microsoft.AspNetCore.Http.HttpRequest request)
        {
            HPDataset dataset = this._Datasets.FirstOrDefault(d => d.Id == @params.Id);
            var mppk = dataset as HP_MPPKDataset;

            DataRange range = Data.DataRange.Create(Helper.ParseRange(
                dataset.XType,
                @params.From,
                @params.To
            ));

            var bound = DataRange.BoundingBox(dataset.DataRange);

            if (!bound.Contains(range)) return Task.FromResult(Enumerable.Empty<string>());

            if (mppk != null && mppk.Grouping != HP_MPPKDataset.MPPKGrouping.ById)
                return Task.FromResult(mppk.Variants.AsEnumerable());

            var hashset = new HashSet<string>();

            foreach (var dir in Ranges.Where(r => r.Range.Intersection(range) != null))
            {
                var map = DirectoryMap.BuildDirectoryMap(dir.Path);
                
                if (!map.Zips.ContainsKey(dataset.ZipPath) || !map.Zips[dataset.ZipPath].ContainsKey(dataset.FileEntry))
                    continue;

                if (mppk != null)
                {
                    map.OpenLocalZip(dataset.ZipPath, zip => {
                        using var reader = new CsvReader(zip.GetEntry(dataset.FileEntry).Open(), false);

                        foreach (var row in reader.ReadNextBlockAsString()) {
                            foreach (var col in row.Values.Span) {
                                var split = col.Data.Split(';');

                                if (split.Length <= 1) break;

                                hashset.Add(split[2]); 
                            }
                        }
                    });
                    continue;
                }

                var meta = map.Zips[dataset.ZipPath][dataset.FileEntry];

                foreach (var header in meta.Headers)
                    foreach (var variant in header.VariantsSpan)
                        if (!hashset.Contains(variant))
                            hashset.Add(variant);
            }

            return Task.FromResult(hashset.AsEnumerable());
        }

        public override async Task GetBulkData(string id, IEnumerable<string> variant, DataRange range, Stream stream)
        {
            var dataset = _Datasets.First(d => d.Id == id) as HPDataset;
            var bound = DataRange.BoundingBox(dataset.DataRange);

            if (!bound.Contains(range)) return;

            int i = 0;
            var variantMap = variant.ToDictionary(v => v, v => ++i);

            var cursor = (DateTime)range.From;
            var end    = (DateTime)range.To;

            var buffer = new byte[(variant.Count() + 1) * sizeof(int)];

            void SerializeInt (int data,      int idx) => BitConverter.TryWriteBytes(buffer.AsSpan(idx, sizeof(int)), data);
            void SerializeDate(DateTime date, int idx) => SerializeInt(date.ToMinuteRepre(), idx);
            void ClearBuffer(int clearValue = -1) {
                for (int j = 0; j < buffer.Length / sizeof(int); ++j)
                    SerializeInt(clearValue, j * sizeof(int));
            };
            
            foreach (var dir in Ranges.Where(r => r.Range.Intersection(range) != null))
            {
                var zipPath = Path.Combine(dir.Path, dataset.ZipPath);

                if (!File.Exists(zipPath)) continue;

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

                if (dataset is HP_MPPKDataset mppk)
                {
                    var query = csvReader.ReadNextBlockAsString()
                        .SkipWhile(row => row.Time < cursor)
                        .TakeWhile(row => row.Time <= end);

                    var tally = new int[variant.Count()];

                    foreach (var row in query)
                    {
                        ClearBuffer(0);
                        for (int j = 0; j < tally.Length; ++j) tally[j] = 0;
                        cursor = row.Time;
                        SerializeDate(cursor, 0);

                        var cols = row.Values;

                        for (i = 0; i < cols.Length; ++i)
                        {
                            var col = cols.Span[i];
                            var split = col.Data.Split(';');

                            if (split.Length <= 1) continue; // Skip undefined

                            var idx = mppk.Grouping switch {
                                HP_MPPKDataset.MPPKGrouping.ByKernel => (variantMap[split[0]] - 1),
                                HP_MPPKDataset.MPPKGrouping.ByType   => (variantMap[split[1]] - 1),
                                HP_MPPKDataset.MPPKGrouping.ById     => variantMap.ContainsKey(split[2]) ? (variantMap[split[2]] - 1) : -1,
                                _ => -1
                            };

                            if (idx >= 0)
                                tally[idx] += int.Parse(split.Last());
                        }

                        for (int j = 0; j < tally.Length; ++j)
                            SerializeInt(tally[j], (j + 1) * sizeof(int));

                        await stream.WriteAsync(buffer);
                    }
                }
                else
                {
                    var query = csvReader.ReadNextBlock()
                        .SkipWhile(row => row.Time < cursor)
                        .TakeWhile(row => row.Time <= end);

                    foreach (var row in query)
                    {
                        ClearBuffer();
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
    }

    record LDEVMapRequest
    {
        [JsonPropertyName("ids")] public string[] Ids { get; init; }
        [JsonPropertyName("mode")] public string Mode { get; init; }
    }

    record VariantRecommendRequest
    {
        [JsonPropertyName("id")] public string Id { get; init; }
        [JsonPropertyName("from")] public string From { get; init; }
        [JsonPropertyName("to")] public string To { get; init; }
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
            IEnumerable<string> variants
            
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
            ("_Update_Copy_RIO", "IO/s"),
            ("PhyMPPK", "percent"),
            ("iops", "IO/s"),
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

    public class HP_MPPKDataset : HPDataset
    {
        public MPPKGrouping Grouping { get; set; }

        public HP_MPPKDataset(
            string id,
            string[] category,
            IIdentified source,
            MPPKGrouping grouping,
            (DateTime From, DateTime To) xRange,
            IEnumerable<string> variants
        ) : base(id, category, "percent", source, typeof(DateTime), typeof(int), xRange, variants)
        {
            Grouping = grouping;
        }

        public static string CreateId(string file, MPPKGrouping grouping)
        {
            var suffix = grouping switch {
                MPPKGrouping.ByKernel => "byKernel",
                MPPKGrouping.ByType => "byType",
                MPPKGrouping.ById => "byId",
                _ => "unknown",
            };

            return $"{file}-{suffix}";
        }

        public static string[] DefaultVariants(MPPKGrouping grouping)
        {
            return grouping switch {
                MPPKGrouping.ByKernel => new[] { "Open-Target", "Open-External", "Open-Initiator", "BackEnd", "System" },
                MPPKGrouping.ByType => new[] { "LDEV", "ExG", "JNLG" },
                _ => new[] { "default" },
            }; 
        }

        public enum MPPKGrouping
        {
            ById,
            ByKernel,
            ByType,
        }
    }
}