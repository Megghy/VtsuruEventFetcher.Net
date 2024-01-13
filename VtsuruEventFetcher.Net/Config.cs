using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VtsuruEventFetcher.Net
{
    internal class Config
    {
        public string Token { get; set; } = "";

        public string CookieCloudKey { get; set; } = "";
        public string CookieCloudPassword { get; set; } = "";
        public string CookieCloudHost { get; set; } = ""; 

        public string Cookie { get; set; } = "";
    }
}
