
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SAApi.Data.Sources.HP
{


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
}