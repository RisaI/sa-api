using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SAApi.Entities
{
    [Table("externals")]
    public class ExternalEntity
    {
        [Key] [Column("id_external")] public int IdExternal { get; set; }

        [Column("id_cat_external_type")] public int IdType { get; set; }
        
        [Column("value")] public string Value { get; set; }

        [Column("id_storage_entity")]
        public int? StorageEntityId { get; set; }
        public StorageEntity StorageEntity { get; set; }
    }
}