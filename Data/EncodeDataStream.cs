
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SAApi.Data
{
    public class EncodeDataStream : IDataStream, IDisposable
    {
        static readonly Type[] XTypes = new Type[] {  };
        static readonly Type[] YTypes = new Type[] {  };

        public bool IsCompatible(Type xType, Type yType) => XTypes.Contains(xType) && YTypes.Contains(yType);

        public void Write<X, Y>(X x, Y y)
        {
            throw new NotImplementedException();
        }

        public Task<string> CreateEncodedString()
        {
            return Task.FromResult(string.Empty);
        }

        public void Dispose()
        {
            // TODO: dispose underlying stream
        }
    }
}