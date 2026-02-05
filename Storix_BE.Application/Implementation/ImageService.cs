using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class ImageService : IImageService
    {
        private readonly Cloudinary _cloudinary;

        public ImageService(Cloudinary cloudinary)
        {
            _cloudinary = cloudinary;
        }

        public async Task<string> UploadProductImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("Image file is required.");

            using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "products",   // Cloudinary folder
                Transformation = new Transformation()
                    .Width(800)
                    .Height(800)
                    .Crop("limit")
                    .Quality("auto")
            };

            var result = await _cloudinary.UploadAsync(uploadParams);

            if (result.Error != null)
                throw new InvalidOperationException(result.Error.Message);

            return result.SecureUrl.ToString();
        }
    }

}
