using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Models
{
    public static class InboundOrderStatuses
    {
        public const string WaitingAssignStaff = "Waiting Assign Staff";
        public const string WaitingForPayment = "Waiting for payment";
        public const string PartiallyCompleted = "Partially Completed";
        public const string Completed = "Completed";
    }
}
