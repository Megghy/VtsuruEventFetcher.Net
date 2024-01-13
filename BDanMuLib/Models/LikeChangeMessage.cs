using System;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record LikeChangeMessage : BaseMessage<LikeChangeMessage>
    {
        public LikeChangeMessage(JObject json) : base(json)
        {
            LikeCount = json["data"]["click_count"].Value<int>();
            SendTime = DateTime.Now;
        }
        public int LikeCount;

        public override DateTime SendTime { get; }
    }
}
