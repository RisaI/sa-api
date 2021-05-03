using System;

namespace SAApi.Data
{
    public struct HighPrecTime : IComparable
    {
        const long TickPerNanosecond = TimeSpan.TicksPerMillisecond / 1000;
        public DateTime Time;

        public HighPrecTime(DateTime time)
        {
            Time = time;
        }

        public HighPrecTime(int nanoseconds)
        {
            Time = new DateTime(nanoseconds * TickPerNanosecond);
        }

        public static HighPrecTime Parse(ReadOnlySpan<char> text, ReadOnlySpan<char> format)
        {
            return new HighPrecTime(DateTime.ParseExact(text, format, System.Globalization.CultureInfo.InvariantCulture));
        }

        public int CompareTo(object obj)
        {
            return Time.CompareTo(obj);
        }

        public long ToLongRepresentation()
        {
            return Time.Ticks / TickPerNanosecond;
        }
    }
}