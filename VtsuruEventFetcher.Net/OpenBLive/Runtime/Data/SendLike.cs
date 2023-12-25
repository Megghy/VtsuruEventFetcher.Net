using Newtonsoft.Json;

namespace OpenBLive.Runtime.Data
{
    [Serializable]
    public struct SendLike
    {
        /// <summary>
        /// 用户UID
        /// </summary>
        [JsonProperty("uid")] public long uid;

        /// <summary>
        /// 用户昵称
        /// </summary>
        [JsonProperty("uname")] public string userName;

        /// <summary>
        /// 用户头像
        /// </summary>
        [JsonProperty("uface")] public string userFace;

        /// <summary>
        /// 弹幕发送时间秒级时间戳
        /// </summary>
        [JsonProperty("timestamp")] public long timestamp;


        /// <summary>
        /// 点赞内容
        /// </summary>
        [JsonProperty("like_text")] public string likeText;
        /// <summary>
        /// 点赞数量
        /// </summary>
        [JsonProperty("like_count")] public int likeCount;

        /// <summary>
        /// 粉丝勋章等级
        /// </summary>
        [JsonProperty("fans_medal_level")] public long fansMedalLevel;

        /// <summary>
        /// 粉丝勋章名
        /// </summary>
        [JsonProperty("fans_medal_name")] public string fansMedalName;

        /// <summary>
        /// 佩戴的粉丝勋章佩戴状态
        /// </summary>
        [JsonProperty("fans_medal_wearing_status")]
        public bool fansMedalWearingStatus;

        /// <summary>
        /// 大航海等级
        /// </summary>
        [JsonProperty("guard_level")] public long guardLevel;

        /// <summary>
        /// 弹幕接收的直播间
        /// </summary>
        [JsonProperty("room_id")] public long roomId;
    }
}
