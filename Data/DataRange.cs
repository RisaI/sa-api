using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace SAApi.Data
{
    public record DataRange(Type Type, IComparable From, IComparable To)
    {
        public DataRange? Intersection(DataRange b)
        {
            if (b.Type != Type)
                return null;

            var start = From.CompareTo(b.From) >= 0 ? From : b.From;
            var end   = To.CompareTo(b.To)     <= 0 ? To   : b.To;

            if (start.CompareTo(end) <= 0)
                return new DataRange(Type, start, end);
            else
                return null;
        }

        public bool Contains(DataRange b) =>
            From.CompareTo(b.From) <= 0 && To.CompareTo(b.To) >= 0;

        public static DataRange? BoundingBox(IEnumerable<DataRange> ranges)
        {
            if (ranges.Select(r => r.Type).Distinct().Count() != 1) return null;

            var min = ranges.Min(r => r.From)!;
            var max = ranges.Max(r => r.To)!;

            return new (ranges.First().Type, min, max);
        }

        public static DataRange Create<T>(T from, T to) where T : IComparable
        {
            return new DataRange(typeof(T), from, to);
        }

        public static DataRange Create<T>((T, T) range) where T : IComparable
        {
            return new DataRange(typeof(T), range.Item1, range.Item2);
        }
    }
}