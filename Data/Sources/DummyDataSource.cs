
using System;
using System.Collections.Generic;
using System.IO;
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

        public override string Type => "dummy";

        public override IEnumerable<DataRange> Dataranges
        {
            get { yield return DataRange.Create(DateTime.Today.AddDays(-300), DateTime.Today); }
        }

        private float[] SampleData;

        public DummyDataSource(string id, IConfigurationSection config) : base(id, "Dummy Data Source", config)
        {
            SampleData = new float[91];
            for (int i = 0; i < SampleData.Length; ++i)
                SampleData[i] = (float)_Random.NextDouble();

            Datasets = new [] { 
                new DummyDataset( // "Testovací set", "Obsahuje náhodně vygenerovaná data na test."
                    "testset", this, 
                    (DateTime.Today.AddDays(-90), DateTime.Today), TimeSpan.FromDays(1),
                    (date, idx) => SampleData[idx]
                ),
                new DummyDataset( // "Prázdný set", "Obsahuje nuly."
                    "zeros", this,
                    (DateTime.Today.AddDays(-90), DateTime.Today), TimeSpan.FromDays(1),
                    (date, idx) => 0f
                ),
                new DummyDataset( // "Pík", "Obsahuje jeden Gaussovský pík."
                    "peak", this,
                    (DateTime.Today.AddDays(-90), DateTime.Today), TimeSpan.FromDays(1),
                    (date, idx) => MathF.Exp(-0.7f * MathF.Pow((float)(date - DateTime.Today.AddDays(-45)).TotalDays, 2f))
                ),
                new DummyDataset( // "Hustá data", "Obsahuje 36k bodů."
                    "dense", this,
                    (DateTime.Today.AddDays(-300), DateTime.Today), TimeSpan.FromMinutes(12),
                    (date, idx) => (float)_Random.NextDouble()
                ),
                new DummyDataset( // "Extrémně hustá data", "Obsahuje 108k bodů."
                    "extradense", this,
                    (DateTime.Today.AddDays(-300), DateTime.Today), TimeSpan.FromMinutes(4),
                    (date, idx) => (float)_Random.NextDouble()
                ),
            };
        }
        
        public override Task<Node> GetNode(string id, string variant, Services.ResourceCache _)
        {
            var dataset = Datasets.First(d => d.Id == id) as DummyDataset;
            return Task.FromResult<Node>(new DummyNode((DateTime)dataset.DataRange.First().From, dataset.Jump, dataset.DataRange.BoundingBox(), dataset.Func));
        }

        public override Task OnTick(IServiceScope scope)
        {
            return Task.CompletedTask;
        }

        public override async Task GetBulkData(string id, IEnumerable<string> variant, DataRange range, Stream stream)
        {
            var (from, to) = range.ToTuple<DateTime>();
            var dataset = Datasets.First(d => d.Id == id) as DummyDataset;
            var bounds  = dataset.DataRange.BoundingBox();

            if (range.Intersection(bounds) != null)
            {
                var buffer = new byte[sizeof(int) + sizeof(float)];

                void SerializeFloat (float data,  int idx) => BitConverter.TryWriteBytes(buffer.AsSpan(idx, sizeof(float)), data);
                void SerializeDate(DateTime date, int idx) => BitConverter.TryWriteBytes(buffer.AsSpan(idx, sizeof(int)), date.ToMinuteRepre());

                while (from <= to) {
                    
                    SerializeDate(from, 0);
                    SerializeFloat(dataset.Func.Invoke(from, (int)((from - (DateTime)bounds.From) / dataset.Jump)), sizeof(int));

                    await stream.WriteAsync(buffer, 0, buffer.Length);
                    from += dataset.Jump;
                }        
            }
        }

        public class DummyDataset : Dataset
        {
            public TimeSpan Jump;
            public Func<DateTime, int, float> Func;
            
            public DummyDataset(string id, IIdentified source, (DateTime, DateTime) xRange, TimeSpan jump, Func<DateTime, int, float> func) :
                base(id, new string[0], null, source, typeof(DateTime), typeof(float), new [] { Data.DataRange.Create(xRange) }, null)
            {
                Jump = jump;
                Func = func;
            }
        }

        public class DummyNode : Node
        {
            private int _Idx = 0;
            private DateTime _Cursor;
            private DateTime _Max;
            private TimeSpan _Jump;
            private Func<DateTime, int, float> _Func;
            private DataRange DataRange;

            public DummyNode(DateTime start, TimeSpan jump, DataRange availableXRange, Func<DateTime, int, float> func)
                : base(typeof(DateTime), typeof(float))
            {
                _Jump = jump;
                _Func = func;
                DataRange = availableXRange;

                _Cursor = start;
            }

            public override void ApplyXRange((object, object) xRange)
            {
                var range = DataRange.Intersection(Data.DataRange.Create<DateTime>(((DateTime, DateTime))xRange));
                _Max = (DateTime)range.To;

                while (_Cursor < (DateTime)range.From)
                {
                    _Cursor += _Jump;
                    ++_Idx;
                }
            }

            public override Task<bool> HasNextAsync()
            {
                return Task.FromResult(_Cursor <= _Max);
            }

            public override Task<(object, object)> NextAsync()
            {
                var val = PeekAsync();

                _Cursor += _Jump;
                ++_Idx;

                return val;
            }

            public override Task<(object X, object Y)> PeekAsync()
            {
                return Task.FromResult<(object, object)>((_Cursor, _Func.Invoke(_Cursor, _Idx)));
            }
        }
    }
}