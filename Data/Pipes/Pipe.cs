using System;
using System.Threading.Tasks;

namespace SAApi.Data.Pipes
{
    public abstract class Pipe : IDataWriter
    {
        public IDataWriter Parent
        {
            get;
            private set;
        }

        public Pipe(IDataWriter parent)
        {
            Parent = parent;
        }

        public abstract Task Write<X, Y>(X x, Y y);
        protected abstract Task OnTerminate();
        protected abstract (Type, Type) OnSetTypes(Type xType, Type yType);

        public void SetTypes(Type xType, Type yType)
        {
            var (nx, ny) = OnSetTypes(xType, yType);

            Parent.SetTypes(nx, ny);
        }

        public async Task Terminate()
        {
            await OnTerminate();

            if (Parent is Pipe p)
                await p.Terminate();
        }
    }
}