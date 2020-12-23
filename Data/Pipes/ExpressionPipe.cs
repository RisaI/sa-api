using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NCalc;

namespace SAApi.Data.Pipes
{
    [Pipe("expr")]
    public class ExpressionPipe : Pipe
    {
        Expression Expression;

        public ExpressionPipe(Node child, Dictionary<string, object> options) : base(child.XType, child.YType, child)
        {
            Expression = new Expression(options["expression"] as string, EvaluateOptions.IgnoreCase);
            
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

        public override async Task<(object X, object Y)> PeekAsync()
        {
            var (x, y) = await Children.First().PeekAsync();

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