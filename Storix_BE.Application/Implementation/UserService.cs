using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _accRepository;
        public UserService(IUserRepository accRepository)
        {
            _accRepository = accRepository;
        }
        public async Task<User> Login(string email, string password)
        {
            return await _accRepository.Login(email, password);
        }

        public async Task<User> LoginWithGoogleAsync(ClaimsPrincipal? claimsPrincipal)
        {
            return await _accRepository.LoginWithGoogleAsync(claimsPrincipal);
        }
    }
}
