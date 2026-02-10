using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IInventoryInboundService
    {
        Task<InboundRequest> CreateInboundRequestAsync(CreateInboundRequestRequest request);

        Task<InboundRequest> UpdateInboundRequestStatusAsync(int ticketRequestId, int approverId, string status);

        Task<InboundOrder> CreateTicketFromRequestAsync(int inboundRequestId, int createdBy, int? staffId);

        Task<InboundOrder> UpdateTicketItemsAsync(int inboundOrderId, IEnumerable<UpdateInboundOrderItemRequest> items);
        Task<List<InboundRequestDto>> GetAllInboundRequestsAsync(int companyId);
        Task<List<InboundOrderDto>> GetAllInboundOrdersAsync(int companyId);
        Task<InboundRequestDto> GetInboundRequestByIdAsync(int companyId, int id);
        Task<InboundOrderDto> GetInboundOrderByIdAsync(int companyId, int id);
        Task<List<InboundOrderDto>> GetInboundOrdersByStaffAsync(int companyId, int staffId);
        Task<InboundRequestExportDto> GetInboundRequestForExportAsync(int inboundRequestId);
        Task<InboundOrderExportDto> GetInboundOrderForExportAsync(int inboundOrderId);

        byte[] ExportInboundRequestToCsv(InboundRequestExportDto request);
        byte[] ExportInboundRequestToExcel(InboundRequestExportDto request);

        byte[] ExportInboundOrderToCsv(InboundOrderExportDto order);
        byte[] ExportInboundOrderToExcel(InboundOrderExportDto order);
    }
    public sealed record SupplierDto(int Id, string? Name, string? Phone, string? Email);

    public sealed record WarehouseDto(int Id, string? Name, string? Address, string? Description, int? Width, int? Height, int? Length);

    public sealed record UserDto(int Id, string? FullName, string? Email, string? Phone);
    public sealed record InboundOrderItemDto(
        int Id,
        int? ProductId,
        string? Sku,
        string? Name,
        double? Price,
        double? LineDiscount,
        int? ExpectedQuantity,
        int? TypeId,
        string? Description);
    public sealed record InboundRequestDto(
        int Id,
        int? WarehouseId,
        int? SupplierId,
        int? RequestedBy,
        int? ApprovedBy,
        string? Status,
        double? TotalPrice,
        double? OrderDiscount,
        double? FinalPrice,
        string? Code,
        string? Note,
        DateOnly? ExpectedArrivalDate,
        DateTime? CreatedAt,
        DateTime? ApprovedAt,
        IEnumerable<InboundOrderItemDto> InboundOrderItems,
        SupplierDto? Supplier,
        WarehouseDto? Warehouse,
        UserDto? RequestedByUser,
        UserDto? ApprovedByUser);

    public sealed record InboundOrderDto(
        int Id,
        int? InboundRequestId,
        int? WarehouseId,
        int? SupplierId,
        int? CreatedBy,
        int? StaffId,
        string? ReferenceCode,
        string? Status,        
        double? TotalPrice,
        double? OrderDiscount,
        double? FinalPrice,
        DateTime? CreatedAt,
        IEnumerable<InboundOrderItemDto> InboundOrderItems,
        SupplierDto? Supplier,
        WarehouseDto? Warehouse,
        UserDto? CreatedByUser,
        UserDto? StaffUser);
    public sealed record CreateInboundOrderItemRequest(int ProductId, int ExpectedQuantity, double Price, double LineDiscount);

    public sealed record CreateInboundRequestRequest(
        int? WarehouseId,
        int? SupplierId,
        int RequestedBy,
        string? Note,
        DateOnly? ExpectedArrivalDate,
        double? OrderDiscount,
        IEnumerable<CreateInboundOrderItemRequest> Items);

    public sealed record UpdateInboundRequestStatusRequest(int ApproverId, string Status);
    public sealed record UpdateInboundOrderItemRequest(int Id, int ProductId, int? ExpectedQuantity, int? ReceivedQuantity);
}
