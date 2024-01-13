using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VtsuruEventFetcher.Net.DanmakuClient
{
    public interface IDanmakuClient : IDisposable
    {
        public Task Connect();
        public Task Init();
    }
}
