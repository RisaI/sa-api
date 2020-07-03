
using System;

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

        public static (object, object) ParseRange(Type type, string a, string b)
        {
            if (type == typeof(DateTime))
                return (a != null ? (DateTime?)new DateTime(long.Parse(a)) : null,
                        b != null ? (DateTime?)new DateTime(long.Parse(b)) : null);

            return (null, null);
        }
    }
}