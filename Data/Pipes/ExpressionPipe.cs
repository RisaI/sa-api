using System;
using System.Linq;
using System.Threading.Tasks;
using NCalc;

namespace SAApi.Data.Pipes
{
    [Pipe("expr", typeof(ExpressionOptions))]
    public class ExpressionPipe : Pipe
    {
        Expression Expression;

        public ExpressionPipe(Node child, ExpressionOptions options) : base(child.XType, child.YType, child)
        {
            Expression = new Expression(options.Expression, EvaluateOptions.IgnoreCase);
            
            if (!XType.IsValueType || !YType.IsValueType)
                throw new InvalidOperationException("Expressions support only value types");

            Expression.Parameters["x"] = Activator.CreateInstance(XType);
            Expression.Parameters["y"] = Activator.CreateInstance(YType);

            YType = Expression.Evaluate().GetType();
        }

        public override Task<bool> HasNextAsync()
        {
            return Children.First().HasNextAsync();
        }

        public override async Task<(object, object)> NextAsync()
        {
            var (x, y) = await Children.First().NextAsync();

            Expression.Parameters["x"] = x;
            Expression.Parameters["y"] = y;

            return (x, Expression.Evaluate());
        }
    }

    public class ExpressionOptions
    {
        public string Expression { get; set; }
    }
}