using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BDanmuLib.Models;
using BDanMuLib.Converters;
using BDanMuLib.Models;
using Nerdbank.Streams;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Net.Sockets;

namespace BDanMuLib
{
    /// <summary>
    /// 委托接收消息事件
    /// </summary>
    /// <param name="messageType"></param>
    /// <param name="obj"></param>
    public delegate void ReceiveMessage(long roomId, MessageType messageType, IBaseMessage obj);
    public delegate bool ReceiveRawMessage(long roomId, string message);


    /// <summary>
    /// 弹幕库核心
    /// </summary>
    public class DanMuCore
    {
        public static bool LogError = true;
        public static int MaxErrorCount = 10;

        public static string Proxy;
        public static string CaptchaResolverEndpointBase = "";

        private readonly static Random Rand = new();

        /// <summary>
        /// 消息类型到消息类构造函数的映射字典
        /// 用于统一消息对象的创建逻辑，避免重复的 switch 语句
        /// </summary>
        private static readonly Dictionary<MessageType, Func<JObject, IBaseMessage>> MessageTypeMap = new()
        {
            { MessageType.DANMU_MSG, json => new DanmuMessage(json) },
            { MessageType.SEND_GIFT, json => new GiftMessage(json) },
            { MessageType.PREPARING, json => new DefaultMessage(json) },
            { MessageType.LIVE, json => new DefaultMessage(json) },
            { MessageType.INTERACT_WORD, json => new InteractWordMessage(json) },
            { MessageType.INTERACT_WORD_V2, json => new InteractWordV2Message(json) },
            { MessageType.ONLINE_RANK_COUNT, json => new OnlineRankChangeMessage(json) },
            { MessageType.WATCHED_CHANGE, json => new WatchNumChangeMessage(json) },
            { MessageType.HOT_RANK_CHANGED, json => new DefaultMessage(json) },
            { MessageType.HOT_RANK_CHANGED_V2, json => new DefaultMessage(json) },
            { MessageType.ONLINE_RANK_TOP3, json => new DefaultMessage(json) },
            { MessageType.ONLINE_RANK_V3, json => new OnlineRankV3Message(json) },
            { MessageType.SUPER_CHAT_MESSAGE, json => new SuperChatMessage(json) },
            { MessageType.SUPER_CHAT_MESSAGE_JP, json => new SuperChatMessage(json) },
            { MessageType.ROOM_CHANGE, json => new RoomChangeMessage(json) },
            { MessageType.USER_TOAST_MSG, json => new GuardBuyMessage(json) },
            { MessageType.ROOM_BLOCK_MSG, json => new BlockMessage(json) },
            { MessageType.LIKE_INFO_V3_UPDATE, json => new LikeChangeMessage(json) },
            { MessageType.USER_VIRTUAL_MVP, json => new VirtualMVPMessage(json) },
            { MessageType.WARNING, json => new WarnMessage(json) }
        };

        private static DateTime _lastGetCookie = DateTime.MinValue;
        private static DateTime? _cookieExpireDate;
        private static TimeSpan _getCookieTime = new(0, 1, 0);
        private static string _buvidCookie = "";

        public DanMuCore(ClientType type, long id, long uid, string cookie = null, long cookieUid = -1, bool reconnect = true)
        {
            if (id == 0)
                throw new ArgumentOutOfRangeException(nameof(id));
            _roomId = id;
            _uid = uid;
            _reconnectOnFaid = reconnect;
            _cookie = cookie;
            _cookieUid = cookieUid;
            Type = type;
        }
        private readonly bool _reconnectOnFaid;
        public readonly long _roomId;
        public readonly long _uid;

        public readonly string _cookie;
        public readonly long _cookieUid;
        // /// <summary>
        // /// 直播弹幕地址
        // /// </summary>
        // private string[] _defaultHosts = { "livecmt-2.bilibili.com", "livecmt-1.bilibili.com" };

        /// <summary>
        /// 直播服务地址DNS
        /// </summary>
        private string _chatHost = "chat.bilibili.com";
        public ClientType Type { get; init; }

        /// <summary>
        /// TCP端口
        /// </summary>
        private int _chatPort = 2243;
        private int _wsPort = 0;
        private int _wssPort = 443;

        /// <summary>
        /// 客户端
        /// </summary>
        private IDanmuConnection _connection;

        /// <summary>
        /// 网络流
        /// </summary>
        private PipeReader _reader;

        /// <summary>
        /// 是否已经连接
        /// </summary>
        public bool _isConnected;

        public delegate void DisconnectDelegate();
        /// <summary>
        /// 接受消息
        /// </summary>
        public event ReceiveMessage ReceiveMessage;
        public event ReceiveRawMessage ReceiveRawMessage;
        public event DisconnectDelegate OnDisconnect;

        /// <summary>
        /// 协议版本
        /// </summary>
        private const short ProtocolVersion = 2;

        private DateTime LastDNSUpdate = DateTime.MinValue;
        private IPAddress[] ChatHosts;
        private TimeSpan MaxUpdateDNSDelay = new(0, 10, 0);

        /// <summary>
        /// 前端数据唯一性
        /// </summary>
        private static Guid Key
        {
            get
            {
                return Guid.NewGuid();
            }
        }
        public enum ClientType
        {
            Tcp,
            Ws,
            Wss
        }
        /// <summary>
        /// 连接直播弹幕服务器
        /// 建立与 B 站弹幕服务器的连接，获取房间 token 并发送认证信息
        /// </summary>
        /// <param name="proxy">代理服务器配置（可选）</param>
        /// <param name="cancel">取消令牌，用于取消连接操作</param>
        /// <returns>连接是否成功</returns>
        public async Task<bool> ConnectAsync(WebProxy proxy = null, CancellationToken cancel = default)
        {
            IPAddress ip = null;
            try
            {
                if (_isConnected)
                    throw new InvalidOperationException();

                string token = "";

                try
                {
                    using var client = new HttpClient(new HttpClientHandler()
                    {
                        Proxy = proxy,
                        AutomaticDecompression = DecompressionMethods.All
                    });

                    if (string.IsNullOrEmpty(_buvidCookie)
                        || DateTime.Now - _lastGetCookie > _getCookieTime)
                    {
                        var uri = new Uri("https://api.bilibili.com/x/web-frontend/getbuvid");
                        var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
                        request.Headers.Add("Upgrade-Insecure-Requests", "1");
                        var buvidResponse = await client.SendAsync(request, cancel);
                        try
                        {
                            if (!buvidResponse.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"无法获取Buvid: {buvidResponse.StatusCode}");
                                return false;
                            }
                            var json = JObject.Parse(await buvidResponse.Content.ReadAsStringAsync(cancel));
                            if (json["code"].ToString() == "0")
                            {
                                _buvidCookie = json["data"]["buvid"].ToString();
                                Console.WriteLine($"已刷新buvid: {_buvidCookie}");
                                _lastGetCookie = DateTime.Now;
                            }
                            else
                            {
                                Console.WriteLine($"无法获取Buvid: {json["code"]}");
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (LogError)
                            {
                                Console.WriteLine(ex);
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(_cookie))
                    {
                        _chatHost = "broadcastlv.chat.bilibili.com";
                    }
                    else
                    {
                        var dataJToken = await GetTokenAsync(proxy, cancel);
                        token = dataJToken["token"].Value<string>();

                        var hosts = dataJToken["host_list"];
                        var host = hosts[new Random().Next(hosts.Count())];
                        _chatHost = host.Value<string>("host");
                        //_chatHost = "broadcastlv.chat.bilibili.com";
                        _chatPort = host.Value<int>("port"); ;
                        _wsPort = host.Value<int>("ws_port");
                        _wssPort = host.Value<int>("wss_port");
                    }
                }
                catch (Exception ex)
                {
                    if (LogError)
                        Console.WriteLine(ex);
                    return false;
                }

                if (DateTime.Now - LastDNSUpdate > MaxUpdateDNSDelay)
                {
                    ChatHosts = await Dns.GetHostAddressesAsync(_chatHost);
                }

                _connection = Type switch
                {
                    ClientType.Tcp => new TcpDanmuConnection(),
                    ClientType.Ws => new WSDanmuConnection(proxy),
                    ClientType.Wss => new WSSDanmuConnection(proxy)
                };
                _reader = await _connection.ConnectAsync(_chatHost, Type switch
                {
                    ClientType.Tcp => _chatPort,
                    ClientType.Ws => _wsPort,
                    ClientType.Wss => _wssPort,
                }, cancel);

                if (!await SendJoinRoomAsync(_roomId, _uid, token))
                    return false;

                _isConnected = true;
                //_ = ReceiveMessageLoop();
#pragma warning disable CS4014 // 由于此调用不会等待，因此在调用完成前将继续执行当前方法
                Task.Run(HeartBeatLoopAsync);
                Task.Run(async () =>
                {
                    try
                    {
                        await ProcessDataAsync(_reader, HandleCmd).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex)
                    {
                        if (LogError)
                            Console.WriteLine(ex);
                    }

                    try
                    {
                        Disconnect();
                    }
                    catch (Exception) { }
                });
#pragma warning restore CS4014 // 由于此调用不会等待，因此在调用完成前将继续执行当前方法

                //Console.WriteLine("连接房间号:" + _roomId);
                return true;
            }
            catch (Exception ex)
            {
                if (LogError)
                    Console.WriteLine(ex.Message);
                return false;
            }
        }

        public async Task<JToken> GetTokenAsync(WebProxy proxy = null, CancellationToken cancel = default)
        {
            using var client = new HttpClient(new HttpClientHandler()
            {
                Proxy = proxy,
                AutomaticDecompression = DecompressionMethods.All
            });
            var baseQuery = new Dictionary<string, string>()
            {
                { "id", _roomId.ToString() },
                { "type", "0" },
            };
            var request = GetTokenRequest(baseQuery);
            var response = await client.SendAsync(request, cancel == default ? CancellationToken.None : cancel);
            var responseText = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseText);
            var code = json["code"].Value<int>();
            if (code != 0)
            {
                throw new Exception($"获取房间信息失败: {json["message"].Value<string>()}");
            }

            var dataJToken = json["data"];

            return dataJToken;
        }
        /// <summary>
        /// 清洗 HTTP 头部的值，移除非 ASCII 字符以及换行控制字符，避免 .NET HttpClient 抛出异常。
        /// </summary>
        /// <param name="value">原始头部值</param>
        /// <returns>仅包含可打印 ASCII 的清洗后字符串</returns>
        private static string SanitizeHeaderValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            // 允许范围: 0x20(space) - 0x7E(~)。去除 0x00-0x1F 及 0x7F 以及任何 >0x7F 的字符
            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (ch >= 0x20 && ch <= 0x7E)
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
        HttpRequestMessage GetTokenRequest(Dictionary<string, string> baseQuery)
        {
            var connectRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?" + Utils.EncWbi(new Dictionary<string, string>(baseQuery))));

            var asciiCookie = SanitizeHeaderValue(_cookie);
            if (!string.IsNullOrEmpty(asciiCookie))
                connectRequest.Headers.TryAddWithoutValidation("Cookie", asciiCookie);
            var asciiUA = SanitizeHeaderValue(Extensions.RandomUa.RandomUserAgent);
            connectRequest.Headers.TryAddWithoutValidation("User-Agent", asciiUA);
            connectRequest.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
            connectRequest.Headers.TryAddWithoutValidation("Origin", "https://www.bilibili.com");
            connectRequest.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            connectRequest.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
            connectRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            connectRequest.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            connectRequest.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            connectRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            connectRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            connectRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            connectRequest.Headers.TryAddWithoutValidation("Sec-GPC", "1");
            connectRequest.Headers.TryAddWithoutValidation("TE", "trailers");
            return connectRequest;
        }

        private const string APP_KEY = "1d8b6e7d45233436";
        private const string APP_SEC = "560c52ccd288fed045859ed18bffd973";

        public static string AppSign(Dictionary<string, string> @params)
        {
            // 为请求参数进行 APP 签名
            @params.Add("appkey", APP_KEY);
            // 按照 key 重排参数
            var sortedParams = new SortedDictionary<string, string>(@params);
            // 序列化参数
            var query = string.Join("&", sortedParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            var sign = GenerateMD5(query + APP_SEC);
            return string.Join("&", sortedParams.Select(kvp => $"{kvp.Key}={kvp.Value}")) + $"&sign={sign}";
        }

        private static string GenerateMD5(string input)
        {
            try
            {
                MD5 md = MD5.Create();
                byte[] digest = md.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in digest)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return null;
        }
        public static string GetRandomString(int length, bool useNum, bool useLow, bool useUpp, bool useSpe, string custom)
        {
            byte[] b = new byte[4];
            RandomNumberGenerator.Create().GetBytes(b);
            Random r = new(BitConverter.ToInt32(b, 0));
            string s = null, str = custom;
            if (useNum == true) { str += "0123456789"; }
            if (useLow == true) { str += "abcdefghijklmnopqrstuvwxyz"; }
            if (useUpp == true) { str += "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; }
            if (useSpe == true) { str += "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"; }
            for (int i = 0; i < length; i++)
            {
                s += str.Substring(r.Next(0, str.Length - 1), 1);
            }
            return s;
        }
        /// <summary>
        /// 发送加入房间包
        /// </summary>
        /// <param name="RoomId">房间号</param>
        /// <param name="Token">凭证</param>
        /// <returns></returns>
        private async Task<bool> SendJoinRoomAsync(long RoomId, long UId, string Token)
        {
            dynamic packageModel;
            if (string.IsNullOrEmpty(Token))
            {
                packageModel = new
                {
                    roomid = RoomId,
                };
            }
            else
            {
                packageModel = new
                {
                    roomid = RoomId,
                    uid = string.IsNullOrEmpty(_cookie) ? 0 : _cookieUid,
                    protover = 3,
                    key = Token,
                    buvid = _buvidCookie,
                    platform = "web",
                    type = 2
                };
            }
            var body = JsonConvert.SerializeObject(packageModel);

            await SendSocketDataAsync(0, 16, ProtocolVersion, 7, 1, body);
            return true;
        }

        /// <summary>
        /// 循环发送心跳包
        /// 每 30 秒向服务器发送一次心跳包，保持连接活跃状态
        /// </summary>
        /// <returns>异步任务</returns>
        private async Task HeartBeatLoopAsync()
        {
            try
            {
                while (_isConnected)
                {
                    await SendSocketDataAsync(0, 16, ProtocolVersion, 2, 1, string.Empty);
                    //心跳只需要30秒激活一次,偏移检查
                    await Task.Delay(30000);
                }
            }
            catch (Exception ex)
            {
                if (LogError && _isConnected)
                    Console.WriteLine(ex);
                DisconnectInternal();
            }
        }


        #region Receive

        private async Task ProcessDataAsync(PipeReader reader, Action<string> callback)
        {
            try
            {
                while (true)
                {
                    ReadResult result;
                    try
                    {
                        result = await reader.ReadAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常的取消/关闭，静默退出
                        break;
                    }
                    catch (IOException ioEx)
                    {
                        // 连接关闭或传输中断（例如 Operation canceled），视为正常退出
                        var msg = ioEx.Message ?? string.Empty;
                        if (msg.IndexOf("Operation canceled", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            break;
                        }
                        if (ioEx.InnerException is SocketException)
                        {
                            break;
                        }
                        // 其他 IO 异常抛出到上层（会被 ConnectAsync 外层捕获）
                        throw;
                    }
                    catch (WebSocketException)
                    {
                        // 远端未完成关闭握手直接断开，视为正常退出
                        break;
                    }

                    var buffer = result.Buffer;

                    while (TryParseCommand(ref buffer, callback)) { }

                    reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCanceled || result.IsCompleted)
                        break;
                }
            }
            finally
            {
                try { await reader.CompleteAsync(); } catch { }
            }
        }

        private bool TryParseCommand(ref ReadOnlySequence<byte> buffer, Action<string> callback)
        {
            if (buffer.Length < 4)
                return false;

            int length;
            {
                var lengthSlice = buffer.Slice(buffer.Start, 4);
                if (lengthSlice.IsSingleSegment)
                {
                    length = BinaryPrimitives.ReadInt32BigEndian(lengthSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    lengthSlice.CopyTo(stackBuffer);
                    length = BinaryPrimitives.ReadInt32BigEndian(stackBuffer);
                }
            }

            if (buffer.Length < length)
                return false;

            var headerSlice = buffer.Slice(buffer.Start, 16);
            buffer = buffer.Slice(headerSlice.End);
            var bodySlice = buffer.Slice(buffer.Start, length - 16);
            buffer = buffer.Slice(bodySlice.End);

            DanmakuProtocol header;
            if (headerSlice.IsSingleSegment)
            {
                Parse2Protocol(headerSlice.First.Span, out header);
            }
            else
            {
                Span<byte> stackBuffer = stackalloc byte[16];
                headerSlice.CopyTo(stackBuffer);
                Parse2Protocol(stackBuffer, out header);
            }

            if (header.Version == 2 && header.Action == 5)
            {
                using var deflate = new DeflateStream(bodySlice.Slice(2, bodySlice.End).AsStream(), CompressionMode.Decompress, leaveOpen: false);
                ParseCommandCompressedBody(deflate, callback);
            }
            else if (header.Version == 3 && header.Action == 5)
            {
                using var brotli = new BrotliStream(bodySlice.AsStream(), CompressionMode.Decompress, leaveOpen: false);
                ParseCommandCompressedBody(brotli, callback);
            }
            else
                ParseCommandNormalBody(ref bodySlice, header.Action, callback);

            return true;
        }

        private void ParseCommandCompressedBody(Stream decompressed, Action<string> callback)
        {
            var reader = PipeReader.Create(decompressed);
            try
            {
                while (true)
                {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                    // 全内存内运行同步返回，所以不会有问题
                    var result = reader.ReadAsync().Result;
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
                    var inner_buffer = result.Buffer;

                    while (TryParseCommand(ref inner_buffer, callback)) { }

                    reader.AdvanceTo(inner_buffer.Start, inner_buffer.End);

                    if (result.IsCompleted)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // 忽略
            }
            catch (IOException ioEx)
            {
                var msg = ioEx.Message ?? string.Empty;
                if (msg.IndexOf("Operation canceled", StringComparison.OrdinalIgnoreCase) < 0 && ioEx.InnerException is not SocketException)
                {
                    throw;
                }
                // 其他认为是正常关闭，忽略
            }
            catch (WebSocketException)
            {
                // 远端直接断开，忽略
            }
            finally
            {
                try { reader.Complete(); } catch { }
            }
        }

        private void ParseCommandNormalBody(ref ReadOnlySequence<byte> buffer, int action, Action<string> callback)
        {
            switch (action)
            {
                case 5:
                    {
                        if (buffer.Length > int.MaxValue)
                            throw new ArgumentOutOfRangeException(nameof(buffer), "ParseCommandNormalBody buffer length larger than int.MaxValue");

                        var b = ArrayPool<byte>.Shared.Rent((int)buffer.Length);
                        try
                        {
                            buffer.CopyTo(b);
                            var json = Encoding.UTF8.GetString(b, 0, (int)buffer.Length);
                            callback(json);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(b);
                        }
                    }
                    break;
                case 3:
                    var hot = EndianBitConverter.BigEndian.ToUInt32(buffer.ToArray(), 0);
                    ReceiveMessage?.Invoke(_roomId, MessageType.NONE, null);
                    break;
                default:
                    break;
            }
        }
        private static unsafe void Parse2Protocol(ReadOnlySpan<byte> buffer, out DanmakuProtocol protocol)
        {
            fixed (byte* ptr = buffer)
            {
                protocol = *(DanmakuProtocol*)ptr;
            }
            protocol.ChangeEndian();
        }

        private struct DanmakuProtocol
        {
            /// <summary>
            /// 消息总长度 (协议头 + 数据长度)
            /// </summary>
            public int PacketLength;
            /// <summary>
            /// 消息头长度 (固定为16[sizeof(DanmakuProtocol)])
            /// </summary>
            public short HeaderLength;
            /// <summary>
            /// 消息版本号
            /// </summary>
            public short Version;
            /// <summary>
            /// 消息类型
            /// </summary>
            public int Action;
            /// <summary>
            /// 参数, 固定为1
            /// </summary>
            public int Parameter;
            /// <summary>
            /// 转为本机字节序
            /// </summary>
            public void ChangeEndian()
            {
                this.PacketLength = IPAddress.HostToNetworkOrder(this.PacketLength);
                this.HeaderLength = IPAddress.HostToNetworkOrder(this.HeaderLength);
                this.Version = IPAddress.HostToNetworkOrder(this.Version);
                this.Action = IPAddress.HostToNetworkOrder(this.Action);
                this.Parameter = IPAddress.HostToNetworkOrder(this.Parameter);
            }
        }
        #endregion

        /// <summary>
        /// 解析原始消息并创建对应的消息对象
        /// 统一的消息解析逻辑，避免重复代码
        /// </summary>
        /// <param name="rawMessage">原始 JSON 消息字符串</param>
        /// <returns>解析后的消息对象</returns>
        private static IBaseMessage ParseMessage(string rawMessage)
        {
            var jObj = JObject.Parse(rawMessage);
            if (!jObj.ContainsKey("cmd"))
                return new DefaultMessage(jObj);

            var cmd = jObj.Value<string>("cmd");

            // 处理特殊的 DANMU_MSG 变体（如 DANMU_MSG:4:0:2:2:2:0）
            if (cmd.StartsWith("DANMU_MSG"))
                return new DanmuMessage(jObj);

            // 尝试解析标准消息类型
            if (Enum.TryParse<MessageType>(cmd, out var messageType))
            {
                // 使用映射字典创建消息对象
                if (MessageTypeMap.TryGetValue(messageType, out var factory))
                    return factory(jObj);
            }

            // 默认返回基础消息对象
            return new DefaultMessage(jObj);
        }

        /// <summary>
        /// 处理消息并触发相应事件
        /// 统一的消息处理逻辑，用于事件触发
        /// </summary>
        /// <param name="rawMessage">原始 JSON 消息字符串</param>
        /// <param name="messageObj">已解析的消息对象（可选，如果为 null 则重新解析）</param>
        private void ProcessMessageAndTriggerEvent(string rawMessage, IBaseMessage messageObj = null)
        {
            // 如果外部处理了原始消息，则不再继续处理
            if (ReceiveRawMessage?.Invoke(_roomId, rawMessage) == true)
                return;

            // 如果没有提供消息对象，则解析原始消息
            messageObj ??= ParseMessage(rawMessage);

            var cmd = messageObj.Metadata.Value<string>("cmd");

            // 处理特殊的 DANMU_MSG 变体
            if (cmd.StartsWith("DANMU_MSG"))
            {
                ReceiveMessage?.Invoke(_roomId, MessageType.DANMU_MSG, messageObj);
                return;
            }

            // 处理标准消息类型
            if (Enum.TryParse<MessageType>(cmd, out var messageType))
            {
                // 特殊处理：USER_TOAST_MSG 需要同时触发 GUARD_BUY 事件
                if (messageType == MessageType.USER_TOAST_MSG)
                {
                    ReceiveMessage?.Invoke(_roomId, MessageType.GUARD_BUY, messageObj);
                }

                ReceiveMessage?.Invoke(_roomId, messageType, messageObj);
            }
        }

        /// <summary>
        /// 处理消息，具体的类型处理（旧版本方法，已被新的统一处理逻辑替代）
        /// 保留此方法以维护向后兼容性，但建议使用新的 ProcessMessageAndTriggerEvent 方法
        /// </summary>
        /// <param name="type">操作码，指示消息的类型</param>
        /// <param name="buffer">消息内容的字节流</param>
        private void HandleMsg(OperateType type, byte[] buffer)
        {
            switch (type)
            {
                case OperateType.SendHeartBeat:
                    // 心跳包发送，无需处理
                    break;
                case OperateType.Hot:
                    // 人气值消息
                    var hot = EndianBitConverter.BigEndian.ToUInt32(buffer, 0);
                    ReceiveMessage?.Invoke(_roomId, MessageType.NONE, null);
                    break;
                case OperateType.DetailCommand:
                    // 详细命令消息，包含弹幕、礼物等各种事件
                    var json = Encoding.UTF8.GetString(buffer, 0, buffer.Length);

                    // 使用新的统一处理逻辑
                    ProcessMessageAndTriggerEvent(json);
                    break;
                case OperateType.AuthAndJoinRoom:
                    // 认证和加入房间响应，无需处理
                    break;
                case OperateType.ReceiveHeartBeat:
                    // 心跳包接收响应，无需处理
                    break;
                default:
                    break;
            }
        }
        /// <summary>
        /// 处理命令消息
        /// 解析 JSON 消息并触发相应的事件
        /// </summary>
        /// <param name="json">原始 JSON 消息字符串</param>
        private void HandleCmd(string json)
        {
            // 使用统一的消息处理逻辑
            ProcessMessageAndTriggerEvent(json);
        }
        /// <summary>
        /// 从原始消息字符串获取弹幕消息对象
        /// 解析 JSON 消息并返回对应的消息对象实例
        /// </summary>
        /// <param name="rawMessage">原始 JSON 消息字符串</param>
        /// <returns>解析后的消息对象</returns>
        public static IBaseMessage GetDanmakuFromRawMessage(string rawMessage)
        {
            // 使用统一的消息解析逻辑
            return ParseMessage(rawMessage);
        }

        /// <summary>
        /// 发送套字节数据
        /// </summary>
        /// <param name="packLength"></param>
        /// <param name="magic"></param>
        /// <param name="ver"></param>
        /// <param name="action"></param>
        /// <param name="param"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        private async Task SendSocketDataAsync(int packLength, short magic, short ver, int action, int param = 1, string body = "")
        {
            var playLoad = Encoding.UTF8.GetBytes(body);
            if (packLength == 0)
            {
                packLength = playLoad.Length + 16;
            }
            var buffer = new byte[packLength];

            // ReSharper disable once ConvertToUsingDeclaration
            await using (var ms = new MemoryStream(buffer))
            {
                var b = EndianBitConverter.BigEndian.GetBytes(buffer.Length);

                await ms.WriteAsync(b.AsMemory(0, 4));
                b = EndianBitConverter.BigEndian.GetBytes(magic);
                await ms.WriteAsync(b.AsMemory(0, 2));
                b = EndianBitConverter.BigEndian.GetBytes(ver);
                await ms.WriteAsync(b.AsMemory(0, 2));
                b = EndianBitConverter.BigEndian.GetBytes(action);
                await ms.WriteAsync(b.AsMemory(0, 4));
                b = EndianBitConverter.BigEndian.GetBytes(param);
                await ms.WriteAsync(b.AsMemory(0, 4));

                if (playLoad.Length > 0)
                {
                    await ms.WriteAsync(playLoad);
                }

                await _connection.SendAsync(buffer, 0, buffer.Length);
            }
        }

        private void DisconnectInternal()
        {
            _isConnected = false;

            if (_reconnectOnFaid)
            {
                ConnectAsync();
            }
            else
            {
                Disconnect();
            }
        }
        /// <summary>
        /// 断开与弹幕服务器的连接
        /// 清理连接资源并触发断开连接事件
        /// </summary>
        public void Disconnect()
        {
            if (!_isConnected) return;

            _isConnected = false;
            try
            {
                //client?.Dispose();
                _connection.Dispose();
                _reader = null;
            }
            catch (Exception)
            {
                // ignored
            }
            OnDisconnect?.Invoke();
        }

    }
}
