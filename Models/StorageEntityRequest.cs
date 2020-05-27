namespace SAApi.Models
{
    public class StorageEntityRequest
    {
        public string Name;
        public string SerialNumber;
        public StorageEntityType Type;
        public int ParentId;
    }

    public enum StorageEntityType
    {
        DATA_CENTER = 1,
        SYSTEM,
        POOL,
        ADAPTER,
        PORT,
        HOST_GROUP,
    }
}