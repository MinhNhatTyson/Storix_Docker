using Storix_BE.Domain.Models;
using Storix_BE.Repository.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IWarehouseAssignmentService
    {
        Task<List<Warehouse>> GetWarehousesByCompanyAsync(int companyId, int callerRoleId);
        Task<List<WarehouseAssignment>> GetAssignmentsByCompanyAsync(int companyId, int callerRoleId);
        Task<List<WarehouseAssignment>> GetAssignmentsByWarehouseAsync(int companyId, int callerRoleId, int warehouseId);
        Task<WarehouseAssignment> AssignWarehouseAsync(int companyId, int callerRoleId, AssignWarehouseRequest request);
        Task<bool> UnassignWarehouseAsync(int companyId, int callerRoleId, int userId, int warehouseId);
        Task<int> CountAssignmentsByUserAsync(int userId);
        Task<int> UpdateRoleInAssignmentsAsync(int userId, string roleInWarehouse);
        Task<Warehouse> CreateSimpleWarehouseAsync(int companyId, CreateSimpleWarehouseRequest request);
        Task<Warehouse> UpdateWarehouseStructureAsync(int companyId, int warehouseId, CreateWarehouseRequest request);

        Task<Warehouse> GetWarehouseStructureAsync(int companyId, int warehouseId);
        Task<bool> DeleteWarehouseAsync(int companyId, int warehouseId);
        Task<List<ZoneResponse>> GetZoneIdsByWarehouseAsync(int companyId, int warehouseId);
        Task<bool> DisableWarehouseAsync(int warehouseId);
    }

    public sealed record AssignWarehouseRequest(int UserId, int WarehouseId);
    public sealed record CreateWarehouseRequest(
        double? Width,
        double? Height,
        double? Length,
        IEnumerable<ZoneDto>? Zones,
        IEnumerable<NodeDto>? Nodes,
        IEnumerable<EdgeDto>? Edges);
    public sealed record CreateSimpleWarehouseRequest(
        string? Name,
        string? Address,
        string? Description,
        string? Status,
        int? AssignedManagerUserId);

    public sealed record ZoneDto(
        string? Id,
        string? Code,
        double? X,
        double? Y,
        double? Width,
        double? Height,
        double? Length,
        bool? isESD,
        bool? isMSD,
        bool? isCold,
        bool? isVulnerable,
        bool? isHighValue,
        IEnumerable<ShelfDto>? Shelves);

    public sealed record ShelfDto(
        string? Id,
        string? Code,
        double? X,
        double? Y,
        double? Width,
        double? Height,
        double? Length,
        IEnumerable<AccessNodeDto>? AccessNodes,
        IEnumerable<LevelDto>? Levels);

    public sealed record AccessNodeDto(string? Id, string? Side, double? X, double? Y, double? Radius);

    public sealed record LevelDto(string? Id, string? Code, IEnumerable<BinDto>? Bins);

    public sealed record BinDto(string? Id, string? Code, string? Status, double? Width, double? Length, double? Height);

    public sealed record NodeDto(string? Id, double? X, double? Y, double? Radius, string? Side, string? Type);

    public sealed record EdgeDto(string? Id, string? From, string? To, double? Distance);

}
