
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SAApi.Data.Sources
{
    public class DummyDataSource : IDataSource
    {
        private static Random _Random = new Random();

        public IEnumerable<DataSet> DataSets { get; private set; }

        public string Id { get { return "dummy"; } }
        public string Name { get { return "Dummy Data Source"; } }

        public DummyDataSource()
        {
            DataSets = new [] { 
                new DataSet("testset", "Testovací set", "Obsahuje náhodně vygenerovaná data na test.", this, typeof(DateTime), typeof(float), (DateTime.Today.AddDays(-90), DateTime.Today))
            };
        }

        public Task GetData(IDataStream stream, string id, DataSelectionOptions selection, DataManipulationOptions manipulation)
        {
            if (id == "testset")
            {
                var range = Helper.IntersectDateTimes(DataSets.ElementAt(0).AvailableXRange, (selection.From, selection.To));

                DateTime current = range.Item1;

                while (current <= range.Item2)
                {
                    stream.Write<DateTime, float>(current, (float)_Random.NextDouble());
                }
            }

            return Task.CompletedTask;
        }

        public Task OnTick(IServiceScope scope)
        {
            return Task.CompletedTask;
        }
    }
}