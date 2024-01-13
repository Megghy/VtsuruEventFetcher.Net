using System;
using BDanMuLib.Extensions;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record VirtualMVPMessage : BaseMessage<VirtualMVPMessage>
    {
        public VirtualMVPMessage(JObject original) : base(original)
        {
            var data = original["data"];
            GoodName = data["goods_name"].ToString();
            Username = data["uname"]?.ToString();
            UID = data["uid"].Value<long>();
            GoodNum = data["goods_num"].Value<int>();
            GoodPrice = data["goods_price"].Value<int>();
            GoodToast = data["success_toast"].ToString();
            Timestamp = data["timestamp"].Value<long>().FromUnix();
        }

        public string GoodName { get; set; }
        public int GoodNum { get; set; }
        public int GoodPrice { get; set; }
        public string GoodToast { get; set; }
        public DateTime Timestamp { get; set; }
        public long UID { get; set; }
        public string Username { get; set; }
        public override DateTime SendTime
            => Timestamp;
    }
}
