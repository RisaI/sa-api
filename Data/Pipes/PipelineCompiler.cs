using System;
using System.Linq;
using System.Threading.Tasks;

namespace SAApi.Data.Pipes
{
    public static class PipelineCompiler
    {
        public static async Task<Node> Compile(Models.NodeDescriptor desc, Services.DataSourceService data)
        {
            if (desc.Child != null && desc.Children != null)
                throw new ArgumentException("Set either the 'Child' or 'Children' argument, not both.");

            if (desc.Type == "data")
            {
                if (desc.Dataset == null)
                    throw new ArgumentException("Missing 'Dataset' property");

                return await data.GetSource(desc.Dataset.Source).GetNode(desc.Dataset.Id, desc.Dataset.Variant);
            }
            else
            {
                if (desc.Child == null && desc.Children == null)
                    throw new ArgumentException("A pipe must have either the 'Child' or the 'Children' argument.");

                if (desc.Children != null)
                    return Pipe.CompilePipe(
                        desc.Type,
                        await Task.WhenAll(desc.Children.Select(c => Compile(c, data))),
                        desc.Options
                    );
                else
                    return Pipe.CompilePipe(
                        desc.Type,
                        await Compile(desc.Child, data),
                        desc.Options
                    );
            }
        }
    }
}