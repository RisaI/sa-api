using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SAApi.Data.Pipes
{
    [Pipe("sum")]
    public class SumPipe : Pipe
    {
        protected CastMode Mode;

        public SumPipe(Node[] children, Dictionary<string, object> options) : base(children.First().XType, children.First().YType, children)
        {
            if (children.Select(c => c.XType).Distinct().Count() > 1)
                throw new ArgumentException("All children must have the same X type.");

            if (!typeof(IComparable).IsAssignableFrom(children.First().XType))
                throw new ArgumentException($"The X type must implement {nameof(IComparable)}");
            
            if (children.Select(c => c.YType).Except(new [] { typeof(int), typeof(float), typeof(double) }).Count() > 0)
                throw new ArgumentException("All children must have a numerical Y type.");
                
            if (children.Any(c => c.YType == typeof(double)))
                Mode = CastMode.Double;
            else if (children.Any(c => c.YType == typeof(float)))
                Mode = CastMode.Float;
            else
                Mode = CastMode.Int;

            YType = Mode switch {
                CastMode.Double => typeof(double),
                CastMode.Float => typeof(float),
                CastMode.Int or _ => typeof(int),
            };
        }

        private async Task<(object X, object[] Ys)?> PullNext()
        {
            if ((await Task.WhenAll(Children.Select(c => c.HasNextAsync()))).Any(v => !v))
                return null;

            var data = (await Task.WhenAll(Children.Select(c => c.NextAsync()))).ToArray();
            var max = data.Max(d => d.X);

            for (int i = 0; i < data.Length; ++i)
            {
                if (data[i].X.Equals(max))
                    continue;
                
                check:
                if (!await Children[i].HasNextAsync())
                    return null;

                var next = await Children[i].PeekAsync();
                var comp = (next.X as IComparable).CompareTo(max as IComparable);
                
                if (comp == 0)
                    data[i] = next;
                else if (comp > 0)
                    data[i] = (max, next.Y); // TODO: interpolate
                else
                {
                    await Children[i].NextAsync();
                    goto check;
                }
            }

            return (max, data.Select(d => d.Y).ToArray());
        }

        bool init = false;
        (object X, object[] Ys)? Next = null;

        public override async Task<bool> HasNextAsync()
        {
            if (!init)
            {
                Next = await PullNext();
                init = true;
            }

            return Next.HasValue;
        }

        public override async Task<(object X, object Y)> NextAsync()
        {
            var current = await PeekAsync();
            Next = await PullNext();

            return current;
        }

        public override async Task<(object X, object Y)> PeekAsync()
        {
            if (!init)
            {
                Next = await PullNext();
                init = true;
            }

            return (Next.Value.X, (Mode switch {
                CastMode.Double => (object)Next.Value.Ys.Select(d => Convert.ToDouble(d)).Sum(),
                CastMode.Float => (object)Next.Value.Ys.Select(d => Convert.ToSingle(d)).Sum(),
                CastMode.Int or _ => (object)Next.Value.Ys.Select(d => (int)d).Sum(),
            }));
        }

        protected enum CastMode
        {
            Int,
            Float,
            Double,
        }
    }

    [Pipe("avg")]
    public class AvgPipe : SumPipe
    {
        private int N;
        public AvgPipe(Node[] children, Dictionary<string, object> options) : base(children, options)
        {
            N = children.Length;
            YType = typeof(float);
        }

        public override Task<bool> HasNextAsync() => base.HasNextAsync();

        public override async Task<(object X, object Y)> NextAsync()
        {
            var point = await base.NextAsync();
            return (point.X, Convert.ToSingle(point.Y) / N);
        }
    }
}