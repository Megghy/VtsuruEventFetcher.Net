using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace BDanMuLib
{
    public interface IDanmuConnection : IDisposable
    {
        Task<PipeReader> ConnectAsync(string host, int port, CancellationToken cancellationToken);
        Task SendAsync(byte[] buffer, int offset, int count);
    }
}
