using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace SAApi.Data.Pipes
{
    [Pipe("diff")]
    public class DifferentiationPipe : Pipe
    {
        static Type[] SupportedXTypes = new Type[] { typeof(DateTime), typeof(int), typeof(float) };
        static Type[] SupportedYTypes = new Type[] { typeof(int), typeof(float) };

        Common.RotatingList<(object, object)> rotator;
        public DifferentiationPipe(IDataWriter parent) : base(parent)
        {
            rotator = new Common.RotatingList<(object, object)>(3);
        }

        Delegate xDiff, yDiff;
        Func<object, object, float> yxDiv;
        protected override (Type, Type) OnSetTypes(Type xType, Type yType)
        {
            yDiff = CreateBinaryExpr(yType, yType, (l, r) => Expression.Convert(Expression.Subtract(l, r), typeof(float))).Compile();

            if (xType == typeof(DateTime))
            {
                xDiff = CreateBinaryExpr(xType, xType, (l, r) =>
                    Expression.Property(Expression.Subtract(l, r), typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalSeconds))) // Divide to obtain time difference in seconds
                ).Compile();
            }
            else
            {
                xDiff = CreateBinaryExpr(xType, xType, (l, r) =>
                    Expression.Convert(Expression.Subtract(l, r), typeof(float))
                ).Compile();
            }

            yxDiv = (l, r) => ((float)l) / ((float)r);

            return (xType, typeof(float));
        }

        static LambdaExpression CreateBinaryExpr(Type leftType, Type rightType, Func<Expression, Expression, Expression> operation)
        {
            var left = Expression.Parameter(leftType, "left");
            var right = Expression.Parameter(rightType, "right");
            return Expression.Lambda(operation.Invoke(left, right), false, new ParameterExpression[] { left, right });
        }

        int _Written = 0;
        public override async Task Write<X, Y>(X x, Y y)
        {
            rotator.Push((x, y));

            if (_Written == 1)
            {
                var (pX, pY) = ((X,Y))rotator[-1];

                await Parent.Write(pX, yxDiv.Invoke(yDiff.DynamicInvoke(y, pY), xDiff.DynamicInvoke(x, pX)));

            }
            else if (_Written > 1)
            {
                var (pX, pY) = ((X,Y))rotator[-2];
                var cX = (X)rotator[-1].Item1;

                await Parent.Write(pX, yxDiv.Invoke(yDiff.DynamicInvoke(y, pY), xDiff.DynamicInvoke(x, pX)));
            }

            ++_Written;
        }

        protected override async Task OnTerminate()
        {
            if (_Written > 1)
            {
                var (x, y) = rotator[0];
                var (pX, pY) = rotator[-1];

                await Parent.Write(pX, yxDiv.Invoke(yDiff.DynamicInvoke(y, pY), xDiff.DynamicInvoke(x, pX)));
            }
        }
    }
}