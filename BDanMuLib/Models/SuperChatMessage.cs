using System;
using BDanMuLib.Extensions;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record SuperChatMessage : BaseMessage<SuperChatMessage>
    {
        public SuperChatMessage(JObject json) : base(json)
        {
            var data = json["data"];
            UserId = data["uid"].Value<long>();
            UserName = data["user_info"]["uname"].Value<string>();
            Price = data["price"].Value<int>();
            StartTime = data["start_time"].Value<int>();
            EndTime = data["end_time"].Value<int>();
            Message = data["message"]?.Value<string>();
            Message_JP = data["message_jpn"]?.Value<string>();
            SendTime = data["ts"]?.Value<long>().FromUnix() ?? DateTime.Now;
        }

        public string UserName { get; set; }
        public long UserId { get; set; }
        public int Price { get; set; }
        public int StartTime { get; set; }
        public int EndTime { get; set; }
        public string Message { get; set; }
        public string Message_JP { get; set; }
        public override DateTime SendTime { get; }
    }
}
