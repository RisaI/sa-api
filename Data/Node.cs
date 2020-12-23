using System;
using System.Threading.Tasks;

namespace SAApi.Data
{
    public abstract class Node
    {
        private bool _Connected = false;
        public bool Connected
        {
            get { return _Connected; }
            set { _Connected = value || _Connected; }
        }

        public Type XType { get; protected set; }
        public Type YType { get; protected set; }

        public Node(Type xType, Type yType)
        {
            XType = xType;
            YType = yType;
        }
        
        public abstract Task<bool> HasNextAsync();
        public abstract Task<(object X, object Y)> NextAsync();
        public abstract Task<(object X, object Y)> PeekAsync();

        public abstract void ApplyXRange((object From, object To) xRange);
        public virtual Type QueryLeafXType() { return XType; }

        public Models.PipelineSpecs GetSpecs()
        {
            return new Models.PipelineSpecs() { XType = XType, YType = YType };
        }
    }
}