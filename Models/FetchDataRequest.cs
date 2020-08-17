using System;
using System.Collections.Generic;

namespace SAApi.Models
{
    public sealed class FetchDataRequest
    {
        public string From { get; set; }
        public string To   { get; set; }

        public string[] Manipulations { get; set; }
    }
}