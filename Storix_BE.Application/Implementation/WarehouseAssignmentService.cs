using Storix_BE.Domain.Exception;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class WarehouseAssignmentService : IWarehouseAssignmentService
    {
        private readonly IUserRepository _userRepository;
        private readonly IWarehouseAssignmentRepository _assignmentRepository;
        private readonly IConfiguration _configuration;
        private readonly IActivityLogRepository _activityLogRepo;

        public WarehouseAssignmentService(
            IUserRepository userRepository,
            IWarehouseAssignmentRepository assignmentRepository,
            IConfiguration configuration,
            IActivityLogRepository activityLogRepo)
        {
            _userRepository = userRepository;
            _assignmentRepository = assignmentRepository;
            _configuration = configuration;
            _activityLogRepo = activityLogRepo;
        }

        private static void EnsureCompanyAdministratorAsync(int callerRoleId)
        {
            if (callerRoleId != 2)
                throw new UnauthorizedAccessException("Only Company Administrator can assign warehouses.");
        }

        private static bool IsInactiveStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            return status.Equals("inactive", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("deactivated", StringComparison.OrdinalIgnoreCase);
        }

        private int? GetMaxUsersPerWarehouse()
        {
            var value = _configuration.GetValue<int?>("Policy:MaxUsersPerWarehouse");
            return value.HasValue && value.Value > 0 ? value.Value : null;
        }

        public async Task<List<Warehouse>> GetWarehousesByCompanyAsync(int companyId, int callerRoleId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            return await _assignmentRepository.GetWarehousesByCompanyIdAsync(companyId);
        }

        public async Task<List<WarehouseAssignment>> GetAssignmentsByCompanyAsync(int companyId, int callerRoleId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            return await _assignmentRepository.GetAssignmentsByCompanyIdAsync(companyId);
        }

        public async Task<List<WarehouseAssignment>> GetAssignmentsByWarehouseAsync(int companyId, int callerRoleId, int warehouseId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");

            var warehouse = await _assignmentRepository.GetWarehouseByIdAsync(warehouseId);
            if (warehouse == null)
                throw new BusinessRuleException("BR-WH-01", "Warehouse not found.");
            if (warehouse.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

            var assignments = await _assignmentRepository.GetAssignmentsByWarehouseIdAsync(warehouseId);

            // Only return Manager/Staff assignments
            var result = new List<WarehouseAssignment>();
            foreach (var assignment in assignments)
            {
                var role = await _userRepository.GetRoleByIdAsync(assignment.User?.RoleId ?? 0);
                if (role?.Name == "Manager" || role?.Name == "Staff")
                    result.Add(assignment);
            }

            return result;
        }

        public async Task<WarehouseAssignment> AssignWarehouseAsync(int companyId, int callerRoleId, AssignWarehouseRequest request)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");

            var user = await _userRepository.GetUserByIdWithRoleAsync(request.UserId);
            if (user == null)
                throw new InvalidOperationException("User not found.");
            if (user.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

            var userRole = await _userRepository.GetRoleByIdAsync(user.RoleId ?? 0);
            if (userRole?.Name == "Super Admin")
                throw new InvalidOperationException("Cannot assign warehouse to Super Admin.");
            if (userRole?.Name == "Company Administrator")
                throw new InvalidOperationException("Cannot assign warehouse to Company Administrator.");
            if (userRole?.Name != "Manager" && userRole?.Name != "Staff")
                throw new BusinessRuleException("BR-WH-03", "Role not eligible for warehouse assignment.");

            var warehouse = await _assignmentRepository.GetWarehouseByIdAsync(request.WarehouseId);
            if (warehouse == null)
                throw new BusinessRuleException("BR-WH-01", "Warehouse not found.");
            if (warehouse.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");
            if (IsInactiveStatus(warehouse.Status))
                throw new BusinessRuleException("BR-WH-02", "Warehouse is inactive.");

            var existingWarehouse = await _assignmentRepository.GetAssignmentAsync(request.UserId, request.WarehouseId);
            if (existingWarehouse != null)
                throw new BusinessRuleException("BR-WH-04", "User already assigned to this warehouse.");

            var maxUsers = GetMaxUsersPerWarehouse();
            if (maxUsers.HasValue)
            {
                var currentCount = await _assignmentRepository.CountAssignmentsByWarehouseIdAsync(request.WarehouseId);
                if (currentCount >= maxUsers.Value)
                    throw new BusinessRuleException("BR-WH-05", "Warehouse capacity policy exceeded.");
            }

            var assignment = new WarehouseAssignment
            {
                UserId = request.UserId,
                WarehouseId = request.WarehouseId,
                RoleInWarehouse = userRole?.Name,
                AssignedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            await _assignmentRepository.AddAssignmentAsync(assignment);
            return assignment;
        }

        public async Task<bool> UnassignWarehouseAsync(int companyId, int callerRoleId, int userId, int warehouseId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");

            var assignment = await _assignmentRepository.GetAssignmentAsync(userId, warehouseId);
            if (assignment == null)
                return false;

            if (assignment.Warehouse?.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

            if (assignment.User?.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

            var hasActiveTasks = await _assignmentRepository.HasActiveWarehouseOperationsAsync(userId, warehouseId);
            if (hasActiveTasks)
                throw new BusinessRuleException("BR-WH-06", "User has active tasks in warehouse.");

            var userRole = await _userRepository.GetRoleByIdAsync(assignment.User?.RoleId ?? 0);
            if (userRole?.Name == "Manager")
            {
                var assignmentCount = await _assignmentRepository.CountAssignmentsByUserIdAsync(userId);
                if (assignmentCount <= 1)
                    throw new BusinessRuleException("BR-WH-07", "Manager must have at least one warehouse.");
            }

            await _assignmentRepository.RemoveAssignmentAsync(assignment);
            return true;
        }

        public async Task<int> CountAssignmentsByUserAsync(int userId)
        {
            if (userId <= 0) throw new InvalidOperationException("Invalid user id.");
            return await _assignmentRepository.CountAssignmentsByUserIdAsync(userId);
        }

        public async Task<int> UpdateRoleInAssignmentsAsync(int userId, string roleInWarehouse)
        {
            if (userId <= 0) throw new InvalidOperationException("Invalid user id.");
            if (string.IsNullOrWhiteSpace(roleInWarehouse))
                throw new InvalidOperationException("Role in warehouse is required.");

            return await _assignmentRepository.UpdateRoleInAssignmentsAsync(userId, roleInWarehouse);
        }
        public async Task<Warehouse> CreateSimpleWarehouseAsync(int companyId, CreateSimpleWarehouseRequest request)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            if (request == null) throw new InvalidOperationException("Request is required.");

            var warehouse = new Warehouse
            {
                CompanyId = companyId,
                Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name,
                Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description,
                Status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            var created = await _assignmentRepository.CreateWarehouseAsync(warehouse);

            // If assigned manager provided, validate and create assignment
            if (request.AssignedManagerUserId.HasValue && request.AssignedManagerUserId.Value > 0)
            {
                var user = await _userRepository.GetUserByIdWithRoleAsync(request.AssignedManagerUserId.Value);
                if (user == null) throw new InvalidOperationException("Assigned manager user not found.");
                if (user.CompanyId != companyId) throw new BusinessRuleException("BR-WH-08", "Cross-company assignment is not allowed.");

                var userRole = await _userRepository.GetRoleByIdAsync(user.RoleId ?? 0);
                if (userRole?.Name == "Super Admin")
                    throw new InvalidOperationException("Cannot assign warehouse to Super Admin.");
                if (userRole?.Name == "Company Administrator")
                    throw new InvalidOperationException("Cannot assign warehouse to Company Administrator.");
                if (userRole?.Name != "Manager" && userRole?.Name != "Staff")
                    throw new BusinessRuleException("BR-WH-03", "Role not eligible for warehouse assignment.");

                // Optional policy check: Max users per warehouse
                var maxUsers = GetMaxUsersPerWarehouse();
                if (maxUsers.HasValue)
                {
                    var currentCount = await _assignmentRepository.CountAssignmentsByWarehouseIdAsync(created.Id);
                    if (currentCount >= maxUsers.Value)
                        throw new BusinessRuleException("BR-WH-05", "Warehouse capacity policy exceeded.");
                }

                var assignment = new WarehouseAssignment
                {
                    UserId = request.AssignedManagerUserId.Value,
                    WarehouseId = created.Id,
                    RoleInWarehouse = userRole?.Name,
                    AssignedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };

                await _assignmentRepository.AddAssignmentAsync(assignment);
            }
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = request.AssignedManagerUserId,
                Action = "Create Warehouse",
                Entity = "Warehouse",
                EntityId = created.Id,
                Timestamp = now
            }).ConfigureAwait(false);

            return created;
        }

        // New: update an existing warehouse structure (nodes, edges, zones)
        public async Task<Warehouse> UpdateWarehouseStructureAsync(int companyId, int warehouseId, CreateWarehouseRequest request)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            if (warehouseId <= 0) throw new InvalidOperationException("Invalid warehouse id.");
            if (request == null) throw new InvalidOperationException("Request is required.");
            if (request.Width.HasValue && request.Width.Value < 0)
                throw new InvalidOperationException("Warehouse Width must be >= 0.");
            if (request.Height.HasValue && request.Height.Value < 0)
                throw new InvalidOperationException("Warehouse Height must be >= 0.");

            // Fetch existing warehouse
            var existingWarehouse = await _assignmentRepository.GetWarehouseByIdAsync(warehouseId);
            if (existingWarehouse == null)
                throw new BusinessRuleException("BR-WH-01", "Warehouse not found.");
            if (existingWarehouse.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company access is not allowed.");
            if (IsInactiveStatus(existingWarehouse.Status))
                throw new BusinessRuleException("BR-WH-02", "Warehouse is inactive.");

            // Validate & build in-memory structure (similar validations from previous create flow)
            var zoneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shelfIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var levelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var binIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var edgeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var warehouseStructure = new Warehouse
            {
                Width = request.Width.HasValue ? Convert.ToInt32(Math.Round(request.Width.Value)) : null,
                Height = request.Height.HasValue ? Convert.ToInt32(Math.Round(request.Height.Value)) : null,
                Length = request.Length.HasValue ? Convert.ToInt32(Math.Round(request.Length.Value)) : null,
                // keep CompanyId null here — repository update will attach to existing warehouse
            };

            var nodeDict = new Dictionary<string, NavNode?>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

            if (request.Nodes != null)
            {
                foreach (var n in request.Nodes)
                {
                    if (n == null) throw new InvalidOperationException("Node entry cannot be null.");
                    if (string.IsNullOrWhiteSpace(n.Id)) throw new InvalidOperationException("Node Id is required.");
                    if (!nodeIds.Add(n.Id)) throw new InvalidOperationException($"Duplicate Node IdCode detected: '{n.Id}'");
                    if (!n.X.HasValue || !n.Y.HasValue) throw new InvalidOperationException($"Node '{n.Id}' X and Y coordinates are required.");
                    if (n.X.Value < 0 || n.Y.Value < 0) throw new InvalidOperationException($"Node '{n.Id}' coordinates must be >= 0.");
                    if (n.Radius.HasValue && n.Radius.Value < 0) throw new InvalidOperationException($"Node '{n.Id}' radius must be >= 0.");

                    var navNode = new NavNode
                    {
                        IdCode = n.Id,
                        XCoordinate = Convert.ToInt32(Math.Round(n.X.Value)),
                        YCoordinate = Convert.ToInt32(Math.Round(n.Y.Value)),
                        Radius = n.Radius,
                        Side = n.Side,
                        Type = n.Type,
                    };
                    nodeDict[n.Id] = navNode;
                    warehouseStructure.NavNodes.Add(navNode);
                }
            }

            if (request.Zones != null)
            {
                foreach (var z in request.Zones)
                {
                    if (z == null) throw new InvalidOperationException("Zone entry cannot be null.");
                    if (string.IsNullOrWhiteSpace(z.Id)) throw new InvalidOperationException("Zone Id is required.");
                    if (!zoneIds.Add(z.Id)) throw new InvalidOperationException($"Duplicate Zone IdCode detected: '{z.Id}'");
                    if (z.Width.HasValue && z.Width.Value < 0) throw new InvalidOperationException($"Zone '{z.Id}' width must be >= 0.");
                    if (z.Height.HasValue && z.Height.Value < 0) throw new InvalidOperationException($"Zone '{z.Id}' height must be >= 0.");
                    if (z.Length.HasValue && z.Length.Value < 0) throw new InvalidOperationException($"Zone '{z.Id}' length must be >= 0.");
                    if (z.X.HasValue && z.X.Value < 0) throw new InvalidOperationException($"Zone '{z.Id}' X must be >= 0.");
                    if (z.Y.HasValue && z.Y.Value < 0) throw new InvalidOperationException($"Zone '{z.Id}' Y must be >= 0.");

                    var zone = new StorageZone
                    {
                        IdCode = z.Id,
                        Code = z.Code,
                        Width = z.Width,
                        Height = z.Height,
                        Length = z.Length,
                        XCoordinate = z.X,
                        YCoordinate = z.Y,
                        IsEsd = z.isESD,
                        IsMsd = z.isMSD,
                        IsCold = z.isCold,
                        IsVulnerable = z.isVulnerable,
                        IsHighValue = z.isHighValue,
                        CreatedAt = now
                    };
                    warehouseStructure.StorageZones.Add(zone);

                    if (z.Shelves != null)
                    {
                        foreach (var s in z.Shelves)
                        {
                            if (s == null) throw new InvalidOperationException("Shelf entry cannot be null.");
                            if (string.IsNullOrWhiteSpace(s.Id)) throw new InvalidOperationException("Shelf Id is required.");
                            if (!shelfIds.Add(s.Id)) throw new InvalidOperationException($"Duplicate Shelf IdCode detected: '{s.Id}'");
                            if (string.IsNullOrWhiteSpace(s.Code)) throw new InvalidOperationException($"Shelf '{s.Id}' Code is required.");
                            if (s.X.HasValue && s.X.Value < 0) throw new InvalidOperationException($"Shelf '{s.Id}' X must be >= 0.");
                            if (s.Y.HasValue && s.Y.Value < 0) throw new InvalidOperationException($"Shelf '{s.Id}' Y must be >= 0.");
                            if (s.Width.HasValue && s.Width.Value < 0) throw new InvalidOperationException($"Shelf '{s.Id}' width must be >= 0.");
                            if (s.Height.HasValue && s.Height.Value < 0) throw new InvalidOperationException($"Shelf '{s.Id}' height must be >= 0.");

                            var shelf = new Shelf
                            {
                                IdCode = s.Id,
                                Code = s.Code,
                                XCoordinate = s.X.HasValue ? Convert.ToInt32(Math.Round(s.X.Value)) : null,
                                YCoordinate = s.Y.HasValue ? Convert.ToInt32(Math.Round(s.Y.Value)) : null,
                                Width = s.Width.HasValue ? Convert.ToInt32(Math.Round(s.Width.Value)) : null,
                                Height = s.Height.HasValue ? Convert.ToInt32(Math.Round(s.Height.Value)) : null,
                                Length = s.Length.HasValue ? Convert.ToInt32(Math.Round(s.Length.Value)) : null,
                                CreatedAt = now
                            };
                            zone.Shelves.Add(shelf);

                            // Access nodes for shelf
                            if (s.AccessNodes != null)
                            {
                                foreach (var acc in s.AccessNodes)
                                {
                                    if (acc == null) throw new InvalidOperationException("AccessNode entry cannot be null.");
                                    if (string.IsNullOrWhiteSpace(acc.Id)) throw new InvalidOperationException("AccessNode Id is required.");
                                    if (!nodeIds.Add(acc.Id))
                                    {
                                        if (!nodeDict.ContainsKey(acc.Id))
                                            throw new InvalidOperationException($"Duplicate AccessNode IdCode detected: '{acc.Id}'");
                                    }

                                    if (acc.X.HasValue && acc.X.Value < 0) throw new InvalidOperationException($"AccessNode '{acc.Id}' X must be >= 0.");
                                    if (acc.Y.HasValue && acc.Y.Value < 0) throw new InvalidOperationException($"AccessNode '{acc.Id}' Y must be >= 0.");
                                    if (acc.Radius.HasValue && acc.Radius.Value < 0) throw new InvalidOperationException($"AccessNode '{acc.Id}' radius must be >= 0.");

                                    NavNode existingNode;
                                    if (!nodeDict.TryGetValue(acc.Id, out var existing) || existing == null)
                                    {
                                        existingNode = new NavNode
                                        {
                                            IdCode = acc.Id,
                                            XCoordinate = acc.X.HasValue ? Convert.ToInt32(Math.Round(acc.X.Value)) : null,
                                            YCoordinate = acc.Y.HasValue ? Convert.ToInt32(Math.Round(acc.Y.Value)) : null,
                                            Radius = acc.Radius,
                                            Side = acc.Side,
                                        };
                                        nodeDict[acc.Id] = existingNode;
                                        warehouseStructure.NavNodes.Add(existingNode);
                                    }
                                    else
                                    {
                                        existingNode = existing;
                                    }

                                    var shelfNode = new ShelfNode
                                    {
                                        Shelf = shelf,
                                        Node = existingNode,
                                        IdCode = acc.Id
                                    };
                                    shelf.ShelfNodes.Add(shelfNode);
                                }
                            }

                            // Levels and bins
                            if (s.Levels != null)
                            {
                                foreach (var lvl in s.Levels)
                                {
                                    if (lvl == null) throw new InvalidOperationException("Level entry cannot be null.");
                                    if (string.IsNullOrWhiteSpace(lvl.Id)) throw new InvalidOperationException("Level Id is required.");
                                    if (!levelIds.Add(lvl.Id)) throw new InvalidOperationException($"Duplicate Level IdCode detected: '{lvl.Id}'");
                                    if (string.IsNullOrWhiteSpace(lvl.Code)) throw new InvalidOperationException($"Level '{lvl.Id}' Code is required.");

                                    var level = new ShelfLevel
                                    {
                                        IdCode = lvl.Id,
                                        Code = lvl.Code
                                    };
                                    shelf.ShelfLevels.Add(level);

                                    if (lvl.Bins != null)
                                    {
                                        foreach (var b in lvl.Bins)
                                        {
                                            if (b == null) throw new InvalidOperationException("Bin entry cannot be null.");
                                            if (string.IsNullOrWhiteSpace(b.Id)) throw new InvalidOperationException("Bin Id is required.");
                                            if (!binIds.Add(b.Id)) throw new InvalidOperationException($"Duplicate Bin IdCode detected: '{b.Id}'");
                                            if (string.IsNullOrWhiteSpace(b.Code)) throw new InvalidOperationException($"Bin '{b.Id}' Code is required.");

                                            var bin = new ShelfLevelBin
                                            {
                                                IdCode = b.Id,
                                                Code = b.Code,
                                                Status = (b.Status?.ToLower() == "active")
                                                ? true
                                                        : (b.Status?.ToLower() == "inactive")
                                                            ? false
                                                            : (bool?)null,
                                                Width = b.Width.HasValue ? Convert.ToInt32(Math.Round(b.Width.Value)) : null,
                                                Height = b.Height.HasValue ? Convert.ToInt32(Math.Round(b.Height.Value)) : null,
                                                Length = b.Length.HasValue ? Convert.ToInt32(Math.Round(b.Length.Value)) : null,
                                            };
                                            level.ShelfLevelBins.Add(bin);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (request.Edges != null)
            {
                foreach (var e in request.Edges)
                {
                    if (e == null) throw new InvalidOperationException("Edge entry cannot be null.");
                    if (string.IsNullOrWhiteSpace(e.Id)) throw new InvalidOperationException("Edge Id is required.");
                    if (!edgeIds.Add(e.Id)) throw new InvalidOperationException($"Duplicate Edge IdCode detected: '{e.Id}'");
                    if (string.IsNullOrWhiteSpace(e.From) || string.IsNullOrWhiteSpace(e.To))
                        throw new InvalidOperationException($"Edge '{e.Id}' From and To are required.");
                    if (e.Distance.HasValue && e.Distance.Value < 0) throw new InvalidOperationException($"Edge '{e.Id}' distance must be >= 0.");

                    if (!nodeDict.TryGetValue(e.From, out var fromNode) || fromNode == null)
                        throw new InvalidOperationException($"Edge '{e.Id}' references unknown From node '{e.From}'.");
                    if (!nodeDict.TryGetValue(e.To, out var toNode) || toNode == null)
                        throw new InvalidOperationException($"Edge '{e.Id}' references unknown To node '{e.To}'.");

                    var edge = new NavEdge
                    {
                        IdCode = e.Id,
                        Distance = e.Distance,
                        NodeFromNavigation = fromNode,
                        NodeToNavigation = toNode
                    };

                    warehouseStructure.NavEdges.Add(edge);
                }
            }

            // Persist structure replacement via repository
            var updated = await _assignmentRepository.UpdateWarehouseStructureAsync(warehouseId, warehouseStructure);
            if (!updated)
                throw new System.Exception("Failed to update warehouse structure.");

            // Return warehouse with refreshed structure
            var result = await _assignmentRepository.GetWarehouseWithStructureAsync(warehouseId) ?? throw new System.Exception("Failed to load updated warehouse.");
            /*var time = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _activityLogRepo.AddAsync(new ActivityLog
            {
                UserId = null,
                Action = "Update Warehouse Structure",
                Entity = "Warehouse",
                EntityId = warehouseId,
                Timestamp = time
            }).ConfigureAwait(false);*/
            return result;
        }
        public async Task<Warehouse> GetWarehouseStructureAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            if (warehouseId <= 0) throw new InvalidOperationException("Invalid warehouse id.");

            var warehouse = await _assignmentRepository.GetWarehouseWithStructureAsync(warehouseId);
            if (warehouse == null)
                throw new BusinessRuleException("BR-WH-01", "Warehouse not found.");
            if (warehouse.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company access is not allowed.");

            return warehouse;
        }
        public async Task<bool> DeleteWarehouseAsync(int companyId, int warehouseId)
        {
            if (companyId <= 0) throw new InvalidOperationException("Invalid company id.");
            if (warehouseId <= 0) throw new InvalidOperationException("Invalid warehouse id.");

            var warehouse = await _assignmentRepository.GetWarehouseByIdAsync(warehouseId);
            if (warehouse == null)
                throw new BusinessRuleException("BR-WH-01", "Warehouse not found.");
            if (warehouse.CompanyId != companyId)
                throw new BusinessRuleException("BR-WH-08", "Cross-company access is not allowed.");

            // No special business rule preventing deletion specified; delegate to repository to remove structure and warehouse.
            var deleted = await _assignmentRepository.DeleteWarehouseAsync(warehouseId);
            if (!deleted)
                throw new Exception("Failed to delete warehouse.");

            return true;
        }
    }
}
