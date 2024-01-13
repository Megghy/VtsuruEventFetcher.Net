using System;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record BlockMessage : BaseMessage<BlockMessage>
    {
        public BlockMessage(JObject json) : base(json)
        {
            UserName = json["data"]["uname"].Value<string>();
            UserId = json["data"]["uid"].Value<long>();
            SendTime = DateTime.Now;
        }
        public string UserName;
        public long UserId;

        public override DateTime SendTime { get; }
    }
}
