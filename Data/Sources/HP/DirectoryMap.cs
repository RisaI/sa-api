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
    public class DirectoryMapOld
    {
        public const string MapFileName = ".client-map.json", ConfigFileName = "config.zip";
        public static readonly string[] DetectedFilePatterns = new string[] { "LDEV_*.ZIP", "LDEV_Short.zip", "*_dat.ZIP" };

        [JsonIgnore] public string Root { get; set; } = string.Empty;
        [JsonIgnore] public IEnumerable<string> ZipFiles { get { return Zips.Keys; } }
        public IEnumerable<string> OmittedFiles { get; set; } = System.Linq.Enumerable.Empty<string>();

        public Dictionary<string, Dictionary<string, CsvMeta>> Zips { get; set; } = new ();

        [JsonIgnore] public string MapFile => Path.Combine(Root, MapFileName);
        [JsonIgnore] public bool HasConfigFile => OmittedFiles.Contains(ConfigFileName);
        [JsonIgnore] public string ConfigFile => Path.Combine(Root, ConfigFileName);

        private (DateTime, DateTime)? _TimeRange;
        public (DateTime From, DateTime To) TimeRange
        {
            get {
                if (!_TimeRange.HasValue)
                {
                    var a = Zips.First().Value.First().Value;
                    _TimeRange = (a.From, a.To);
                }

                return _TimeRange.Value;
            }
        }

        public static DirectoryMapOld BuildDirectoryMap(string dir)
        {
            var mapFile = Path.Combine(dir, MapFileName);

            if (File.Exists(mapFile))
            {
                try
                {
                    using (var stream = new FileStream(mapFile, FileMode.Open, FileAccess.Read))
                    {
                        var result = JsonSerializer.DeserializeAsync<DirectoryMapOld>(stream).GetAwaiter().GetResult() ?? throw new Exception("");
                        result.Root = dir;
                        return result;
                    }
                }
                catch
                {
                    File.Delete(mapFile);
                }
            }

            var map = new DirectoryMapOld() {
                Root = dir,
            };

            var zips = ScanKnownFiles(dir, true).ToArray();
            
            map.OmittedFiles = Directory.GetFiles(dir)
                .Select(f => Path.GetRelativePath(dir, f))
                .Where(f => !zips.Contains(f));

            foreach (var f in zips)
            {
                var metas = new Dictionary<string, CsvMeta>();

                map.Zips.Add(f, metas);

                map.OpenLocalZip(f, zip => {
                    foreach (var entry in zip.Entries)
                    {
                        using (var csvStream = entry.Open())
                            metas.Add(entry.FullName, CsvMeta.FromStream(csvStream));
                    }
                });
            }

            try {
                using (var stream = new FileStream(mapFile, FileMode.Create, FileAccess.Write))
                using (var writer = new Utf8JsonWriter(stream))
                    JsonSerializer.Serialize<DirectoryMapOld>(writer, map);
            } finally { }

            return map;
        }

        public void OpenLocalZip(string physFile, Action<ZipArchive> action, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            DirectoryMapOld.OpenZip(Path.Combine(Root, physFile), action, mode, access, archiveMode);
        }

        public T OpenLocalZip<T>(string physFile, Func<ZipArchive, T> func, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            return DirectoryMapOld.OpenZip<T>(Path.Combine(Root, physFile), func, mode, access, archiveMode);
        }

        public async Task OpenLocalZipAsync(string physFile, Func<ZipArchive, Task> action, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            await DirectoryMapOld.OpenZipAsync(Path.Combine(Root, physFile), action, mode, access, archiveMode);
        }

        public async Task<T> OpenLocalZip<T>(string physFile, Func<ZipArchive, Task<T>> func, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, ZipArchiveMode archiveMode = ZipArchiveMode.Read)
        {
            return await DirectoryMapOld.OpenZipAsync<T>(Path.Combine(Root, physFile), func, mode, access, archiveMode);
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

    public class DirectoryMap
    {
        public const string MapFileName = ".client-map.bin", ConfigFileName = "config.zip";
        public static readonly string[] DetectedFilePatterns = new string[] { "LDEV_*.ZIP", "LDEV_Short.zip", "*_dat.ZIP" };

        public string[] OmittedFiles  { get; private set; } = new string[0];
        public (DateTime From, DateTime To) TimeRange { get; private set; }
        public Dictionary<string, Dictionary<string, CsvMeta>> Zips { get; private set; } = new ();

        [JsonIgnore] public string Root { get; init; }
        [JsonIgnore] public string MapFile => Path.Combine(Root, MapFileName);
        [JsonIgnore] public bool HasConfigFile => OmittedFiles.Contains(ConfigFileName);
        [JsonIgnore] public string ConfigFile => Path.Combine(Root, ConfigFileName);
        [JsonIgnore] public IEnumerable<string> ZipFiles { get { return Zips.Keys; } }


        private DirectoryMap(string root)
        {
            Root = root;
        }

        public static DirectoryMap BuildDirectoryMap(string dir)
        {
            var mapFile = Path.Combine(dir, MapFileName);

            if (File.Exists(mapFile))
            {
                try
                {
                    using var stream = new FileStream(mapFile, FileMode.Open, FileAccess.Read);
                    using var reader = new BinaryReader(stream);

                    var ret = new DirectoryMap(dir);

                    ret.Deserialize(reader);

                    return ret;
                }
                catch
                {
                    File.Delete(mapFile);
                }
            }

            var map = new DirectoryMap(dir) { Zips = new () };

            var zips = ScanKnownFiles(dir, true).ToArray();
            
            map.OmittedFiles = Directory.GetFiles(dir)
                .Select(f => Path.GetRelativePath(dir, f))
                .Where(f => !zips.Contains(f))
                .ToArray();

            foreach (var f in zips)
            {
                var metas = new Dictionary<string, CsvMeta>();
                map.Zips.Add(f, metas);

                map.OpenLocalZip(f, zip => {
                    foreach (var entry in zip.Entries)
                    {
                        using var csvStream = entry.Open();
                        metas.Add(entry.FullName, CsvMeta.FromStream(csvStream));
                    }
                });
            }

            if (map.Zips.Any(z => z.Value.Count > 0))
            {
                var meta = map.Zips.First(z => z.Value.Count > 0).Value.First().Value;
                map.TimeRange = (meta.From, meta.To);
            }
            else
            {
                var now = DateTime.UtcNow;
                map.TimeRange = (now, now);
            }

            try {
                using var stream = new FileStream(mapFile, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(stream);

                map.Serialize(writer);
            } finally { }

            return map;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(TimeRange.From.Ticks);
            writer.Write(TimeRange.To.Ticks);

            writer.Write7BitEncodedInt(OmittedFiles.Length);
            foreach (var file in OmittedFiles) writer.Write(file);

            writer.Write7BitEncodedInt(Zips.Count);
            foreach (var zip in Zips)
            {
                writer.Write(zip.Key);
                writer.Write7BitEncodedInt(zip.Value.Count);
                foreach (var meta in zip.Value)
                {
                    writer.Write(meta.Key);
                    meta.Value.Serialize(writer);
                }
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            TimeRange = (new DateTime(reader.ReadInt64()), new DateTime(reader.ReadInt64()));

            OmittedFiles = Enumerable.Range(0, reader.Read7BitEncodedInt())
                .Select(i => reader.ReadString())
                .ToArray();

            Zips.Clear();
            foreach (var i in Enumerable.Range(0, reader.Read7BitEncodedInt()))
            {
                var metas = new Dictionary<string, CsvMeta>();
                Zips.Add(reader.ReadString(), metas);

                foreach (var j in Enumerable.Range(0, reader.Read7BitEncodedInt()))
                {
                    var name = reader.ReadString();
                    var meta = CsvMeta.Deserialize(reader);
                    metas.Add(name, meta);
                }
            }
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