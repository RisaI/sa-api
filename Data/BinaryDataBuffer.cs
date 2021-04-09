
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SAApi.Data
{
    public class BinaryDataBuffer : IDataWriter, IAsyncDisposable, IDisposable
    {
        static readonly Type[] XTypes = new Type[] { typeof(DateTime) };
        static readonly Type[] YTypes = new Type[] { typeof(float), typeof(int) };

        private byte[] _BufferRaw = new byte[1024 * 1024 * 16]; // 16 MB buffer

        public BinaryDataBuffer()
        {
            
        }

        int Cursor = 0;
        public ValueTask Write<X, Y>(X x, Y y)
        {
            xSerializer.Invoke(_BufferRaw.AsSpan(Cursor), x);
            Cursor += xSize;
            ySerializer.Invoke(_BufferRaw.AsSpan(Cursor), y);
            Cursor += ySize;

            return default(ValueTask);
        }

        public async Task FlushAsync(Stream stream)
        {
            await stream.WriteAsync(BitConverter.GetBytes(Cursor), 0, sizeof(int));
            await stream.WriteAsync(_BufferRaw, 0, Cursor);

            Cursor = 0;
        }

        public ValueTask DisposeAsync()
        {
            return default(ValueTask);
        }

        public void Dispose()
        {

        }

        int xSize, ySize;
        SpanAction<byte, object> xSerializer, ySerializer;
        public ValueTask SetTypes(Type xType, Type yType)
        {
            if (!XTypes.Contains(xType) || !YTypes.Contains(yType))
                throw new InvalidOperationException("Unsupported data types.");

            Cursor = 0;

            xSize = xType == typeof(DateTime) ? sizeof(int) : System.Runtime.InteropServices.Marshal.SizeOf(xType);
            ySize = System.Runtime.InteropServices.Marshal.SizeOf(yType);

            xSerializer = GetSerializer(xType);
            ySerializer = GetSerializer(yType);

            return default(ValueTask);
        }

        public static SpanAction<byte, object> GetSerializer(Type type)
        {
            if (type == typeof(int))
                return (s, o) => BitConverter.TryWriteBytes(s, (int)o);
            if (type == typeof(float))
                return (s, o) => BitConverter.TryWriteBytes(s, (float)o);
            if (type == typeof(DateTime))
                return (s, o) => BitConverter.TryWriteBytes(s, (int)((DateTimeOffset)(DateTime)o).ToUnixTimeSeconds());
            
            throw new Exception("Unsupported type");
        }
    }
}