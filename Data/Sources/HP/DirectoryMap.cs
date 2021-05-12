using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SAApi.Data.Sources.HP
{
    public class DirectoryMap
    {
        public const string MapFileName = ".client-map.json", ConfigFileName = "config.zip";
        public static readonly string[] DetectedFilePatterns = new string[] { "LDEV_*.ZIP", "LDEV_Short.zip", "*_dat.ZIP" };

        [JsonIgnore] public string Root { get; set; } = string.Empty;
        public IEnumerable<string> PhysicalFiles { get; set; } = System.Linq.Enumerable.Empty<string>();
        public IEnumerable<string> TotalFiles { get; set; } = System.Linq.Enumerable.Empty<string>();
        public IEnumerable<string> OmittedFiles { get; set; } = System.Linq.Enumerable.Empty<string>();
        public Dictionary<string, CsvMeta> Metas { get; set; } = new Dictionary<string, CsvMeta>();

        [JsonIgnore] public string MapFile => Path.Combine(Root, MapFileName);

        [JsonIgnore] public bool HasConfigFile => OmittedFiles.Contains(ConfigFileName);
        [JsonIgnore] public string ConfigFile => Path.Combine(Root, ConfigFileName);

        private (DateTime, DateTime)? _TimeRange;
        public (DateTime From, DateTime To) TimeRange
        {
            get {
                if (!_TimeRange.HasValue)
                {
                    var a = Metas.First().Value;
                    _TimeRange = (a.From, a.To);
                }

                return _TimeRange.Value;
            }
        }

        public static DirectoryMap BuildDirectoryMap(string dir)
        {
            var mapFile = Path.Combine(dir, MapFileName);

            if (File.Exists(mapFile))
            {
                try
                {
                    using (var stream = new FileStream(mapFile, FileMode.Open, FileAccess.Read))
                    {
                        var result = JsonSerializer.DeserializeAsync<DirectoryMap>(stream).GetAwaiter().GetResult() ?? throw new Exception("");
                        result.Root = dir;
                        return result;
                    }
                }
                catch
                {
                    File.Delete(mapFile);
                }
            }

            var totalFiles = new List<string>();
            var map = new DirectoryMap() {
                Root = dir,
                PhysicalFiles = ScanKnownFiles(dir, true).ToArray(),
                TotalFiles = totalFiles,
                Metas = new Dictionary<string, CsvMeta>()
            };
            
            map.OmittedFiles = Directory.GetFiles(dir)
                .Select(f => Path.GetRelativePath(dir, f))
                .Where(f => !map.PhysicalFiles.Contains(f));

            foreach (var f in map.PhysicalFiles)
            {
                map.OpenLocalZip(f, zip => {
                    foreach (var entry in zip.Entries)
                    {
                        var fileName = $"{f}::{entry.FullName}";
                        totalFiles.Add(fileName);
                        using (var csvStream = entry.Open())
                            map.Metas.Add(fileName, CsvMeta.FromStream(csvStream));
                    }
                });
            }

            try {
                using (var stream = new FileStream(mapFile, FileMode.Create, FileAccess.Write))
                using (var writer = new Utf8JsonWriter(stream))
                    JsonSerializer.Serialize<DirectoryMap>(writer, map);
            } finally { }

            return map;
        }

        public void OpenLocalZip(string physFile, Action<ZipArchive> action, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            DirectoryMap.OpenZip(Path.Combine(Root, physFile), action, mode, access, archiveMode);
        }

        public T OpenLocalZip<T>(string physFile, Func<ZipArchive, T> func, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            return DirectoryMap.OpenZip<T>(Path.Combine(Root, physFile), func, mode, access, archiveMode);
        }

        public async Task OpenLocalZipAsync(string physFile, Func<ZipArchive, Task> action, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            await DirectoryMap.OpenZipAsync(Path.Combine(Root, physFile), action, mode, access, archiveMode);
        }

        public async Task<T> OpenLocalZip<T>(string physFile, Func<ZipArchive, Task<T>> func, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            return await DirectoryMap.OpenZipAsync<T>(Path.Combine(Root, physFile), func, mode, access, archiveMode);
        }

        public static void OpenZip(string path, Action<ZipArchive> action, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            using (var stream = new FileStream(path, mode, access))
            using (var zip = new ZipArchive(stream, archiveMode, false))
            {
                action.Invoke(zip);
            }
        }

        public static T OpenZip<T>(string path, Func<ZipArchive, T> func, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            using (var stream = new FileStream(path, mode, access))
            using (var zip = new ZipArchive(stream, archiveMode, false))
            {
                return func.Invoke(zip);
            }
        }

        public static async Task OpenZipAsync(string path, Func<ZipArchive, Task> action, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            using (var stream = new FileStream(path, mode, access))
            using (var zip = new ZipArchive(stream, archiveMode, false))
            {
                await action.Invoke(zip);
            }
        }

        public static async Task<T> OpenZipAsync<T>(string path, Func<ZipArchive, Task<T>> func, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            using (var stream = new FileStream(path, mode, access))
            using (var zip = new ZipArchive(stream, archiveMode, false))
            {
                return await func.Invoke(zip);
            }
        }
        
        public static IEnumerable<string> ScanKnownFiles(string directory, bool relativePaths = false)
        {
            var result = DetectedFilePatterns
                .SelectMany(pat => Directory.GetFiles(directory, pat, SearchOption.AllDirectories))
                .Distinct();

            if (relativePaths)
                return result.Select(p => Path.GetRelativePath(directory, p));
            else
                return result;
        }

        public static (DateTime From, DateTime To) DetermineTimeRange(string directory)
        {
            var path = ScanKnownFiles(directory, false).First();

            return OpenZip(path, zip => {
                using (var stream = zip.Entries.First().Open())
                {
                    var data = CsvMeta.FromStream(stream);
                    return (data.From, data.To);
                }
            });
        }

        public static (DateTime From, DateTime To)? TryDetermineTimeRange(string directory)
        {
            try
            {
                var path = ScanKnownFiles(directory, false).FirstOrDefault();

                if (path == null)
                    return null;

                return OpenZip(path, zip => {
                    using (var stream = zip.Entries.First().Open())
                    {
                        var data = CsvMeta.FromStream(stream);
                        return (data.From, data.To);
                    }
                });
            }
            catch
            {
                return null;
            }
        }
    }
}