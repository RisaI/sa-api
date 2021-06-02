using System.Collections.Generic;

namespace SAApi.Models
{
    public record BulkDataRequest(
        IEnumerable<string> Variants,
        
        string From,
        string To
    ) {

    }
}