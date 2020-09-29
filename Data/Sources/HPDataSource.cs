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

        string DataPath { get { return _Config["path"]; } }

        public HPDataSource(string id, IConfigurationSection config)
            : base(id, "Disková pole HP", config)
        {

        }

        private List<HPDataset> _Datasets = new List<HPDataset>();
        public override IEnumerable<Dataset> Datasets => _Datasets;

        public IEnumerable<DateTime> GetAvailableDates { get { return Directory.GetDirectories(DataPath).Select(d => DateTime.ParseExact(Path.GetFileName(d).Substring(4), DirectoryDateFormat, null)); } }
        public string GetPathFromDate(DateTime date) => Path.Combine(DataPath, $"PFM_{date.ToString(DirectoryDateFormat)}");

        public override Task<Node> GetNode(string id, string variant, Services.ResourceCache resCache)
        {
            var dataset = _Datasets.Find(d => d.Id == id);
            return Task.FromResult<Node>(new HPDataNode(this, dataset, variant, resCache));
        }

        private List<HPDataset> _temp = new List<HPDataset>();
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

            await ScanZip(latestDir, "LDEV_Short.zip", _temp, availableRange);
            await ScanZip(latestDir, "PhyMPU_dat.ZIP", _temp, availableRange);
            await ScanZip(latestDir, "PhyPG_dat.ZIP", _temp, availableRange);
            await ScanZip(latestDir, "PhyProc_Cache_dat.ZIP", _temp, availableRange);
            await ScanZip(latestDir, "PhyProc_dat.ZIP", _temp, availableRange);
            await ScanZip(latestDir, "Port_dat.ZIP", _temp, availableRange);

            {
                var dict = new Dictionary<string, List<string>>();
                foreach (var date in dates)
                {
                    var path = GetPathFromDate(date);

                    await ScanZipVariants(path, "LDEV_Short.zip", dict);
                    await ScanZipVariants(path, "PhyMPU_dat.ZIP", dict);
                    await ScanZipVariants(path, "PhyPG_dat.ZIP", dict);
                    await ScanZipVariants(path, "PhyProc_Cache_dat.ZIP", dict);
                    await ScanZipVariants(path, "PhyProc_dat.ZIP", dict);
                    await ScanZipVariants(path, "Port_dat.ZIP", dict);
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
                var ldevs = new List<LDEVInfo>();
                using (var csvFile = zip.GetEntry("LdevInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync(); // Skip first two lines

                    while (!reader.EndOfStream)
                        ldevs.Add(new LDEVInfo((await reader.ReadLineAsync()).Split(',')));
                }

                var ldevHostsMap = ldevs.ToDictionary(k => k.Id, v => new List<(string, string)>());

                using (var csvFile = zip.GetEntry("LunInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync();
                    
                    while (!reader.EndOfStream)
                    {
                        var cols = (await reader.ReadLineAsync()).Split(',');
                        if (string.IsNullOrWhiteSpace(cols[5]) || !ldevHostsMap.ContainsKey(cols[5]))
                            continue;
                        
                        var ldev = ldevHostsMap[cols[5]];
                        ldev.Add((cols[0], cols[1]));
                    }

                    foreach (var ldev in ldevs)
                        ldev.HostNicknames = ldevHostsMap[ldev.Id].Select(v => v.Item2).Distinct().ToArray();
                }

                using (var csvFile = zip.GetEntry("WwnInfo.csv").Open())
                using (var reader = new StreamReader(csvFile))
                {
                    await reader.ReadLineAsync();
                    await reader.ReadLineAsync();

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
            var val = _Cached[0];

            _Cached.RemoveAt(0);

            return Task.FromResult<(object, object)>(val);
        }

        private async Task PullToCache()
        {
            var csv = await resourceCache.GetCSVFileFromZipAsync(Path.Combine(Source.GetPathFromDate(AvailableDates[0]), Dataset.ZipPath), Dataset.FileEntry, ',', 6);

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

        public string[] Ports { get; set; }
        public string[] WWNs { get; set; }
        public string[] HostNicknames { get; set; }

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

    public class WWNInfo
    {
        public string Port { get; set; }
        public string HostGroup { get; set; }
        public string WWN { get; set; }
        public string Nickname { get; set; }
        public string Location { get; set; }

        public WWNInfo(string[] csvColumns)
        {
            Port = csvColumns[0];
            HostGroup = csvColumns[1];
            WWN = csvColumns[4];
            Nickname = csvColumns[5];
            Location = csvColumns[7];
        }
    }
}