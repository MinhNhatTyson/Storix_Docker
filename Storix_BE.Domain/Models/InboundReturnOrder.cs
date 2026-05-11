using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Models
{
    public partial class InboundReturnOrder
    {
        public int Id { get; set; }

        /// <summary>FK → inbound_orders.id — the original inbound order that produced the failed units.</summary>
        public int InboundOrderId { get; set; }

        /// <summary>FK → suppliers.id — copied from the inbound order for convenience.</summary>
        public int? SupplierId { get; set; }

        /// <summary>FK → warehouses.id — copied from the inbound order.</summary>
        public int? WarehouseId { get; set; }

        /// <summary>
        /// PENDING  → staff flagged, awaiting manager approval
        /// APPROVED → manager approved, staff can ship
        /// SENT     → staff marked goods as physically shipped back
        /// </summary>
        public string Status { get; set; } = "PENDING";

        /// <summary>Manager's overall note / reason for approving or annotating the return.</summary>
        public string? Reason { get; set; }

        /// <summary>FK → users.id — the staff member who created this return order.</summary>
        public int CreatedBy { get; set; }

        /// <summary>FK → users.id — the manager who approved it. Null until approved.</summary>
        public int? ApprovedBy { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ApprovedAt { get; set; }

        /// <summary>Timestamp when staff marked the physical goods as shipped back.</summary>
        public DateTime? SentAt { get; set; }

        // ── Navigation properties ────────────────────────────────────────────────

        public virtual InboundOrder InboundOrder { get; set; } = null!;

        public virtual Supplier? Supplier { get; set; }

        public virtual Warehouse? Warehouse { get; set; }

        public virtual User CreatedByNavigation { get; set; } = null!;

        public virtual User? ApprovedByNavigation { get; set; }

        public virtual ICollection<InboundReturnOrderItem> ReturnOrderItems { get; set; }
            = new List<InboundReturnOrderItem>();
    }
}
