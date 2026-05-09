using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Models
{
    public partial class InboundQualityCheck
    {
        public int Id { get; set; }

        /// <summary>FK → inbound_orders.id</summary>
        public int InboundOrderId { get; set; }

        /// <summary>FK → inbound_order_items.id</summary>
        public int InboundOrderItemId { get; set; }

        public int? ProductId { get; set; }

        /// <summary>Total physical units received from the supplier for this line.</summary>
        public int ReceivedQuantity { get; set; }

        /// <summary>Units that passed quality inspection — these will be placed into bins.</summary>
        public int PassedQuantity { get; set; }

        /// <summary>Units rejected during inspection (ReceivedQuantity - PassedQuantity).</summary>
        public int FailedQuantity { get; set; }

        /// <summary>
        /// Short description of the failure reason (e.g. "Damaged packaging", "Wrong specification").
        /// Nullable — not required when all units pass.
        /// </summary>
        public string? FailureReason { get; set; }

        /// <summary>Additional free-text notes from the inspector.</summary>
        public string? Notes { get; set; }

        /// <summary>FK → users.id — the staff member who performed the inspection.</summary>
        public int InspectedBy { get; set; }

        public DateTime InspectedAt { get; set; }

        // ── Navigation properties ────────────────────────────────────────────────

        public virtual InboundOrder InboundOrder { get; set; } = null!;

        public virtual InboundOrderItem InboundOrderItem { get; set; } = null!;

        public virtual Product? Product { get; set; }

        public virtual User? Inspector { get; set; }
    }
}
