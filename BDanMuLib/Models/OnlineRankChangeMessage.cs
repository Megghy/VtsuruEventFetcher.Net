using System;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record OnlineRankChangeMessage : BaseMessage<OnlineRankChangeMessage>
    {
        public OnlineRankChangeMessage(JObject json) : base(json)
        {
            OnlineRank = json["data"]["count"].Value<int>();
            SendTime = DateTime.Now;
        }

        public int OnlineRank { get; set; }
        public override DateTime SendTime { get; }
    }
}
