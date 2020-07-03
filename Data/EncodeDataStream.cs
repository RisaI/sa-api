
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SAApi.Data
{
    public class EncodeDataStream : IDataWriter, IDisposable
    {
        static readonly Type[] XTypes = new Type[] { typeof(DateTime) };
        static readonly Type[] YTypes = new Type[] { typeof(double), typeof(float), typeof(int) };

        public bool IsCompatible(Type xType, Type yType) => XTypes.Contains(xType) && YTypes.Contains(yType);

        private Stream _Stream;
        private BinaryWriter _Writer;

        public EncodeDataStream(Stream stream)
        {
            _Stream = stream;
            _Writer = new BinaryWriter(_Stream);
        }

        public Task Write<X, Y>(X x, Y y)
        {
            // X data
            if (x is DateTime a)
                _Writer.Write(a.Ticks);

            // Y data
            if (y is double d)
                _Writer.Write(d);
            else if (y is float f)
                _Writer.Write(f);
            else if (y is int i)
                _Writer.Write(i);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _Writer.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}