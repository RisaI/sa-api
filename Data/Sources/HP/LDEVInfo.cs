
using System.Collections.Generic;
using System.Linq;

namespace SAApi.Data.Sources.HP
{
    public class LDEVInfo
    {
        public string ECCGroup { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public float Size { get; set; }
        public string MPU { get; set; }
        public string PoolName { get; set; }

        public List<HostPort> HostPorts { get; set; }
        public List<WWNInfo> Wwns { get; set; }

        public LDEVInfo(string[] csvColumns)
        {
            ECCGroup = csvColumns[0];
            Id = csvColumns[1].ToLowerInvariant();
            Name = csvColumns[2];
            Size = float.Parse(csvColumns[7], System.Globalization.CultureInfo.InvariantCulture);
            MPU = csvColumns[15].Split(';').Last();
            PoolName = csvColumns[18];

            HostPorts = new();
            Wwns = new();
        }
    }

    public class HostPort
    {
        public string Hostgroup { get; set; }
        public string Port { get; set; }

        public HostPort() { }
        public HostPort(string hostgroup, string port)
        {
            Hostgroup = hostgroup;
            Port = port;
        }
    }

    public class WWNInfo
    {
        public string Port { get; set; }
        public string Hostgroup { get; set; }
        public string Wwn { get; set; }
        public string Nickname { get; set; }
        public string Location { get; set; }

        public WWNInfo(string[] csvColumns)
        {
            Port = csvColumns[0];
            Hostgroup = csvColumns[1];
            Wwn = csvColumns[4];
            Nickname = csvColumns[5];
            Location = csvColumns[7];
        }
    }
}