using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Enum
{
    public enum ProductPackageTypeCode
    {
        TR,     // Băng cuốn / Cuộn băng — Tape and Reel
        BOX,    // Hộp — Box
        TRAY,   // Khay nhựa — Tray
        BULK,   // Đóng gói rời — Loose / Bulk
        TUBE,   // Ống — Tube / Stick
        BAG,    // Túi — Bag
        BLST,   // Vỉ nhựa — Blister pack
        SPOOL,  // Cuộn chỉ — Spool
        BAR,    // Thanh / Thanh dài — Bar / Strip
        PKG     // Fallback — generic / unrecognised
    }
}
