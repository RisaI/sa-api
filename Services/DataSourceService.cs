using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SAApi.Services
{
    public class DataSourceService : IHostedService, IDisposable
    {
        private readonly List<Data.DataSource> _Sources;
        private readonly ILogger<DataSourceService> _Logger;
        private readonly IServiceScopeFactory _ScopeFactory;
        private Timer _timer;
    
        public DataSourceService(ILogger<DataSourceService> logger, IServiceScopeFactory scopeFactory)
        {
            _Logger = logger;
            _ScopeFactory = scopeFactory;
            _Sources = new List<Data.DataSource>(128);
        }
    
        public Task StartAsync(CancellationToken stoppingToken)
        {
            _Logger.LogInformation("DataSource Hosted Service running.");

            // TODO: load sources
            _Sources.Add(new Data.Sources.DummyDataSource());
    
            _timer = new Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(300));
    
            return Task.CompletedTask;
        }
    
        private void OnTick(object state)
        {
            using (var scope = _ScopeFactory.CreateScope())
            {
                Task.WaitAll(_Sources.Select(s => s.OnTick(scope)).ToArray());
            }
        }
    
        public Task StopAsync(CancellationToken stoppingToken)
        {
            _Logger.LogInformation("DataSource Hosted Service is stopping.");
    
            _timer?.Change(Timeout.Infinite, 0);
    
            return Task.CompletedTask;
        }
    
        public void Dispose()
        {
            _timer?.Dispose();
        }

        public IReadOnlyList<Data.DataSource> AllSources => _Sources.AsReadOnly();
        public IEnumerable<Data.Dataset> AllDataSets => _Sources.SelectMany(s => s.Datasets);
        public Data.DataSource GetSource(string id) => _Sources.FirstOrDefault(s => s.Id == id);

        public async Task GetTrace(Data.IDataWriter stream, string source, string id, Data.DataSelectionOptions selectionOptions, Data.DataManipulationOptions manipulationOptions) => 
            await GetSource(source).GetData(stream, id, selectionOptions, manipulationOptions);
    }
}