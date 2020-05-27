using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SAApi.Entities
{
    [Table("storage_entities")]
    public class StorageEntity
    {
        [Key] [Column("id")] public int Id { get; set; }

        [Column("name")] public string Name { get; set; }

        [Column("id_cat_storage_entity_status")]
        public int IDStatus { get; set; }

        [Column("id_cat_storage_entity_type")]
        public int IDType { get; set; }

        [Column("serial_number")]
        public string SerialNumber;

        [Column("parentId")]
        public int? ParentId { get; set; }
        public StorageEntity Parent { get; set; }

        public List<StorageEntity> Children { get; set; }

        public List<ExternalEntity> Externals { get; set; }

        // TODO: metrics
    }
}