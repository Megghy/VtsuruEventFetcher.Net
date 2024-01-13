using System;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record RoomChangeMessage : BaseMessage<RoomChangeMessage>
    {
        /// <summary>
        /// 标题
        /// </summary>
        public string Title;

        /// <summary>
        /// 子分区ID
        /// </summary>
        public int AreaId;

        /// <summary>
        /// 分区ID
        /// </summary>
        public int ParentAreaId;

        /// <summary>
        /// 子分区名称
        /// </summary>
        public string AreaName;

        /// <summary>
        /// 分区名称
        /// </summary>
        public string ParentAreaName;

        public RoomChangeMessage(JObject json) : base(json)
        {
            Title = json["data"]["title"].ToString();
            AreaId = int.Parse(json["data"]["area_id"].ToString());
            ParentAreaId = int.Parse(json["data"]["parent_area_id"].ToString());
            AreaName = json["data"]["area_name"].ToString();
            ParentAreaName = json["data"]["parent_area_name"].ToString();
            SendTime = DateTime.Now;
        }

        public override DateTime SendTime { get; }
    }
}
