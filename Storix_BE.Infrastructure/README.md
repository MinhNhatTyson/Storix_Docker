# Storix_BE.Infrastructure

## Mô tả
Infrastructure layer chứa các implementation cụ thể của data access và external services.

## Cấu trúc
- **Data/**: DbContext, configurations, migrations
- **Repositories/**: Repository implementations
- **Services/**: External service implementations (email, file storage, etc.)

## Dependencies
- **Storix_BE.Domain**: Sử dụng entities và interfaces
- **Storix_BE.Application**: Implement các interfaces từ Application

## Nguyên tắc
- Implement tất cả interfaces từ Domain và Application
- Chứa tất cả dependencies bên ngoài (EF Core, external APIs, etc.)
- Không được reference bởi Domain hoặc Application
