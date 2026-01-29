using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly StorixDbContext _context;

        public DebugController(StorixDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Kiểm tra kết nối tới database.
        /// </summary>
        [HttpGet("db-health")]
        public async Task<IActionResult> DbHealth()
        {
            var canConnect = await _context.Database.CanConnectAsync();
            return Ok(new { canConnect });
        }

        /// <summary>
        /// Sửa lại sequence cho cột id của bảng users để tránh lỗi duplicate key (users_pkey).
        /// Chỉ cần gọi 1 lần khi khởi tạo dữ liệu mẫu.
        /// </summary>
        [HttpPost("fix-users-sequence")]
        public async Task<IActionResult> FixUsersSequence()
        {
            const string sql = @"
DO $$
DECLARE
  seq_name text;
BEGIN
  SELECT pg_get_serial_sequence('public.users', 'id') INTO seq_name;

  IF seq_name IS NOT NULL THEN
    EXECUTE format(
      'SELECT setval(%L, (SELECT COALESCE(MAX(id), 0) + 1 FROM public.users), false);',
      seq_name
    );
  END IF;
END $$;";

            await _context.Database.ExecuteSqlRawAsync(sql);

            return Ok(new { message = "Users id sequence has been realigned to MAX(id)+1." });
        }
    }
}

