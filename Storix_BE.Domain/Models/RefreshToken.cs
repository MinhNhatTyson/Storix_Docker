using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Models
{
    public partial class RefreshToken
    {
        public int Id { get; set; }

        public int? UserId { get; set; }

        public string? Token { get; set; }

        public DateTime? ExpiredAt { get; set; }

        public bool? IsRevoked { get; set; }

        public DateTime? CreatedAt { get; set; }

        public virtual User? User { get; set; }
    }
}
