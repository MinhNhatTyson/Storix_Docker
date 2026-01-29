using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.DTO
{
    public class UpdateProfileDto
    {
        public int? CompanyId { get; set; }

        public string? FullName { get; set; }

        public string? Email { get; set; }

        public string? Phone { get; set; }
        public string? PasswordHash { get; set; }
    }
}
