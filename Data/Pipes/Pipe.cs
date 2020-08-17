using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
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

        public static IEnumerable<(Type, PipeAttribute)> GetNamedPipeTypes()
        {
            return Assembly.GetCallingAssembly().GetTypes()
                .Select(t => (t, (PipeAttribute)t.GetCustomAttribute(typeof(PipeAttribute))))
                .Where(t => t.Item2 != null);
        }

        static Dictionary<string, Func<IDataWriter, string, Pipe>> LoadedPipes;
        static Pipe()
        {
            LoadedPipes = new Dictionary<string, Func<IDataWriter, string, Pipe>>();
            foreach (var resolved in GetNamedPipeTypes())
            {
                ParameterExpression[] @params = new ParameterExpression[] {
                    Expression.Parameter(typeof(IDataWriter), "writer"),
                    Expression.Parameter(typeof(string), "unparsed")
                };
                Expression expr;

                if (resolved.Item2.Options == null)
                {
                    expr = Expression.New(resolved.Item1.GetConstructor(new Type[] { typeof(IDataWriter) }), new Expression[] { @params[0] });
                }
                else
                {
                    expr = Expression.New(resolved.Item1.GetConstructor(new Type[] { typeof(IDataWriter), resolved.Item2.Options }), new Expression[] {
                        @params[0],
                        Expression.Convert(Expression.Call(
                            typeof(JsonSerializer).GetMethod(nameof(JsonSerializer.Deserialize), new Type[] { typeof(string), typeof(Type), typeof(JsonSerializerOptions) }),
                            @params[1],
                            Expression.Constant(resolved.Item2.Options),
                            Expression.Constant(new JsonSerializerOptions() { PropertyNameCaseInsensitive = true })
                        ), resolved.Item2.Options)
                    });
                }

                LoadedPipes.Add(resolved.Item2.Directive, Expression.Lambda(expr, false, @params).Compile() as Func<IDataWriter, string, Pipe>);
            }
        }

        public static Pipe CompilePipe(string type, IDataWriter writer, string parms)
        {
            return LoadedPipes[type].Invoke(writer, parms);
        }
    }
}