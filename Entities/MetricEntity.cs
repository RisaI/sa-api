namespace SAApi.Entities
{
    public class MetricEntity
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public System.DateTime Date { get; set; }
        public CatMetricTypeEntity MetricTypeEntity { get; set; }
    }
}