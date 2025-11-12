using System;

namespace data_foundry.Models
{
    public class DatabaseChange
    {
        public string Type { get; set; }
        public string ObjectName { get; set; }
        public string Schema { get; set; }
        public string ChangeType { get; set; }
        public string ModifiedDate { get; set; }
    }
}
