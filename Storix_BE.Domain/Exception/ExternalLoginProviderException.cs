using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Exception
{
    public class ExternalLoginProviderException(string provider, string message) : 
        IOException($"External login provider error {provider}: {message}");
    
    
}
