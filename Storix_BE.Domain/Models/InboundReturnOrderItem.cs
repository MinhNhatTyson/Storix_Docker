using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Models
{
    public partial class InboundReturnOrderItem
    {
        public int Id { get; set; }

        /// <summary>FK → inbound_return_orders.id</summary>
        public int ReturnOrderId { get; set; }

        /// <summary>FK → inbound_order_items.id — the original line item that was inspected.</summary>
        public int InboundOrderItemId { get; set; }

        /// <summary>
        /// FK → inbound_quality_checks.id — the QC record whose FailedQuantity
        /// is the source of units being returned.
        /// </summary>
        public int QualityCheckId { get; set; }

        /// <summary>FK → products.id — denormalised for query convenience.</summary>
        public int? ProductId { get; set; }

        /// <summary>
        /// Number of failed units being returned.
        /// Must be > 0 and ≤ InboundQualityCheck.FailedQuantity.
        /// </summary>
        public int ReturnQuantity { get; set; }

        /// <summary>
        /// Failure reason for this line — copied from the QC record by default
        /// but can be overridden by the staff member when creating the return order.
        /// </summary>
        public string? FailureReason { get; set; }

        // ── Navigation properties ────────────────────────────────────────────────

        public virtual InboundReturnOrder ReturnOrder { get; set; } = null!;

        public virtual InboundOrderItem InboundOrderItem { get; set; } = null!;

        public virtual InboundQualityCheck QualityCheck { get; set; } = null!;

        public virtual Product? Product { get; set; }
    }
}
