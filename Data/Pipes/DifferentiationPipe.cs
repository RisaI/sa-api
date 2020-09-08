using System;
using System.Collections.Generic;
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
        public DifferentiationPipe(Node parent, Dictionary<string, object> opts) : base(parent.XType, typeof(float), parent)
        {
            rotator = new Common.RotatingList<(object, object)>(3);
            SetTypes(parent.XType, parent.YType);
        }

        Delegate xDiff, yDiff;
        Func<object, object, float> yxDiv;
        protected void SetTypes(Type xType, Type yType)
        {
            yDiff = CreateBinaryExpr(yType, yType, (l, r) => Expression.Convert(Expression.Subtract(l, r), typeof(float))).Compile();

            if (xType == typeof(DateTime))
            {
                xDiff = CreateBinaryExpr(xType, xType, (l, r) =>
                    Expression.Convert(
                        Expression.Property(Expression.Subtract(l, r), typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalSeconds))),
                        typeof(float)
                    ) // Divide to obtain time difference in seconds
                ).Compile();
            }
            else
            {
                xDiff = CreateBinaryExpr(xType, xType, (l, r) =>
                    Expression.Convert(Expression.Subtract(l, r), typeof(float))
                ).Compile();
            }

            yxDiv = (l, r) => ((float)l) / ((float)r);
        }

        static LambdaExpression CreateBinaryExpr(Type leftType, Type rightType, Func<Expression, Expression, Expression> operation)
        {
            var left = Expression.Parameter(leftType, "left");
            var right = Expression.Parameter(rightType, "right");
            return Expression.Lambda(operation.Invoke(left, right), false, new ParameterExpression[] { left, right });
        }

        int _Written = 0;
        bool last = false;

        public override async Task<bool> HasNextAsync()
        {
            if (_Written < 1)
                await PullFirstAsync();

            return await Children.First().HasNextAsync() || last;
        }

        public override async Task<(object, object)> NextAsync()
        {
            if (_Written < 1)
                await PullFirstAsync();

            if (!last)
            {
                rotator.Push(await Children.First().NextAsync());

                if (!(await Children.First().HasNextAsync()))
                    last = true;
                
                if (+_Written++ == 1)
                {
                    var (x, y) = rotator[0];
                    var (pX, pY) = rotator[-1];

                    return (pX, yxDiv.Invoke(yDiff.DynamicInvoke(y, pY), xDiff.DynamicInvoke(x, pX)));

                }
                else
                {
                    var (pX, pY) = rotator[-2];
                    var (nX, nY) = rotator[0];
                    var cX = rotator[-1].Item1;

                    return (cX, yxDiv.Invoke(yDiff.DynamicInvoke(nY, pY), xDiff.DynamicInvoke(nX, pX)));
                }
            }
            else
            {
                last = false;

                var (x, y) = rotator[0];
                var (pX, pY) = rotator[-1];

                return (x, yxDiv.Invoke(yDiff.DynamicInvoke(y, pY), xDiff.DynamicInvoke(x, pX)));
            }
        }

        private async Task PullFirstAsync()
        {
            var f = Children.First();

            if (await f.HasNextAsync())
            {
                _Written = 1;
                rotator.Push(await f.NextAsync());
            }
        }
    }
}