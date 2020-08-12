
using System;
using System.Threading.Tasks;

namespace SAApi.Data
{
    public interface IDataWriter
    {
        void SetTypes(Type xType, Type yType);
        Task Write<X,Y>(X x, Y y);
    }
}