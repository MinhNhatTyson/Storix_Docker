using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.DTO
{
    public class ProductExportDto
    {
        public int Id { get; set; }
        public string? Sku { get; set; }
        public string? Name { get; set; }
        public string? Category { get; set; }
        public string? Unit { get; set; }
        public double? Weight { get; set; }
        public string? CompanyName { get; set; }
        public string? ProductType { get; set; }
        public string? Description { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
