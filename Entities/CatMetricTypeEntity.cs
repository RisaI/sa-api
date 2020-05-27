using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SAApi.Entities
{
    [Table("cat_metric_type")]
    public class CatMetricTypeEntity
    {
        [Key] [Column("id_cat_metric_type")] public int Id { get; set; }

        [Column("name")] public string Name { get; set; }
        [Column("unit")] public string Unit { get; set; }
        [Column("id_cat_metric_group")] public int IdCatMetricGroup { get; set; }

        // TODO: treshold
    }
}