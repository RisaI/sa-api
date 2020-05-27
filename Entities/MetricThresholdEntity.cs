using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SAApi.Entities
{
    [Table("metric_tresholds")]
    public class MetricThresholdEntity
    {
        [Key] [Column("id_metric_threshold")] public int Id { get; set; }

        [Column("id_cat_metric_type")]
        public int MetricTypeEntityId { get; set; }
        [ForeignKey(nameof(MetricTypeEntityId))]
        public CatMetricTypeEntity MetricTypeEntity { get; set; }

        [Column("min_value")] public float MinValue { get; set; }
        [Column("max_value")] public float MaxValue { get; set; }
    }
}