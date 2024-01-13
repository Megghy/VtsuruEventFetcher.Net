using System;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record WarnMessage : BaseMessage<WarnMessage>
    {
        public WarnMessage(JObject original) : base(original)
        {
            SendTime = DateTime.Now;
            Message = original["msg"].ToString();
        }
        public string Message { get; set; }
        public override DateTime SendTime { get; }
    }
}
