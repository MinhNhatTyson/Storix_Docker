using System;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface IImageService
    {
        Task<string> UploadProductImageAsync(IFormFile file);
    }
}
