using System;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record DefaultMessage : BaseMessage<DefaultMessage>
    {
        public DefaultMessage(JObject json) : base(json)
        {
            SendTime = DateTime.Now;
        }
        public DefaultMessage(string json) : base(JObject.Parse(json))
        {
        }

        public override DateTime SendTime { get; }
    }
}
