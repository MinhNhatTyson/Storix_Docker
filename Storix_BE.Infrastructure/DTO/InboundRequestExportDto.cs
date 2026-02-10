using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.DTO
{
    public class InboundRequestExportDto
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? Warehouse { get; set; }
        public string? Supplier { get; set; }
        public string? RequestedBy { get; set; }
        public string? ApprovedBy { get; set; }
        public string? Status { get; set; }
        public double? TotalPrice { get; set; }
        public double? OrderDiscount { get; set; }
        public double? FinalPrice { get; set; }
        public DateOnly? ExpectedDate { get; set; }
        public string? Note { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public List<InboundOrderItemExportDto> Items { get; set; } = new();
    }
    public class InboundOrderExportDto
    {
        public int Id { get; set; }
        public string? ReferenceCode { get; set; }
        public string? Warehouse { get; set; }
        public string? Supplier { get; set; }
        public string? CreatedBy { get; set; }
        public string? Staff { get; set; }
        public string? Status { get; set; }
        public double? TotalPrice { get; set; }
        public DateTime? CreatedAt { get; set; }
        public List<InboundOrderItemExportDto> Items { get; set; } = new();
    }
    public class InboundOrderItemExportDto
    {
        public int? ProductId { get; set; }
        public string? Sku { get; set; }
        public string? Name { get; set; }
        public double? Price { get; set; }
        public double? Discount { get; set; }
        public int? ExpectedQuantity { get; set; }
        public int? ReceivedQuantity { get; set; }
        public int? TypeId { get; set; }
        public string? Description { get; set; }
    }
}
