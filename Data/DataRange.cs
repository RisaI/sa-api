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

        public (T, T) ToTuple<T>() where T : IComparable => ((T, T))(From, To);

        public static DataRange? BoundingBox(IEnumerable<DataRange> ranges)
        {
            if (ranges.Select(r => r.Type).Distinct().Count() != 1) return null;

            var min = ranges.Min(r => r.From)!;
            var max = ranges.Max(r => r.To)!;

            return new (ranges.First().Type, min, max);
        }

        public static IEnumerable<DataRange> Simplify(IEnumerable<DataRange> ranges)
        {
            if (!ranges.Any()) return Enumerable.Empty<DataRange>();

            var sorted = ranges.OrderBy(r => r.From);

            return Merge(sorted).ToArray();

            IEnumerable<DataRange> Merge(IEnumerable<DataRange> m)
            {
                DataRange prev = m.First();

                foreach (var next in m.Skip(1))
                {
                    if (prev.Intersection(next) != null)
                        prev = BoundingBox(new [] { prev, next })!;
                    else
                    {
                        yield return prev;
                        prev = next;
                    }
                }

                yield return prev;
            }
        }

        public static DataRange Create<T>(T from, T to) where T : IComparable
        {
            return new DataRange(from.GetType(), from, to);
        }

        public static DataRange Create<T>((T, T) range) where T : IComparable
        {
            return new DataRange(range.Item1.GetType(), range.Item1, range.Item2);
        }
    }
}