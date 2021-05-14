
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAApi.Data.Sources.HP
{
    public class LDEVInfo
    {
        public string ECCGroup { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public LdevType Type { get; set; }
        public float Size { get; set; }
        public string MPU { get; set; }
        public Pool? Pool { get; set; }

        public List<HostPort> HostPorts { get; set; }
        public List<WWNInfo> Wwns { get; set; }

        public LDEVInfo(string[] csvRow)
        {
            ECCGroup = csvRow[0];
            Id = csvRow[1].ToLowerInvariant();
            Name = csvRow[2];
            Size = float.Parse(csvRow[7], CultureInfo.InvariantCulture);
            MPU = csvRow[15].Split(';').Last();

            Type = csvRow[4] switch {
                "Basic" => LdevType.Basic,
                "Dynamic Provisioning" => LdevType.Dynamic,
                "External" => LdevType.External,
                _ => LdevType.Unknown,
            };

            HostPorts = new();
            Wwns = new();
        }

        public static (int? PoolId, string PoolName) GetPoolInfo(string[] csvRow) {
            return (int.TryParse(csvRow[10], NumberStyles.Any, CultureInfo.InvariantCulture, out var id) ? id : null, csvRow[18]);
        }
    }

    public enum LdevType {
        Basic,
        Dynamic,
        External,
        Unknown,
    }

    public class Pool {
        public int Id { get; set; }
        public string Name { get; set; }
        public HashSet<string> EccGroups { get; set; }

        public Pool(int id, string name, params string[] eccGroups)
        {
            Id = id;
            Name = name;
            EccGroups = new HashSet<string>(eccGroups ?? Enumerable.Empty<string>());
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