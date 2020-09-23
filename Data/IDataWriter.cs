
using System;
using System.Threading.Tasks;

namespace SAApi.Data
{
    public interface IDataWriter
    {
        ValueTask SetTypes(Type xType, Type yType);
        ValueTask Write<X,Y>(X x, Y y);
    }
}