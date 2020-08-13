
namespace SAApi.Data.Pipes
{
    public static class Extensions
    {
        public static DifferentiationPipe Differentiate(this IDataWriter writer)
        {
            return new DifferentiationPipe(writer);
        }
    }
}