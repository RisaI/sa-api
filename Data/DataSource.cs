
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

        public abstract Task<Node> GetNode(string id, string variant);
        public abstract Task OnTick(IServiceScope scope);
    }
}