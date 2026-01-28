# Storix_BE.API

## Mô tả
API layer là presentation layer, chứa controllers và API configuration.

## Cấu trúc
- **Controllers/**: API controllers
- **Program.cs**: Application startup và configuration
- **appsettings.json**: Configuration files

## Dependencies
- **Storix_BE.Application**: Sử dụng services từ Application layer
- **Storix_BE.Infrastructure**: Đăng ký services từ Infrastructure

## Nguyên tắc
- Chỉ chứa presentation logic
- Controllers nên mỏng, delegate logic cho Application services
- Dependency Injection được cấu hình ở đây
