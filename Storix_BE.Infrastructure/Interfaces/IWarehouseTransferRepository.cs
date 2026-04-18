using Storix_BE.Domain.Models;

namespace Storix_BE.Repository.Interfaces
{
    public interface IWarehouseTransferRepository
    {
        Task<TransferOrder> CreateTransferOrderAsync(TransferOrder order);
        Task SaveChangesAsync();
        Task<TransferOrder?> GetTransferOrderWithWarehousesAsync(int transferOrderId);
        Task<TransferOrder?> GetTransferOrderDetailAsync(int transferOrderId);
        Task<List<TransferOrder>> GetTransferOrdersByCompanyAsync(int companyId, int? sourceWarehouseId, int? destinationWarehouseId, string? status);

        Task<TransferOrderItem?> GetTransferOrderItemAsync(int transferOrderId, int itemId);
        Task<TransferOrderItem?> GetTransferOrderItemByProductAsync(int transferOrderId, int productId);
        void AddTransferOrderItem(TransferOrderItem item);
        void RemoveTransferOrderItem(TransferOrderItem item);
        Task<bool> AnyTransferItemsAsync(int transferOrderId);
        Task<List<TransferOrderItem>> GetTransferItemsByOrderIdAsync(int transferOrderId);
        Task<List<TransferOrderItem>> GetTransferItemsWithProductByOrderIdAsync(int transferOrderId);

        Task<List<Inventory>> GetInventoriesByWarehouseAndProductsAsync(int? warehouseId, IReadOnlyCollection<int> productIds);
        Task<Inventory?> GetInventoryAsync(int? warehouseId, int productId);
        void AddInventory(Inventory inventory);

        Task<User?> GetUserByIdAsync(int userId);
        Task<bool> IsStaffAssignedToWarehouseAsync(int userId, int warehouseId);
        Task<Warehouse?> GetWarehouseByIdAsync(int warehouseId);
        Task<bool> ProductInCompanyAsync(int productId, int companyId);

        Task AddActivityAsync(int userId, string action, int transferOrderId, DateTime? timestamp = null);
        Task<List<ActivityLog>> GetActivitiesAsync(int transferOrderId);
        Task<string?> GetLatestActivityActionAsync(int transferOrderId, string prefix);

        Task<List<User>> GetAssignedStaffInWarehouseAsync(int warehouseId, int companyId);
        Task<int> CountWarehouseAssignmentsByUserAsync(int userId);
        Task<List<int>> GetTransferOrderIdsByCarrierAsync(int staffUserId);
        Task<int> CountActiveTransfersByOrderIdsAsync(int companyId, IReadOnlyCollection<int> orderIds, IReadOnlyCollection<string> activeStatuses);

        Task<OutboundOrder?> GetOutboundOrderWithItemsAsync(int outboundOrderId);

        Task ApproveTransferAsync(
            TransferOrder order,
            IReadOnlyCollection<TransferOrderItem> items,
            IReadOnlyCollection<Inventory> sourceInventories,
            int actorUserId,
            int? receiverStaffId,
            int? carrierId);

        Task ShipTransferAsync(TransferOrder order);

        Task ReceiveTransferAsync(
            TransferOrder order,
            IReadOnlyCollection<(int ProductId, int ReceivedQuantity)> receiveLines,
            IReadOnlyDictionary<int, int> requiredByProduct);

        Task<int> BackfillTransferItemLinksAsync(int transferOrderId, int? outboundTicketId, int? inboundTicketId);
    }
}
