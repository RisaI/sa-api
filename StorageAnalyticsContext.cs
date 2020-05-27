
using Microsoft.EntityFrameworkCore;

namespace SAApi
{
    public class StorageAnalyticsContext : DbContext
    {
        public DbSet<Entities.StorageEntity> StorageEntities { get; set; }
        public DbSet<Entities.ExternalEntity> Externals { get; set; }
    }
}