using System;
using BDanMuLib.Extensions;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record InteractWordMessage : BaseMessage<InteractWordMessage>
    {

        /// <summary>
        /// 用户UID
        /// </summary>
        public long UserId;

        /// <summary>
        /// 用户名称
        /// </summary>
        public string Username;

        /// <summary>
        /// 勋章名称
        /// </summary>
        public string Medal;

        /// <summary>
        /// 勋章等级
        /// </summary>
        public int MedalLevel;

        /// <summary>
        /// 勋章所有者
        /// </summary>
        public long MedalOwnerId;

        public InteractWordMessage(JObject json) : base(json)
        {
            var data = json["data"];
            UserId = long.Parse(data["uid"].ToString());
            Username = data["uname"]?.ToString();
            Medal = data["fans_medal"]["medal_name"]?.ToString();
            MedalLevel = int.Parse(data["fans_medal"]["medal_level"]?.ToString());
            MedalOwnerId = long.Parse(data["fans_medal"]["target_id"].ToString());
            SendTime = data["timestamp"].Value<long>().FromUnix();
        }

        public override DateTime SendTime { get; }
    }
}
