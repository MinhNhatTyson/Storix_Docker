# Storix_BE.Application

## Mô tả
Application layer chứa business logic và use cases của ứng dụng.

## Cấu trúc
- **Services/**: Application services (business logic)
- **DTOs/**: Data Transfer Objects (Request/Response models)
- **Mappings/**: AutoMapper profiles
- **Interfaces/**: Service interfaces

## Dependencies
- **Storix_BE.Domain**: Sử dụng entities và interfaces từ Domain

## Nguyên tắc
- Chứa business logic và use cases
- Không phụ thuộc vào Infrastructure (trừ interfaces)
- Sử dụng DTOs để giao tiếp với API layer
