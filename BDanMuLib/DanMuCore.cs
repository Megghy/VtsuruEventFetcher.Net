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

        private readonly static Random Rand = new();

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
        /// </summary>
        /// <param name="roomId">房间号</param>
        /// <returns>连接结果</returns>
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
                        var uri = new Uri("https://data.bilibili.com/v/");
                        var request = new HttpRequestMessage(HttpMethod.Get, uri);
                        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
                        request.Headers.Add("Upgrade-Insecure-Requests", "1");
                        var buvidResponse = await client.SendAsync(request, cancel);
                        try
                        {
                            if (buvidResponse.IsSuccessStatusCode && buvidResponse.Headers.GetValues("Set-Cookie") is { } cookies && cookies.Any())
                            {
                                //_cookie = Regex.Match(cookies.First(), @"buvid\d=(.*?);").Groups[1].Value;
                                //var expireString = Regex.Match(cookies.First(), @"Expires=(.*?);").Groups[1].Value;
                                //_cookieExpireDate = DateTime.Parse(expireString);
                                //_buvidCookie = string.Join(";", cookies);
                                _buvidCookie = Regex.Match(cookies.First(c => Regex.IsMatch(c, @"buvid\d=(.*?);")), @"buvid\d=(.*?);").Groups[1].Value;
                                Console.WriteLine($"已刷新buvid: {_buvidCookie}");
                                _lastGetCookie = DateTime.Now;
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
            /*var connectRequest = new HttpRequestMessage(HttpMethod.Get,
                        new Uri($"{(string.IsNullOrEmpty(Proxy) ? "https://api.live.bilibili.com/" : Proxy)}xlive/app-room/v1/index/getDanmuInfo?" + AppSign(new()
                    {
                        { "room_id", _roomId.ToString() },
                        { "ts", (DateTime.Now.ToUnix() / 1000).ToString() }
                    })));*/
            var connectRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?" + Utils.EncWbi(new()
            {
                { "id", _roomId.ToString() },
                { "type", "0" },
            })));
            //var connectRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiUrls.BroadCastUrl + _roomId));

            if (!string.IsNullOrEmpty(_cookie))
                connectRequest.Headers.TryAddWithoutValidation("Cookie", _cookie);
            connectRequest.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10; Win64; x64; rv:83.0) Gecko/20100101 Firefox/83.0");
            var requestContent = await client.SendAsync(connectRequest, cancel == default ? CancellationToken.None : cancel);
            var json = JObject.Parse(await requestContent.Content.ReadAsStringAsync());
            if (!json["code"].Value<int>().Equals(0))
            {
                throw new Exception($"获取房间信息失败: {json["message"].Value<string>()}");
            }

            var dataJToken = json["data"];

            return dataJToken;
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
        /// </summary>
        /// <returns></returns>
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
            while (true)
            {
                var result = await reader.ReadAsync();
                var buffer = result.Buffer;

                while (TryParseCommand(ref buffer, callback)) { }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
            await reader.CompleteAsync();
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
            reader.Complete();
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
        /// 处理消息,具体的类型处理
        /// </summary>
        /// <param name="type">操作码</param>
        /// <param name="buffer">字节流</param>
        private void HandleMsg(OperateType type, byte[] buffer)
        {
            switch (type)
            {
                case OperateType.SendHeartBeat:
                    break;
                case OperateType.Hot:
                    var hot = EndianBitConverter.BigEndian.ToUInt32(buffer, 0);
                    ReceiveMessage?.Invoke(_roomId, MessageType.NONE, null);
                    break;
                case OperateType.DetailCommand:

                    var json = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                    Console.WriteLine(json);
                    var jObj = JObject.Parse(json);

                    if (!jObj.ContainsKey("cmd")) return;

                    var cmd = jObj.Value<string>("cmd");

                    if (Enum.TryParse<MessageType>(cmd, out var cmdCommand))
                        switch (cmdCommand)
                        {
                            case MessageType.DANMU_MSG:
                                {
                                    ReceiveMessage?.Invoke(_roomId, MessageType.DANMU_MSG, new DanmuMessage(jObj));
                                }
                                break;
                            case MessageType.SEND_GIFT:
                                {
                                    ReceiveMessage?.Invoke(_roomId, MessageType.SEND_GIFT, new GiftMessage(jObj));
                                }
                                break;
                            case MessageType.WELCOME:

                                break;
                            case MessageType.WELCOME_GUARD:
                                break;
                            case MessageType.SYS_MSG:
                                break;
                            case MessageType.PREPARING:
                                ReceiveMessage?.Invoke(_roomId, MessageType.PREPARING, new DefaultMessage(json));
                                break;
                            case MessageType.LIVE:
                                ReceiveMessage?.Invoke(_roomId, MessageType.LIVE, new DefaultMessage(json));
                                break;
                            case MessageType.WISH_BOTTLE:
                                break;
                            case MessageType.INTERACT_WORD:
                                {
                                    ReceiveMessage?.Invoke(_roomId, MessageType.INTERACT_WORD, new InteractWordMessage(jObj));
                                    break;
                                }
                            case MessageType.ONLINE_RANK_COUNT:
                                {
                                    var rank = jObj["data"]["count"].Value<string>();
                                    ReceiveMessage?.Invoke(_roomId, MessageType.ONLINE_RANK_COUNT, new OnlineRankChangeMessage(jObj));
                                }
                                break;
                            case MessageType.NOTICE_MSG:
                                break;
                            case MessageType.STOP_LIVE_ROOM_LIST:
                                break;
                            case MessageType.WATCHED_CHANGE:
                                {
                                    ReceiveMessage?.Invoke(_roomId, MessageType.WATCHED_CHANGE, new WatchNumChangeMessage(jObj));
                                }
                                break;
                            case MessageType.ROOM_REAL_TIME_MESSAGE_UPDATE:
                                break;
                            case MessageType.LIVE_INTERACTIVE_GAME:
                                break;
                            case MessageType.HOT_RANK_CHANGED:
                                {
                                    var rank = jObj["data"]["rank"].Value<string>();
                                    ReceiveMessage?.Invoke(_roomId, MessageType.HOT_RANK_CHANGED, new DefaultMessage(json));
                                }
                                break;
                            case MessageType.HOT_ROOM_NOTIFY:
                                break;
                            case MessageType.HOT_RANK_CHANGED_V2:
                                {
                                    var rank = jObj["data"]["rank"].Value<string>();
                                    ReceiveMessage?.Invoke(_roomId, MessageType.HOT_RANK_CHANGED_V2, new DefaultMessage(json));
                                }
                                break;
                            case MessageType.ONLINE_RANK_TOP3:
                                {
                                    var list = jObj["data"]["list"].ToList();
                                    ReceiveMessage?.Invoke(_roomId, MessageType.ONLINE_RANK_TOP3, new DefaultMessage(json));
                                }
                                break;
                            case MessageType.ONLINE_RANK_V2:
                                {
                                    var list = jObj["data"]["list"];
                                }
                                break;
                            case MessageType.COMBO_SEND:
                                {
                                    var action = jObj["data"]["action"].Value<string>();
                                    var gift = jObj["data"]["gift_name"].Value<string>();
                                    var sendUser = jObj["data"]["uname"].Value<string>();
                                    var combo = jObj["data"]["combo_num"].Value<int>();
                                }
                                break;
                            case MessageType.ENTRY_EFFECT:

                                break;
                            case MessageType.SUPER_CHAT_MESSAGE:
                                ReceiveMessage?.Invoke(_roomId, MessageType.SUPER_CHAT_MESSAGE, new SuperChatMessage(jObj));
                                break;
                            case MessageType.SUPER_CHAT_MESSAGE_JP:
                                ReceiveMessage?.Invoke(_roomId, MessageType.SUPER_CHAT_MESSAGE, new SuperChatMessage(jObj));
                                break;
                            case MessageType.ROOM_CHANGE:
                                ReceiveMessage?.Invoke(_roomId, MessageType.ROOM_CHANGE, new RoomChangeMessage(jObj));
                                break;
                            case MessageType.USER_TOAST_MSG:
                                ReceiveMessage?.Invoke(_roomId, MessageType.GUARD_BUY, new GuardBuyMessage(jObj));
                                ReceiveMessage?.Invoke(_roomId, MessageType.USER_TOAST_MSG, new GuardBuyMessage(jObj));
                                break;
                            case MessageType.ROOM_BLOCK_MSG:
                                ReceiveMessage?.Invoke(_roomId, MessageType.ROOM_BLOCK_MSG, new BlockMessage(jObj));
                                break;
                            case MessageType.LIKE_INFO_V3_UPDATE:
                                ReceiveMessage?.Invoke(_roomId, MessageType.LIKE_INFO_V3_UPDATE, new LikeChangeMessage(jObj));
                                break;
                            case MessageType.USER_VIRTUAL_MVP:
                                ReceiveMessage?.Invoke(_roomId, MessageType.USER_VIRTUAL_MVP, new VirtualMVPMessage(jObj));
                                break;
                            case MessageType.WARNING:
                                ReceiveMessage?.Invoke(_roomId, MessageType.WARNING, new WarnMessage(jObj));
                                break;
                            case MessageType.NONE:
                            default:
                                ReceiveMessage?.Invoke(_roomId, cmdCommand, new BaseMessage(jObj));
                                break;
                        }
                    else if (cmd.StartsWith("DANMU_MSG"))
                        ReceiveMessage?.Invoke(_roomId, MessageType.DANMU_MSG, new DanmuMessage(jObj));
                    break;
                case OperateType.AuthAndJoinRoom:
                    break;
                case OperateType.ReceiveHeartBeat:
                    break;
                default:
                    break;
            }
        }
        private void HandleCmd(string json)
        {
            var danmaku = GetDanmakuFromRawMessage(json);
            var cmd = danmaku.Metadata.Value<string>("cmd");
            if (ReceiveRawMessage?.Invoke(_roomId, json) == true)
            {
                return; // 如果有RawMessage事件处理，则不再继续处理
            }

            if (Enum.TryParse<MessageType>(cmd, out var cmdCommand))
                switch (cmdCommand)
                {
                    case MessageType.DANMU_MSG:
                        {
                            ReceiveMessage?.Invoke(_roomId, MessageType.DANMU_MSG, danmaku);
                        }
                        break;
                    case MessageType.SEND_GIFT:
                        {
                            ReceiveMessage?.Invoke(_roomId, MessageType.SEND_GIFT, danmaku);
                        }
                        break;
                    case MessageType.WELCOME:

                        break;
                    case MessageType.WELCOME_GUARD:
                        break;
                    case MessageType.SYS_MSG:
                        break;
                    case MessageType.PREPARING:
                        ReceiveMessage?.Invoke(_roomId, MessageType.PREPARING, danmaku);
                        break;
                    case MessageType.LIVE:
                        ReceiveMessage?.Invoke(_roomId, MessageType.LIVE, danmaku);
                        break;
                    case MessageType.WISH_BOTTLE:
                        break;
                    case MessageType.INTERACT_WORD:
                        {
                            ReceiveMessage?.Invoke(_roomId, MessageType.INTERACT_WORD, danmaku);
                            break;
                        }
                    case MessageType.ONLINE_RANK_COUNT:
                        {
                            // var rank = jObj["data"]["count"].Value<string>();
                            ReceiveMessage?.Invoke(_roomId, MessageType.ONLINE_RANK_COUNT, danmaku);
                        }
                        break;
                    case MessageType.NOTICE_MSG:
                        break;
                    case MessageType.STOP_LIVE_ROOM_LIST:
                        break;
                    case MessageType.WATCHED_CHANGE:
                        {
                            ReceiveMessage?.Invoke(_roomId, MessageType.WATCHED_CHANGE, danmaku);
                        }
                        break;
                    case MessageType.ROOM_REAL_TIME_MESSAGE_UPDATE:
                        break;
                    case MessageType.LIVE_INTERACTIVE_GAME:
                        break;
                    case MessageType.HOT_RANK_CHANGED:
                        {
                            //var rank = jObj["data"]["rank"].Value<string>();
                            ReceiveMessage?.Invoke(_roomId, MessageType.HOT_RANK_CHANGED, danmaku);
                        }
                        break;
                    case MessageType.HOT_ROOM_NOTIFY:
                        break;
                    case MessageType.HOT_RANK_CHANGED_V2:
                        {
                            // var rank = jObj["data"]["rank"].Value<string>();
                            ReceiveMessage?.Invoke(_roomId, MessageType.HOT_RANK_CHANGED_V2, danmaku);
                        }
                        break;
                    case MessageType.ONLINE_RANK_TOP3:
                        {
                            // var list = jObj["data"]["list"].ToList();
                            ReceiveMessage?.Invoke(_roomId, MessageType.ONLINE_RANK_TOP3, danmaku);
                        }
                        break;
                    case MessageType.ONLINE_RANK_V2:
                        {
                            //var list = jObj["data"]["list"];
                        }
                        break;
                    case MessageType.COMBO_SEND:
                        {
                            /*var action = jObj["data"]["action"].Value<string>();
                            var gift = jObj["data"]["gift_name"].Value<string>();
                            var sendUser = jObj["data"]["uname"].Value<string>();
                            var combo = jObj["data"]["combo_num"].Value<int>();*/
                        }
                        break;
                    case MessageType.ENTRY_EFFECT:

                        break;
                    case MessageType.SUPER_CHAT_MESSAGE:
                        ReceiveMessage?.Invoke(_roomId, MessageType.SUPER_CHAT_MESSAGE, danmaku);
                        break;
                    case MessageType.SUPER_CHAT_MESSAGE_JP:
                        ReceiveMessage?.Invoke(_roomId, MessageType.SUPER_CHAT_MESSAGE, danmaku);
                        break;
                    case MessageType.ROOM_CHANGE:
                        ReceiveMessage?.Invoke(_roomId, MessageType.ROOM_CHANGE, danmaku);
                        break;
                    case MessageType.USER_TOAST_MSG:
                        ReceiveMessage?.Invoke(_roomId, MessageType.GUARD_BUY, danmaku);
                        ReceiveMessage?.Invoke(_roomId, MessageType.USER_TOAST_MSG, danmaku);
                        break;
                    case MessageType.ROOM_BLOCK_MSG:
                        ReceiveMessage?.Invoke(_roomId, MessageType.ROOM_BLOCK_MSG, danmaku);
                        break;
                    case MessageType.LIKE_INFO_V3_UPDATE:
                        ReceiveMessage?.Invoke(_roomId, MessageType.LIKE_INFO_V3_UPDATE, danmaku);
                        break;
                    case MessageType.USER_VIRTUAL_MVP:
                        ReceiveMessage?.Invoke(_roomId, MessageType.USER_VIRTUAL_MVP, danmaku);
                        break;
                    case MessageType.WARNING:
                        ReceiveMessage?.Invoke(_roomId, MessageType.WARNING, danmaku);
                        break;
                    case MessageType.NONE:
                    default:
                        ReceiveMessage?.Invoke(_roomId, cmdCommand, danmaku);
                        break;
                }
            else if (cmd.StartsWith("DANMU_MSG"))
                ReceiveMessage?.Invoke(_roomId, MessageType.DANMU_MSG, danmaku);
        }
        public static IBaseMessage GetDanmakuFromRawMessage(string rawMessage)
        {
            //Console.WriteLine(json);
            var jObj = JObject.Parse(rawMessage);
            if (!jObj.ContainsKey("cmd")) return new DefaultMessage(jObj);
            var cmd = jObj.Value<string>("cmd");

            if (Enum.TryParse<MessageType>(cmd, out var cmdCommand))
            {
                switch (cmdCommand)
                {
                    case MessageType.DANMU_MSG:
                        return new DanmuMessage(jObj);
                    case MessageType.SEND_GIFT:
                        return new GiftMessage(jObj);
                    case MessageType.WELCOME:

                        break;
                    case MessageType.WELCOME_GUARD:
                        break;
                    case MessageType.SYS_MSG:
                        break;
                    case MessageType.PREPARING:
                        return new DefaultMessage(jObj);
                    case MessageType.LIVE:
                        return new DefaultMessage(jObj);
                    case MessageType.WISH_BOTTLE:
                        break;
                    case MessageType.INTERACT_WORD:
                        return new InteractWordMessage(jObj);
                    case MessageType.ONLINE_RANK_COUNT:
                        return new OnlineRankChangeMessage(jObj);
                    case MessageType.NOTICE_MSG:
                        break;
                    case MessageType.STOP_LIVE_ROOM_LIST:
                        break;
                    case MessageType.WATCHED_CHANGE:
                        return new WatchNumChangeMessage(jObj);
                    case MessageType.ROOM_REAL_TIME_MESSAGE_UPDATE:
                        break;
                    case MessageType.LIVE_INTERACTIVE_GAME:
                        break;
                    case MessageType.HOT_RANK_CHANGED:
                        return new DefaultMessage(jObj);
                    case MessageType.HOT_ROOM_NOTIFY:
                        break;
                    case MessageType.HOT_RANK_CHANGED_V2:
                        return new DefaultMessage(jObj);
                    case MessageType.ONLINE_RANK_TOP3:
                        return new DefaultMessage(jObj);
                    case MessageType.ONLINE_RANK_V2:
                        {
                            var list = jObj["data"]["list"];
                        }
                        break;
                    case MessageType.COMBO_SEND:
                        {
                            var action = jObj["data"]["action"].Value<string>();
                            var gift = jObj["data"]["gift_name"].Value<string>();
                            var sendUser = jObj["data"]["uname"].Value<string>();
                            var combo = jObj["data"]["combo_num"].Value<int>();
                        }
                        break;
                    case MessageType.ENTRY_EFFECT:

                        break;
                    case MessageType.SUPER_CHAT_MESSAGE:
                        return new SuperChatMessage(jObj);
                    case MessageType.SUPER_CHAT_MESSAGE_JP:
                        return new SuperChatMessage(jObj);
                    case MessageType.ROOM_CHANGE:
                        return new RoomChangeMessage(jObj);
                    case MessageType.USER_TOAST_MSG:
                        return new GuardBuyMessage(jObj);
                    case MessageType.ROOM_BLOCK_MSG:
                        return new BlockMessage(jObj);
                    case MessageType.LIKE_INFO_V3_UPDATE:
                        return new LikeChangeMessage(jObj);
                    case MessageType.USER_VIRTUAL_MVP:
                        return new VirtualMVPMessage(jObj);
                    case MessageType.WARNING:
                        return new WarnMessage(jObj);
                        //case MessageType.NONE:
                }
            }
            else if (cmd.StartsWith("DANMU_MSG"))
                return new DanmuMessage(jObj);
            return new DefaultMessage(jObj);
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
        /// 断开连接
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
