
using System;

namespace SAApi.Data
{
    public interface IDataStream
    {
        bool IsCompatible(Type xType, Type yType);
        void Write<X,Y>(X x, Y y);
    }
}