using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Configuration
{
    public class SystemSettingModel
    {
        private static SystemSettingModel _instance;
        public static IConfiguration Configuration { get; set; }
        public string ApplicationName { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name;
        public string? Domain { get; set; }

        public static SystemSettingModel Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SystemSettingModel();
                }
                return _instance;
            }
            set
            {
                _instance = value;
            }
        }
    }

    public class CloudinarySetting
    {
        public static CloudinarySetting Instance { get; set; }
        public string CloudinaryUrl { get; set; }
    }
}
