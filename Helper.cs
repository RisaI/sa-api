
using System;
using System.Collections.Generic;
using SAApi.Data;

#nullable enable

namespace SAApi
{
    public static class Helper
    {
        public static (T,T) Intersect<T>((T,T) a, (T,T) b) where T : IComparable
        {
            var result = (a.Item1.CompareTo(b.Item1) >= 0 ? a.Item1 : b.Item1, a.Item2.CompareTo(b.Item2) <= 0 ? a.Item2 : b.Item2);

            if (result.Item2.CompareTo(result.Item1) < 0)
                throw new InvalidOperationException("No intersection possible.");

            return result;
        }

        public static (DateTime, DateTime) IntersectDateTimes((object, object) a, (object, object) b)
        {
            return Intersect(((DateTime)a.Item1, (DateTime)a.Item2), (b.Item1 as DateTime? ?? DateTime.MinValue, b.Item2 as DateTime? ?? DateTime.MaxValue));
        }

        public static (IComparable?, IComparable?) ParseRange(Type type, string a, string b)
        {
            if (type == typeof(DateTime))
                return (a != null ? (DateTime?)DateTimeOffset.FromUnixTimeSeconds(int.Parse(a)).LocalDateTime : null,
                        b != null ? (DateTime?)DateTimeOffset.FromUnixTimeSeconds(int.Parse(b)).LocalDateTime : null);

            return (null, null);
        }

        public static int Mod(int a, int b)
        {
            return ((a % b) + b) % b;
        }

        public static float Mod(float a, float b)
        {
            return ((a % b) + b) % b;
        }

        public static double Mod(double a, double b)
        {
            return ((a % b) + b) % b;
        }
    }

    public static class Extensions
    {
        public static async System.Threading.Tasks.Task Consume(this Data.IDataWriter writer, Data.Node node)
        {
            await writer.SetTypes(node.XType, node.YType);

            while (await node.HasNextAsync())
            {
                var (x, y) = await node.NextAsync();
                await writer.Write(x, y);
            }
        }

        public static DataRange? BoundingBox(this IEnumerable<DataRange> ranges) => DataRange.BoundingBox(ranges);
    }
}