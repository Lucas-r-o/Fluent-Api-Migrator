using System.Collections.Generic;

namespace fluent_api_migrator.Models
{
    public class CommonEntityInfo
    {
        public List<string> PrimaryKeys { get; set; }
        public string Table { get; set; }
        public string Schema { get; set; }
    }
}
