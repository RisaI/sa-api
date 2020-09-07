
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SAApi.Data.Sources
{
    public class DummyDataSource : DataSource
    {
        private static Random _Random = new Random();

        public override IEnumerable<Dataset> Datasets { get; }

        public override string Id { get { return "dummy"; } }
        public override string Name { get { return "Dummy Data Source"; } }

        private float[] SampleData;

        public DummyDataSource(IConfigurationSection config)
        {
            SampleData = new float[91];
            for (int i = 0; i < SampleData.Length; ++i)
                SampleData[i] = (float)_Random.NextDouble();

            Datasets = new [] { 
                new Dataset("testset", "Testovací set", "Obsahuje náhodně vygenerovaná data na test.", this, typeof(DateTime), typeof(float), (DateTime.Today.AddDays(-90), DateTime.Today)),
                new Dataset("zeros", "Prázdný set", "Obsahuje nuly.", this, typeof(DateTime), typeof(float), (DateTime.Today.AddDays(-90), DateTime.Today)),
                new Dataset("peak", "Pík", "Obsahuje jeden Gaussovský pík.", this, typeof(DateTime), typeof(float), (DateTime.Today.AddDays(-90), DateTime.Today)),
                new Dataset("dense", "Hustá data", "Obsahuje 36k bodů.", this, typeof(DateTime), typeof(float), (DateTime.Today.AddDays(-300), DateTime.Today)),
                new Dataset("extradense", "Extrémně hustá data", "Obsahuje 108k bodů.", this, typeof(DateTime), typeof(float), (DateTime.Today.AddDays(-300), DateTime.Today)),
            };
        }

        public override async Task<Node> GetNode(string id, string variant, DataSelectionOptions selection)
        {
            if (id == "testset")
            {
                var range = Helper.IntersectDateTimes(Datasets.ElementAt(0).AvailableXRange, (selection.From, selection.To));

                DateTime current = range.Item1.Date;

                while (current <= range.Item2)
                {
                    await stream.Write<DateTime, float>(current, SampleData[(int)(current - (DateTime)Datasets.ElementAt(0).AvailableXRange.Item1).TotalDays]);
                    current = current.AddDays(1);
                }
            }
            else if (id == "zeros")
            {
                var range = Helper.IntersectDateTimes(Datasets.ElementAt(1).AvailableXRange, (selection.From, selection.To));

                DateTime current = range.Item1.Date;

                while (current <= range.Item2)
                {
                    await stream.Write<DateTime, float>(current, 0);
                    current = current.AddDays(1);
                }
            }
            else if (id == "peak")
            {
                var available = Datasets.ElementAt(2).AvailableXRange;
                var range = Helper.IntersectDateTimes(available, (selection.From, selection.To));

                DateTime current = range.Item1.Date;
                var center = (DateTime)range.Item1 + ((DateTime)range.Item2 - (DateTime)range.Item1) / 2;

                while (current <= range.Item2)
                {
                    await stream.Write<DateTime, float>(current, MathF.Exp(-0.7f * MathF.Pow((float)(current - center).TotalDays, 2f)));
                    current = current.AddDays(1);
                }
            }
            else if (id == "dense")
            {
                var range = Helper.IntersectDateTimes(Datasets.ElementAt(3).AvailableXRange, (selection.From, selection.To));

                DateTime current = range.Item1.Date;

                while (current <= range.Item2)
                {
                    await stream.Write<DateTime, float>(current, (float)_Random.NextDouble() * MathF.Sin(MathF.PI * (float)(current - range.Item1).TotalMinutes / (12f * 3600)));
                    current = current.AddMinutes(12);
                }
            }
            else if (id == "extradense")
            {
                var range = Helper.IntersectDateTimes(Datasets.ElementAt(3).AvailableXRange, (selection.From, selection.To));

                DateTime current = range.Item1.Date;

                while (current <= range.Item2)
                {
                    await stream.Write<DateTime, float>(current, (float)_Random.NextDouble());
                    current = current.AddMinutes(4);
                }
            }
        }

        public override Task OnTick(IServiceScope scope)
        {
            return Task.CompletedTask;
        }

        public class DummyNode : Node
        {
            private DateTime _Max;
            private DateTime _Cursor;
            private TimeSpan _Jump;

            public DummyNode(DateTime cursor, TimeSpan jump, DateTime max)
                : base(typeof(DateTime), typeof(float))
            {
                _Cursor = cursor;
                _Jump = jump;
                _Max = max;
            }

            public override Task<bool> HasNextAsync()
            {
                return Task.FromResult(_Cursor <= _Max);
            }

            public override Task<(object, object)> NextAsync()
            {


                throw new NotImplementedException();
            }
        }
    }
}