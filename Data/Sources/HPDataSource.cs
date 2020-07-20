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
        const string DateFormat = "yyyy/mm/dd hh:MM", DirectoryDateFormat = "yyyyMMdd";
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

        public override async Task GetData(IDataWriter writer, string id, DataSelectionOptions selection, DataManipulationOptions manipulation)
        {
            var trace = _Datasets.First(d => d.Id == id);
            var range = Helper.IntersectDateTimes(trace.AvailableXRange, (selection.From, selection.To));

            var dates = GetAvailableDates.Where(d => d >= range.Item1.Date.AddDays(-1) && d <= range.Item2.Date.AddDays(1)).OrderBy(d => d.Ticks);

            
            foreach (var day in dates)
            {
                // TODO: correct date to path translation
                using (var stream = new FileStream(trace.ZipPath, FileMode.Open, FileAccess.Read))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false))
                {
                    var entry = zip.GetEntry(trace.FileEntry);

                    using (var reader = new StreamReader(entry.Open()))
                    {
                        for (int i = 0; i < 7; ++i)
                            await reader.ReadLineAsync();

                        // while ()
                        // Read lines
                        // col 2 = date x
                        // col trace.Column y
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

            await ScanZip(Path.Combine(latestDir, "LDEV_Short.zip"), _temp, availableRange);
            await ScanZip(Path.Combine(latestDir, "PhyMPU_dat.ZIP"), _temp, availableRange);
            await ScanZip(Path.Combine(latestDir, "PhyPG_dat.ZIP"), _temp, availableRange);
            await ScanZip(Path.Combine(latestDir, "PhyProc_Cache_dat.ZIP"), _temp, availableRange);
            await ScanZip(Path.Combine(latestDir, "PhyProc_dat.ZIP"), _temp, availableRange);
            await ScanZip(Path.Combine(latestDir, "Port_dat.ZIP"), _temp, availableRange);

            // Swapnout temp a ostrej
            {
                var a = _Datasets;
                _Datasets = _temp;
                _temp = a;
                _temp.Clear();
            }
        }

        async Task ScanZip(string path, IList<HPDataset> output, (DateTime, DateTime) range)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                foreach (var entry in zip.Entries)
                {
                    int column = 2;
                    foreach (var set in (await ScanCsvHeader(entry.Open())).Skip(2).Select(h => 
                        new HPDataset(
                            $"{Path.GetFileName(entry.FullName)}_{h}",
                            $"{entry.FullName}_{h}",
                            string.Empty,
                            this,
                            typeof(DateTime),
                            typeof(int),
                            range) {
                                
                            ZipPath = Path.GetRelativePath(DataPath, path), // TODO: fix this
                            FileEntry = entry.FullName,
                            Column = column++,
                        }))
                    {
                        output.Add(set);
                    }
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
        public int Column;

        public HPDataset(string id, string name, string description, IIdentified source, Type xType, Type yType, (object, object) xRange) : base(id, name, description, source, xType, yType, xRange)
        {
        }
    }
}