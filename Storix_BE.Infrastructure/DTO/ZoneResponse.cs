using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.DTO
{
    public class ZoneResponse
    {
        public int Id { get; set; }
        public string Code { get; set; } = null!;
        public bool? IsEsd { get; set; }
        public bool? IsMsd { get; set; }
        public bool? IsCold { get; set; }
        public bool? IsVulnerable { get; set; }
        public bool? IsHighValue { get; set; }
        public double ? Width { get; set; }
        public double ? Height { get; set; }
        public double ? Length { get; set; }
    }
}
