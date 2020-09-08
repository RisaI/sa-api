
namespace SAApi.Data.Pipes
{
    public static class Extensions
    {
        public static DifferentiationPipe Differentiate(this Node node)
        {
            return new DifferentiationPipe(node, null);
        }
    }
}