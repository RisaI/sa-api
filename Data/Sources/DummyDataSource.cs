
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SAApi.Data.Sources
{
    public class DummyDataSource : DataSource
    {
        private static Random _Random = new Random();

        public override IEnumerable<Dataset> Datasets { get; }

        public override string Id { get { return "dummy"; } }
        public override string Name { get { return "Dummy Data Source"; } }

        public DummyDataSource()
        {
            Datasets = new [] { 
                new Dataset("testset", "Testovací set", "Obsahuje náhodně vygenerovaná data na test.", this, typeof(DateTime), typeof(float), (DateTime.Today.AddDays(-90), DateTime.Today))
            };
        }

        public override async Task GetData(IDataWriter stream, string id, DataSelectionOptions selection, DataManipulationOptions manipulation)
        {
            if (id == "testset")
            {
                stream.IsCompatible(typeof(DateTime), typeof(float));

                var range = Helper.IntersectDateTimes(Datasets.ElementAt(0).AvailableXRange, (selection.From, selection.To));

                DateTime current = range.Item1;

                while (current <= range.Item2)
                {
                    await stream.Write<DateTime, float>(current, (float)_Random.NextDouble());
                    current = current.AddDays(1);
                }
            }
        }

        public override Task OnTick(IServiceScope scope)
        {
            return Task.CompletedTask;
        }
    }
}