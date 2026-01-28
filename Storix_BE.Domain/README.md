# Storix_BE.Domain

## Mô tả
Domain layer chứa các entities, interfaces và business logic cốt lõi của ứng dụng.

## Cấu trúc
- **Entities/**: Các entity models (Product, Warehouse, Inventory, etc.)
- **Interfaces/**: Repository interfaces và service contracts
- **Common/**: Base classes, enums, constants, exceptions

## Nguyên tắc
- Không phụ thuộc vào bất kỳ layer nào khác
- Chỉ chứa business logic thuần túy
- Không có dependencies đến Infrastructure (database, external services)
