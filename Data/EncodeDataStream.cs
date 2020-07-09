
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

        public async Task Write<X, Y>(X x, Y y)
        {
            await Write(x);
            await Write(y);
        }

        private Task Write<T>(T obj)
        {
            // X data
            if (obj is DateTime a)
                _Writer.Write(a.Ticks);
            else if (obj is double d)
                _Writer.Write(d);
            else if (obj is float f)
                _Writer.Write(f);
            else if (obj is int i)
                _Writer.Write(i);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _Writer.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}