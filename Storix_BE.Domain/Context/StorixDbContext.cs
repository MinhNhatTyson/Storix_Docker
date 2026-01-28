using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Models;

namespace Storix_BE.Domain.Context;

public partial class StorixDbContext : DbContext
{
    public StorixDbContext()
    {
    }

    public StorixDbContext(DbContextOptions<StorixDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ActivityLog> ActivityLogs { get; set; }

    public virtual DbSet<Company> Companies { get; set; }

    public virtual DbSet<InboundOrder> InboundOrders { get; set; }

    public virtual DbSet<InboundOrderItem> InboundOrderItems { get; set; }

    public virtual DbSet<InboundRequest> InboundRequests { get; set; }

    public virtual DbSet<Inventory> Inventories { get; set; }

    public virtual DbSet<InventoryLocation> InventoryLocations { get; set; }

    public virtual DbSet<InventoryTransaction> InventoryTransactions { get; set; }

    public virtual DbSet<NavEdge> NavEdges { get; set; }

    public virtual DbSet<NavNode> NavNodes { get; set; }

    public virtual DbSet<OutboundOrder> OutboundOrders { get; set; }

    public virtual DbSet<OutboundOrderItem> OutboundOrderItems { get; set; }

    public virtual DbSet<OutboundRequest> OutboundRequests { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Shelf> Shelves { get; set; }

    public virtual DbSet<ShelfLevel> ShelfLevels { get; set; }

    public virtual DbSet<ShelfLevelBin> ShelfLevelBins { get; set; }

    public virtual DbSet<ShelfNode> ShelfNodes { get; set; }

    public virtual DbSet<StockCountItem> StockCountItems { get; set; }

    public virtual DbSet<StockCountsTicket> StockCountsTickets { get; set; }

    public virtual DbSet<StorageForecast> StorageForecasts { get; set; }

    public virtual DbSet<StorageRecommendation> StorageRecommendations { get; set; }

    public virtual DbSet<StorageZone> StorageZones { get; set; }

    public virtual DbSet<Supplier> Suppliers { get; set; }

    public virtual DbSet<TransferOrder> TransferOrders { get; set; }

    public virtual DbSet<TransferOrderItem> TransferOrderItems { get; set; }

    public virtual DbSet<ProductType> Types { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<Warehouse> Warehouses { get; set; }

    public virtual DbSet<WarehouseAssignment> WarehouseAssignments { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("activity_logs_pkey");

            entity.ToTable("activity_logs");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action)
                .HasColumnType("character varying")
                .HasColumnName("action");
            entity.Property(e => e.Entity)
                .HasColumnType("character varying")
                .HasColumnName("entity");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.Timestamp)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("timestamp");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.ActivityLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_activity_logs_user_id");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("companies_pkey");

            entity.ToTable("companies");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasColumnType("character varying")
                .HasColumnName("address");
            entity.Property(e => e.BusinessCode)
                .HasColumnType("character varying")
                .HasColumnName("business_code");
            entity.Property(e => e.ContactEmail)
                .HasColumnType("character varying")
                .HasColumnName("contact_email");
            entity.Property(e => e.ContactPhone)
                .HasColumnType("character varying")
                .HasColumnName("contact_phone");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Name)
                .HasColumnType("character varying")
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");
            entity.Property(e => e.SubscriptionPlan)
                .HasColumnType("character varying")
                .HasColumnName("subscription_plan");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<InboundOrder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inbound_orders_pkey");

            entity.ToTable("inbound_orders");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.InboundRequestId).HasColumnName("inbound_request_id");
            entity.Property(e => e.ReferenceCode)
                .HasColumnType("character varying")
                .HasColumnName("reference_code");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");
            entity.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.InboundOrders)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_inbound_orders_created_by");

            entity.HasOne(d => d.InboundRequest).WithMany(p => p.InboundOrders)
                .HasForeignKey(d => d.InboundRequestId)
                .HasConstraintName("fk_inbound_orders_inbound_request_id");

            entity.HasOne(d => d.Supplier).WithMany(p => p.InboundOrders)
                .HasForeignKey(d => d.SupplierId)
                .HasConstraintName("fk_inbound_orders_supplier_id");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.InboundOrders)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_inbound_orders_warehouse_id");
        });

        modelBuilder.Entity<InboundOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inbound_order_items_pkey");

            entity.ToTable("inbound_order_items");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ExpectedQuantity).HasColumnName("expected_quantity");
            entity.Property(e => e.InboundOrderId).HasColumnName("inbound_order_id");
            entity.Property(e => e.InboundRequestId).HasColumnName("inbound_request_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.ReceivedQuantity).HasColumnName("received_quantity");

            entity.HasOne(d => d.InboundOrder).WithMany(p => p.InboundOrderItems)
                .HasForeignKey(d => d.InboundOrderId)
                .HasConstraintName("fk_inbound_order_items_inbound_order_id");

            entity.HasOne(d => d.InboundRequest).WithMany(p => p.InboundOrderItems)
                .HasForeignKey(d => d.InboundRequestId)
                .HasConstraintName("fk_inbound_order_items_inbound_request_id");

            entity.HasOne(d => d.Product).WithMany(p => p.InboundOrderItems)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("fk_inbound_order_items_product_id");
        });

        modelBuilder.Entity<InboundRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inbound_requests_pkey");

            entity.ToTable("inbound_requests");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ApprovedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("approved_at");
            entity.Property(e => e.ApprovedBy).HasColumnName("approved_by");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");
            entity.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.ApprovedByNavigation).WithMany(p => p.InboundRequestApprovedByNavigations)
                .HasForeignKey(d => d.ApprovedBy)
                .HasConstraintName("fk_inbound_requests_approved_by");

            entity.HasOne(d => d.RequestedByNavigation).WithMany(p => p.InboundRequestRequestedByNavigations)
                .HasForeignKey(d => d.RequestedBy)
                .HasConstraintName("fk_inbound_requests_requested_by");

            entity.HasOne(d => d.Supplier).WithMany(p => p.InboundRequests)
                .HasForeignKey(d => d.SupplierId)
                .HasConstraintName("fk_inbound_requests_supplier_id");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.InboundRequests)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_inbound_requests_warehouse_id");
        });

        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inventory_pkey");

            entity.ToTable("inventory");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.LastUpdated)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_updated");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.ReservedQuantity).HasColumnName("reserved_quantity");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.Product).WithMany(p => p.Inventories)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("fk_inventory_product_id");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.Inventories)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_inventory_warehouse_id");
        });

        modelBuilder.Entity<InventoryLocation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inventory_locations_pkey");

            entity.ToTable("inventory_locations");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.InventoryId).HasColumnName("inventory_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.ShelfId).HasColumnName("shelf_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Inventory).WithMany(p => p.InventoryLocations)
                .HasForeignKey(d => d.InventoryId)
                .HasConstraintName("fk_inventory_locations_inventory_id");

            entity.HasOne(d => d.Shelf).WithMany(p => p.InventoryLocations)
                .HasForeignKey(d => d.ShelfId)
                .HasConstraintName("fk_inventory_locations_shelf_id");
        });

        modelBuilder.Entity<InventoryTransaction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inventory_transactions_pkey");

            entity.ToTable("inventory_transactions");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.PerformedBy).HasColumnName("performed_by");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.QuantityChange).HasColumnName("quantity_change");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.TransactionType)
                .HasColumnType("character varying")
                .HasColumnName("transaction_type");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.PerformedByNavigation).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.PerformedBy)
                .HasConstraintName("fk_inventory_transactions_performed_by");

            entity.HasOne(d => d.Product).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("fk_inventory_transactions_product_id");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_inventory_transactions_warehouse_id");
        });

        modelBuilder.Entity<NavEdge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("nav_edge_pkey");

            entity.ToTable("nav_edge");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Distance).HasColumnName("distance");
            entity.Property(e => e.NodeFrom).HasColumnName("node_from");
            entity.Property(e => e.NodeTo).HasColumnName("node_to");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.NodeFromNavigation).WithMany(p => p.NavEdgeNodeFromNavigations)
                .HasForeignKey(d => d.NodeFrom)
                .HasConstraintName("fk_nav_edge_node_from");

            entity.HasOne(d => d.NodeToNavigation).WithMany(p => p.NavEdgeNodeToNavigations)
                .HasForeignKey(d => d.NodeTo)
                .HasConstraintName("fk_nav_edge_node_to");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.NavEdges)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_nav_edge_warehouse_id");
        });

        modelBuilder.Entity<NavNode>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("nav_node_pkey");

            entity.ToTable("nav_node");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Type)
                .HasColumnType("character varying")
                .HasColumnName("type");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.XCoordinate).HasColumnName("x_coordinate");
            entity.Property(e => e.YCoordinate).HasColumnName("y_coordinate");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.NavNodes)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_nav_node_warehouse_id");
        });

        modelBuilder.Entity<OutboundOrder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("outbound_orders_pkey");

            entity.ToTable("outbound_orders");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Destination)
                .HasColumnType("character varying")
                .HasColumnName("destination");
            entity.Property(e => e.Note)
                .HasColumnType("character varying")
                .HasColumnName("note");
            entity.Property(e => e.StaffId).HasColumnName("staff_id");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.OutboundOrders)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_outbound_orders_created_by");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.OutboundOrders)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_outbound_orders_warehouse_id");
        });

        modelBuilder.Entity<OutboundOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("outbound_order_items_pkey");

            entity.ToTable("outbound_order_items");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OutboundOrderId).HasColumnName("outbound_order_id");
            entity.Property(e => e.OutboundRequestId).HasColumnName("outbound_request_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");

            entity.HasOne(d => d.OutboundOrder).WithMany(p => p.OutboundOrderItems)
                .HasForeignKey(d => d.OutboundOrderId)
                .HasConstraintName("fk_outbound_order_items_outbound_order_id");

            entity.HasOne(d => d.OutboundRequest).WithMany(p => p.OutboundOrderItems)
                .HasForeignKey(d => d.OutboundRequestId)
                .HasConstraintName("fk_outbound_order_items_outbound_request_id");

            entity.HasOne(d => d.Product).WithMany(p => p.OutboundOrderItems)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("fk_outbound_order_items_product_id");
        });

        modelBuilder.Entity<OutboundRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("outbound_requests_pkey");

            entity.ToTable("outbound_requests");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ApprovedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("approved_at");
            entity.Property(e => e.ApprovedBy).HasColumnName("approved_by");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Destination)
                .HasColumnType("character varying")
                .HasColumnName("destination");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.ApprovedByNavigation).WithMany(p => p.OutboundRequestApprovedByNavigations)
                .HasForeignKey(d => d.ApprovedBy)
                .HasConstraintName("fk_outbound_requests_approved_by");

            entity.HasOne(d => d.RequestedByNavigation).WithMany(p => p.OutboundRequestRequestedByNavigations)
                .HasForeignKey(d => d.RequestedBy)
                .HasConstraintName("fk_outbound_requests_requested_by");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.OutboundRequests)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_outbound_requests_warehouse_id");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("products_pkey");

            entity.ToTable("products");

            entity.HasIndex(e => e.Sku, "products_sku_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Category)
                .HasColumnType("character varying")
                .HasColumnName("category");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasColumnType("character varying")
                .HasColumnName("name");
            entity.Property(e => e.Sku)
                .HasColumnType("character varying")
                .HasColumnName("sku");
            entity.Property(e => e.TypeId).HasColumnName("type_id");
            entity.Property(e => e.Unit)
                .HasColumnType("character varying")
                .HasColumnName("unit");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Weight).HasColumnName("weight");

            entity.HasOne(d => d.Company).WithMany(p => p.Products)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("fk_products_company_id");

            entity.HasOne(d => d.Type).WithMany(p => p.Products)
                .HasForeignKey(d => d.TypeId)
                .HasConstraintName("fk_products_type_id");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("role_pkey");

            entity.ToTable("role");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasColumnType("character varying")
                .HasColumnName("name");
        });

        modelBuilder.Entity<Shelf>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shelves_pkey");

            entity.ToTable("shelves");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Capacity).HasColumnName("capacity");
            entity.Property(e => e.Code)
                .HasColumnType("character varying")
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Height).HasColumnName("height");
            entity.Property(e => e.Image)
                .HasColumnType("character varying")
                .HasColumnName("image");
            entity.Property(e => e.Width).HasColumnName("width");
            entity.Property(e => e.XCoordinate).HasColumnName("x_coordinate");
            entity.Property(e => e.YCoordinate).HasColumnName("y_coordinate");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.Zone).WithMany(p => p.Shelves)
                .HasForeignKey(d => d.ZoneId)
                .HasConstraintName("fk_shelves_zone_id");
        });

        modelBuilder.Entity<ShelfLevel>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shelf_levels_pkey");

            entity.ToTable("shelf_levels");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasColumnType("character varying")
                .HasColumnName("code");
            entity.Property(e => e.ShelfId).HasColumnName("shelf_id");

            entity.HasOne(d => d.Shelf).WithMany(p => p.ShelfLevels)
                .HasForeignKey(d => d.ShelfId)
                .HasConstraintName("fk_shelf_levels_shelf_id");
        });

        modelBuilder.Entity<ShelfLevelBin>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shelf_level_bins_pkey");

            entity.ToTable("shelf_level_bins");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasColumnType("character varying")
                .HasColumnName("code");
            entity.Property(e => e.Height).HasColumnName("height");
            entity.Property(e => e.InventoryId).HasColumnName("inventory_id");
            entity.Property(e => e.LevelId).HasColumnName("level_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Width).HasColumnName("width");

            entity.HasOne(d => d.Inventory).WithMany(p => p.ShelfLevelBins)
                .HasForeignKey(d => d.InventoryId)
                .HasConstraintName("fk_shelf_level_bins_inventory_id");

            entity.HasOne(d => d.Level).WithMany(p => p.ShelfLevelBins)
                .HasForeignKey(d => d.LevelId)
                .HasConstraintName("fk_shelf_level_bins_level_id");
        });

        modelBuilder.Entity<ShelfNode>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shelf_node_pkey");

            entity.ToTable("shelf_node");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NodeId).HasColumnName("node_id");
            entity.Property(e => e.ShelfId).HasColumnName("shelf_id");

            entity.HasOne(d => d.Node).WithMany(p => p.ShelfNodes)
                .HasForeignKey(d => d.NodeId)
                .HasConstraintName("fk_shelf_node_node_id");

            entity.HasOne(d => d.Shelf).WithMany(p => p.ShelfNodes)
                .HasForeignKey(d => d.ShelfId)
                .HasConstraintName("fk_shelf_node_shelf_id");
        });

        modelBuilder.Entity<StockCountItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("stock_count_items_pkey");

            entity.ToTable("stock_count_items");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CountedQuantity).HasColumnName("counted_quantity");
            entity.Property(e => e.Description)
                .HasColumnType("character varying")
                .HasColumnName("description");
            entity.Property(e => e.Discrepancy).HasColumnName("discrepancy");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.StockCountId).HasColumnName("stock_count_id");
            entity.Property(e => e.SystemQuantity).HasColumnName("system_quantity");

            entity.HasOne(d => d.Product).WithMany(p => p.StockCountItems)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("fk_stock_count_items_product_id");

            entity.HasOne(d => d.StockCount).WithMany(p => p.StockCountItems)
                .HasForeignKey(d => d.StockCountId)
                .HasConstraintName("fk_stock_count_items_stock_count_id");
        });

        modelBuilder.Entity<StockCountsTicket>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("stock_counts_tickets_pkey");

            entity.ToTable("stock_counts_tickets");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AssignedTo).HasColumnName("assigned_to");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasColumnType("character varying")
                .HasColumnName("description");
            entity.Property(e => e.ExecutedDay)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("executed_day");
            entity.Property(e => e.FinishedDay)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("finished_day");
            entity.Property(e => e.Name)
                .HasColumnType("character varying")
                .HasColumnName("name");
            entity.Property(e => e.PerformedBy).HasColumnName("performed_by");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");
            entity.Property(e => e.Type)
                .HasColumnType("character varying")
                .HasColumnName("type");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.PerformedByNavigation).WithMany(p => p.StockCountsTickets)
                .HasForeignKey(d => d.PerformedBy)
                .HasConstraintName("fk_stock_counts_tickets_performed_by");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StockCountsTickets)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_stock_counts_tickets_warehouse_id");
        });

        modelBuilder.Entity<StorageForecast>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("storage_forecasts_pkey");

            entity.ToTable("storage_forecasts");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Confidence).HasColumnName("confidence");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DaysToStockout).HasColumnName("days_to_stockout");
            entity.Property(e => e.PredictedStock).HasColumnName("predicted_stock");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.RecommendedAction)
                .HasColumnType("character varying")
                .HasColumnName("recommended_action");
            entity.Property(e => e.RiskLevel)
                .HasColumnType("character varying")
                .HasColumnName("risk_level");
            entity.Property(e => e.TypeOfForecast)
                .HasColumnType("character varying")
                .HasColumnName("type_of_forecast");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.Product).WithMany(p => p.StorageForecasts)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("fk_storage_forecasts_product_id");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StorageForecasts)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_storage_forecasts_warehouse_id");
        });

        modelBuilder.Entity<StorageRecommendation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("storage_recommendations_pkey");

            entity.ToTable("storage_recommendations");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.InboundProductId).HasColumnName("inbound_product_id");
            entity.Property(e => e.Reason)
                .HasColumnType("character varying")
                .HasColumnName("reason");
            entity.Property(e => e.StorageRecommendation1)
                .HasColumnType("character varying")
                .HasColumnName("storage_recommendation");

            entity.HasOne(d => d.InboundProduct).WithMany(p => p.StorageRecommendations)
                .HasForeignKey(d => d.InboundProductId)
                .HasConstraintName("fk_storage_recommendations_inbound_product_id");
        });

        modelBuilder.Entity<StorageZone>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("storage_zones_pkey");

            entity.ToTable("storage_zones");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code)
                .HasColumnType("character varying")
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Image)
                .HasColumnType("character varying")
                .HasColumnName("image");
            entity.Property(e => e.TypeId).HasColumnName("type_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.Type).WithMany(p => p.StorageZones)
                .HasForeignKey(d => d.TypeId)
                .HasConstraintName("fk_storage_zones_type_id");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StorageZones)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_storage_zones_warehouse_id");
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("suppliers_pkey");

            entity.ToTable("suppliers");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasColumnType("character varying")
                .HasColumnName("address");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.ContactPerson)
                .HasColumnType("character varying")
                .HasColumnName("contact_person");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasColumnType("character varying")
                .HasColumnName("email");
            entity.Property(e => e.Name)
                .HasColumnType("character varying")
                .HasColumnName("name");
            entity.Property(e => e.Phone)
                .HasColumnType("character varying")
                .HasColumnName("phone");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Company).WithMany(p => p.Suppliers)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("fk_suppliers_company_id");
        });

        modelBuilder.Entity<TransferOrder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("transfer_orders_pkey");

            entity.ToTable("transfer_orders");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.DestinationWarehouseId).HasColumnName("destination_warehouse_id");
            entity.Property(e => e.SourceWarehouseId).HasColumnName("source_warehouse_id");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.TransferOrders)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("fk_transfer_orders_created_by");

            entity.HasOne(d => d.DestinationWarehouse).WithMany(p => p.TransferOrderDestinationWarehouses)
                .HasForeignKey(d => d.DestinationWarehouseId)
                .HasConstraintName("fk_transfer_orders_destination_warehouse_id");

            entity.HasOne(d => d.SourceWarehouse).WithMany(p => p.TransferOrderSourceWarehouses)
                .HasForeignKey(d => d.SourceWarehouseId)
                .HasConstraintName("fk_transfer_orders_source_warehouse_id");
        });

        modelBuilder.Entity<TransferOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("transfer_order_items_pkey");

            entity.ToTable("transfer_order_items");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.TransferOrderId).HasColumnName("transfer_order_id");

            entity.HasOne(d => d.Product).WithMany(p => p.TransferOrderItems)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("fk_transfer_order_items_product_id");

            entity.HasOne(d => d.TransferOrder).WithMany(p => p.TransferOrderItems)
                .HasForeignKey(d => d.TransferOrderId)
                .HasConstraintName("fk_transfer_order_items_transfer_order_id");
        });

        modelBuilder.Entity<ProductType >(entity =>
        {
            entity.HasKey(e => e.Id).HasName("types_pkey");

            entity.ToTable("types");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasColumnType("character varying")
                .HasColumnName("name");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasColumnType("character varying")
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasColumnType("character varying")
                .HasColumnName("full_name");
            entity.Property(e => e.PasswordHash)
                .HasColumnType("character varying")
                .HasColumnName("password_hash");
            entity.Property(e => e.Phone)
                .HasColumnType("character varying")
                .HasColumnName("phone");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Company).WithMany(p => p.Users)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("fk_users_company_id");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("fk_users_role_id");
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("warehouses_pkey");

            entity.ToTable("warehouses");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address)
                .HasColumnType("character varying")
                .HasColumnName("address");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.GridSize).HasColumnName("grid_size");
            entity.Property(e => e.Height).HasColumnName("height");
            entity.Property(e => e.Image)
                .HasColumnType("character varying")
                .HasColumnName("image");
            entity.Property(e => e.Length).HasColumnName("length");
            entity.Property(e => e.Name)
                .HasColumnType("character varying")
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasColumnType("character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Width).HasColumnName("width");

            entity.HasOne(d => d.Company).WithMany(p => p.Warehouses)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("fk_warehouses_company_id");
        });

        modelBuilder.Entity<WarehouseAssignment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("warehouse_assignments_pkey");

            entity.ToTable("warehouse_assignments");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AssignedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("assigned_at");
            entity.Property(e => e.RoleInWarehouse)
                .HasColumnType("character varying")
                .HasColumnName("role_in_warehouse");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");

            entity.HasOne(d => d.User).WithMany(p => p.WarehouseAssignments)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_warehouse_assignments_user_id");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.WarehouseAssignments)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("fk_warehouse_assignments_warehouse_id");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
