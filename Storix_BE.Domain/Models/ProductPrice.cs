using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Models
{
    public partial class ProductPrice
    {
        public int Id { get; set; }

        public int? ProductId { get; set; }

        public double? Price { get; set; }

        public double? LineDiscount { get; set; }

        public DateOnly? Date { get; set; }

        public virtual Product? Product { get; set; }
    }

}
