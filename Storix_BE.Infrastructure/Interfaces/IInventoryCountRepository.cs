using Storix_BE.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IInventoryCountRepository
    {
        Task<InventoryCountsTicket> CreateStockCountTicketAsync(InventoryCountsTicket ticket);
        Task<InventoryCountsTicket> UpdateStockCountTicketStatusAsync(int ticketId, int approverId, string status);
        Task<InventoryCountsTicket> GetStockCountTicketByIdAsync(int companyId, int id);
        Task<List<InventoryCountsTicket>> GetStockCountTicketsByCompanyAsync(int companyId);
        Task<List<InventoryCountsTicket>> GetStockCountTicketsByStaffAsync(int companyId, int staffId);
        Task<InventoryCountsTicket> UpdateStockCountItemsAsync(int ticketId, IEnumerable<InventoryCountItem> items, int performedBy);
        Task<List<InventoryCountsTicket>> GetStockCountTicketsByWarehouseAsync(int companyId, int warehouseId);
    }
}
