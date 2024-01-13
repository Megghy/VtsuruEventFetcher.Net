using System;
using System.Collections;
using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Nerdbank.Streams;

namespace BDanMuLib
{
    internal class WSSDanmuConnection : WSDanmuConnection
    {
        public WSSDanmuConnection(WebProxy proxy = null) : base(proxy) { }
        protected override string Scheme => "wss";
    }
    internal class WSDanmuConnection : IDanmuConnection
    {
        private readonly ClientWebSocket socket;

        protected virtual string Scheme => "ws";
        static WSDanmuConnection()
        {

            var headerInfoTable = typeof(WebHeaderCollection).Assembly.GetType("System.Net.HeaderInfoTable", false);
            if (headerInfoTable is null) return;

            var headerHashTable = headerInfoTable.GetField("HeaderHashTable", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (headerHashTable is null) return;

            if (headerHashTable.GetValue(null) is not Hashtable table) return;

            foreach (var key in new[] { "User-Agent", "Referer", "Accept" })
            {
                var info = table[key];
                if (info is null) continue;

                var isRequestRestrictedProperty = info.GetType().GetField("IsRequestRestricted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (isRequestRestrictedProperty is null) continue;

                isRequestRestrictedProperty.SetValue(info, false);
            }
        }
        public WSDanmuConnection(WebProxy proxy = null)
        {
            this.socket = new ClientWebSocket();
            var options = this.socket.Options;
            options.UseDefaultCredentials = false;
            options.Credentials = null;
            options.Proxy = proxy;
            options.Cookies = null;
            options.SetRequestHeader("Origin", "https://live.bilibili.com");
            options.SetRequestHeader("Referer", "https://live.bilibili.com/");
            options.SetRequestHeader("User-Agent", Extensions.RandomUa.RandomUserAgent);
            options.SetRequestHeader("Accept-Language", "zh-CN");
            options.SetRequestHeader("Accept", "*/*");
            options.SetRequestHeader("Pragma", "no-cache");
            options.SetRequestHeader("Cache-Control", "no-cache");
            options.SetRequestHeader("X-Forwarded-For", GetRandomIP());
        }
        static string GetRandomIP()
        {
            // Generate a random IP address
            Random random = new Random();
            byte[] bytes = new byte[4];
            random.NextBytes(bytes);
            IPAddress ip = new IPAddress(bytes);

            // Return the IP address as a string
            return ip.ToString();
        }
        public async Task<PipeReader> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            var b = new UriBuilder(this.Scheme, host, port, "/sub");

            // 连接超时 10 秒
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            await this.socket.ConnectAsync(b.Uri, cts.Token).ConfigureAwait(false);
            return socket.UsePipeReader();
        }

        public void Dispose()
        {
            socket.Dispose();
        }

        public async Task SendAsync(byte[] buffer, int offset, int count)
        {
            await this.socket.SendAsync(new ArraySegment<byte>(buffer, offset, count), WebSocketMessageType.Binary, true, default).ConfigureAwait(false);
        }
    }
}
