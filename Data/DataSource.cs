using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;

#nullable enable

namespace SAApi.Data
{
    public abstract class DataSource : IIdentified
    {
        public abstract IEnumerable<Dataset> Datasets { get; }
        public IEnumerable<string> Features { get { return RegisteredFeatures.Where(kv => kv.Value.IsActive.Invoke()).Select(kv => kv.Key); } }
        public Dictionary<string, string> Metadata { get; }

        public string Id { get; private set; }
        public string Name { get { return _Config["name"] ?? _DefaultName; } }
        public abstract string Type { get; }

        private string _DefaultName;
        protected IConfiguration _Config { get; private set; }

        public DataSource(string id, string defaultName, IConfigurationSection config)
        {
            Id = id;
            _DefaultName = defaultName;
            _Config = config;
            Metadata = new Dictionary<string, string>();
        }

        public abstract Task<Node> GetNode(string id, string variant, Services.ResourceCache resCache);
        public abstract Task GetBulkData(string id, IEnumerable<string> variant, DataRange range, System.IO.Stream stream);
        public abstract Task OnTick(IServiceScope scope);

        public Task<object?> ActivateFeatureAsync(string feature, HttpRequest request)
        {
            if (RegisteredFeatures.ContainsKey(feature) && RegisteredFeatures[feature].IsActive.Invoke())
                return RegisteredFeatures[feature].Functor.Invoke(request);
            else
                return Task.FromResult<object?>(null);
        }

        private Dictionary<string, FeatureInfo> RegisteredFeatures = new();
        public void RegisterFeature<T, R>(string feature, Func<T, HttpRequest, Task<R>> action, Func<bool>? available)
        {
            RegisteredFeatures.Add(
                feature,
                new FeatureInfo(
                    available ?? (() => true),
                    async (request) => {
                        var @params = await JsonSerializer.DeserializeAsync<T>(request.Body);

                        return await action.Invoke(@params!, request);
                    }
                )
            );
        }

        public class FeatureNames
        {
            public const string 
                VariantRecommend = "variant_recommend",
                LDEVMap = "ldev_map";

        }

        private record FeatureInfo(Func<bool> IsActive, Func<HttpRequest, Task<object?>> Functor);
    }
}