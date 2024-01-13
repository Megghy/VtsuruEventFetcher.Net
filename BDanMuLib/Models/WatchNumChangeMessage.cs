using System;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record WatchNumChangeMessage : BaseMessage<WatchNumChangeMessage>
    {
        public WatchNumChangeMessage(JObject json) : base(json)
        {
            WatchCount = json["data"]["num"].Value<int>();
            SendTime = DateTime.Now;
        }

        public int WatchCount { get; set; }
        public override DateTime SendTime { get; }
    }
}
