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

        public override Task<Node> GetNode(string id, string variant)
        {
            var dataset = _Datasets.Find(d => d.Id == id);
            return Task.FromResult<Node>(new HPDataNode(this, dataset, variant));
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

            var dict = new Dictionary<string, List<string>>();
            foreach (var date in dates)
            {
                var path = Path.Combine(DataPath, $"PFM_{date.ToString(DirectoryDateFormat)}");

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

        public HPDataNode(HPDataSource source, HPDataset set, string variant)
            : base(set.XType, set.YType)
        {
            Source = source;
            Dataset = set;
            Variant = variant;
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
            using (var stream = new FileStream(Path.Combine(Source.GetPathFromDate(AvailableDates[0]), Dataset.ZipPath), FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                var entry = zip.GetEntry(Dataset.FileEntry);

                using (var fStream = entry.Open())
                {
                    await ReadColumnData(fStream, Variant, SelectedRange.Item1, SelectedRange.Item2);
                }
            }

            AvailableDates.RemoveAt(0);
        }

        async Task ReadColumnData(Stream stream, string column, DateTime from, DateTime to)
        {
            using (var reader = new StreamReader(stream))
            {
                for (int i = 0; i < 6; ++i)
                    await reader.ReadLineAsync();

                var colIdx = Array.IndexOf((await reader.ReadLineAsync()).Split(',').Select(a => a.Trim('"')).ToArray(), column);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    var cols = line.Split(',').Select(a => a.Trim('"')).ToArray();

                    var date = DateTime.ParseExact(cols[1], HPDataSource.DateFormat, null);

                    if (date < from)
                        continue;
                    if (date > to)
                        break;

                    _Cached.Add((date, int.Parse(cols[colIdx])));
                }
            }
        }
    }
}