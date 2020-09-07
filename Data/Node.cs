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

        public Type XType { get; private set; }
        public Type YType { get; private set; }

        public Node(Type xType, Type yType)
        {
            XType = xType;
            YType = yType;
        }
        
        public abstract Task<bool> HasNextAsync();
        public abstract Task<(object, object)> NextAsync();
    }
}