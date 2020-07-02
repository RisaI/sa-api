
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SAApi.Data
{
    public interface IDataSource : IIdentified
    {
        IEnumerable<DataSet> DataSets { get; }
        Task GetData(IDataStream stream, string id, DataSelectionOptions selection, DataManipulationOptions manipulation);
        Task OnTick(IServiceScope scope);
    }
}