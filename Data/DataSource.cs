
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SAApi.Data
{
    public abstract class DataSource : IIdentified
    {
        public abstract IEnumerable<Dataset> Datasets { get; }

        public abstract string Id { get; }
        public abstract string Name { get; }

        public abstract Task GetData(IDataWriter writer, string id, string variant, DataSelectionOptions selection, DataManipulationOptions manipulation);
        public abstract Task OnTick(IServiceScope scope);
    }
}