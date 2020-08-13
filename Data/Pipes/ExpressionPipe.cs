using System;
using System.Linq;
using System.Threading.Tasks;
using NCalc;

namespace SAApi.Data.Pipes
{
    [Pipe("expr")]
    public class ExpressionPipe : Pipe
    {
        Expression Expression;

        public ExpressionPipe(IDataWriter parent, ExpressionOptions options) : base(parent)
        {
            Expression = new Expression(options.Expression, EvaluateOptions.IgnoreCase);
        }

        protected override (Type, Type) OnSetTypes(Type xType, Type yType)
        {
            if (!xType.IsValueType || !yType.IsValueType)
                throw new InvalidOperationException("Expressions support only value types");

            Expression.Parameters["x"] = Activator.CreateInstance(xType);
            Expression.Parameters["y"] = Activator.CreateInstance(yType);

            return (xType, Expression.Evaluate().GetType());
        }

        public override async Task Write<X, Y>(X x, Y y)
        {
            Expression.Parameters["x"] = x;
            Expression.Parameters["y"] = y;

            await Parent.Write(x, Expression.Evaluate());
        }

        protected override Task OnTerminate()
        {
            return Task.CompletedTask;
        }
    }

    public class ExpressionOptions
    {
        public string Expression { get; set; }
    }
}