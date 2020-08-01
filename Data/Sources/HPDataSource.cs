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
        const string DateFormat = "yyyy/MM/dd HH:mm", DirectoryDateFormat = "yyyyMMdd";
        private IConfiguration _Config;

        string DataPath { get { return _Config["path"]; } }

        public HPDataSource(IConfigurationSection config)
        {
            _Config = config;
        }

        private List<HPDataset> _Datasets = new List<HPDataset>();
        public override IEnumerable<Dataset> Datasets => _Datasets;

        public override string Id { get { return "hp"; } }
        public override string Name { get { return "Disková pole HP"; } }

        public IEnumerable<DateTime> GetAvailableDates { get { return Directory.GetDirectories(DataPath).Select(d => DateTime.ParseExact(Path.GetFileName(d).Substring(4), DirectoryDateFormat, null)); } }
        public string GetPathFromDate(DateTime date) => Path.Combine(DataPath, $"PFM_{date.ToString(DirectoryDateFormat)}");

        public override async Task GetData(IDataWriter writer, string id, string variant, DataSelectionOptions selection, DataManipulationOptions manipulation)
        {
            writer.IsCompatible(typeof(DateTime), typeof(int));

            var trace = _Datasets.First(d => d.Id == id);
            var range = Helper.IntersectDateTimes(trace.AvailableXRange, (selection.From, selection.To));

            var dates = GetAvailableDates.Where(d => d >= range.Item1.Date.AddDays(-1) && d <= range.Item2.Date.AddDays(1)).OrderBy(d => d.Ticks);

            foreach (var day in dates)
            {
                using (var stream = new FileStream(Path.Combine(GetPathFromDate(day), trace.ZipPath), FileMode.Open, FileAccess.Read))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false))
                {
                    var entry = zip.GetEntry(trace.FileEntry);

                    using (var fStream = entry.Open())
                    {
                        await ReadColumnData(fStream, writer, variant, range.Item1, range.Item2);
                    }
                }
            }
        }

        private List<HPDataset> _temp = new List<HPDataset>();
        public override async Task OnTick(IServiceScope scope)
        {
            var dirs = Directory.GetDirectories(DataPath);
            var dates = dirs.Select(d => DateTime.ParseExact(Path.GetFileName(d).Substring(4), DirectoryDateFormat, null));
            var availableRange = (dates.Min(), dates.Max());

            // ? config.zip
            // ? capacity.cfg
            // ? LDEVEachOfCU_dat

            var latestDir = Path.Combine(DataPath, $"PFM_{availableRange.Item2.ToString(DirectoryDateFormat)}");

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

        async Task ReadColumnData(Stream stream, IDataWriter output, string column, DateTime from, DateTime to)
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

                    await output.Write(DateTime.ParseExact(cols[1], DateFormat, null), int.Parse(cols[colIdx]));
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
}