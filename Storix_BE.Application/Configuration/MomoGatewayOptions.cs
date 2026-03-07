using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Configuration
{
    public sealed class MomoGatewayOptions
    {
        public string PartnerCode { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string PaymentUrl { get; set; } = string.Empty;
        public string QueryUrl { get; set; } = string.Empty;
        public bool IsProduction { get; set; }
    }
}

