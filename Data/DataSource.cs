
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SAApi.Data
{
    public abstract class DataSource : IIdentified
    {
        public abstract IEnumerable<Dataset> Datasets { get; }

        public string Id { get; private set; }
        public string Name { get { return _Config["name"] ?? _DefaultName; } }

        private string _DefaultName;
        protected IConfiguration _Config { get; private set; }

        public DataSource(string id, string defaultName, IConfigurationSection config)
        {
            Id = id;
            _DefaultName = defaultName;
            _Config = config;
        }

        public abstract Task<Node> GetNode(string id, string variant, Services.ResourceCache resCache);
        public abstract Task OnTick(IServiceScope scope);
    }
}