using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace SAApi.Data.Pipes
{
    public abstract class Pipe : Node
    {
        public Node[] Children
        {
            get;
            private set;
        }

        public Pipe(Type xType, Type yType, params Node[] children) :
            base(xType, yType)
        {
            Children = children;
        }

        public static IEnumerable<(Type, PipeAttribute)> GetNamedPipeTypes()
        {
            return Assembly.GetCallingAssembly().GetTypes()
                .Select(t => (t, (PipeAttribute)t.GetCustomAttribute(typeof(PipeAttribute))))
                .Where(t => t.Item2 != null);
        }

        public override void ApplyXRange((object From, object To) xRange)
        {
            foreach (var child in Children)
                child.ApplyXRange(xRange);
        }

        public override Type QueryLeafXType()
        {
            var types = Children.Select(c => c.QueryLeafXType()).Distinct().ToArray();

            if (types.Length > 1)
                throw new ArgumentException("Leaf nodes contain incompatible x axis types.");
            else if (types.Length < 1)
                throw new ArgumentException("There are no leaf nodes.");
            
            return types[0];
        }

        static Dictionary<string, Func<Node, Dictionary<string, object>, Pipe>> _SingleChild;
        static Dictionary<string, Func<Node[], Dictionary<string, object>, Pipe>> _WithChildren;
        static Pipe()
        {
            _SingleChild = new Dictionary<string, Func<Node, Dictionary<string, object>, Pipe>>();
            _WithChildren = new Dictionary<string, Func<Node[], Dictionary<string, object>, Pipe>>();

            foreach (var resolved in GetNamedPipeTypes())
            {
                var sConstr = resolved.Item1.GetConstructor(new Type[] { typeof(Node), typeof(Dictionary<string, object>) });
                var mConstr = resolved.Item1.GetConstructor(new Type[] { typeof(Node[]), typeof(Dictionary<string, object>) });

                if (sConstr == null && mConstr == null)
                    throw new InvalidOperationException($"Named pipe '{resolved.Item1.Name}' is missing a compatible constructor.");

                if (sConstr != null)
                {
                    ParameterExpression[] @params = new ParameterExpression[] {
                        Expression.Parameter(typeof(Node), "child"),
                        Expression.Parameter(typeof(Dictionary<string, object>), "options")
                    };

                    var expr = Expression.New(sConstr, @params);

                    _SingleChild.Add(
                        resolved.Item2.Directive,
                        Expression.Lambda(expr, false, @params).Compile() as Func<Node, Dictionary<string, object>, Pipe>
                    );
                }

                if (mConstr != null)
                {
                    ParameterExpression[] @params = new ParameterExpression[] {
                        Expression.Parameter(typeof(Node[]), "children"),
                        Expression.Parameter(typeof(Dictionary<string, object>), "options")
                    };

                    var expr = Expression.New(mConstr, @params);

                    _WithChildren.Add(
                        resolved.Item2.Directive,
                        Expression.Lambda(expr, false, @params).Compile() as Func<Node[], Dictionary<string, object>, Pipe>
                    );
                }
            }
        }

        public static Pipe CompilePipe(string type, Node child, Dictionary<string, object> @params)
        {
            return _SingleChild[type].Invoke(child, @params);
        }

        public static Pipe CompilePipe(string type, Node[] children, Dictionary<string, object> @params)
        {
            return _WithChildren[type].Invoke(children, @params);
        }
    }
}