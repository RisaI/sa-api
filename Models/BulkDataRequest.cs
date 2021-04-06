

namespace SAApi.Models
{
    public record BulkDataRequest(
        string[] Variants,
        
        string From,
        string To
    ) {

    }
}