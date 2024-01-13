using System;
using BDanMuLib.Extensions;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record GiftMessage : BaseMessage<GiftMessage>
    {
        /// <summary>
        /// 礼物名称
        /// </summary>
        public string GiftName;

        /// <summary>
        /// 礼物ID
        /// </summary>
        public int GiftId;

        /// <summary>
        /// 礼物数量
        /// </summary>
        public short GiftNum;

        /// <summary>
        /// 送礼物的用户名
        /// </summary>
        public string Username;

        /// <summary>
        /// 用户ID
        /// </summary>
        public long UserId;

        /// <summary>
        /// 头像的URL
        /// </summary>
        public string FaceUrl;

        /// <summary>
        /// 礼物类型,
        /// TODO 没收集类型
        /// </summary>
        public string GiftType;

        /// <summary>
        /// 礼物单价
        /// </summary>
        public double Price;

        /// <summary>
        /// 瓜子类型
        /// gold是金瓜子
        /// silver是银瓜子
        /// </summary>
        public string CoinType;

        /// <summary>
        /// 总价值
        /// </summary>
        public long TotalCoin;

        public GiftMessage(JObject json) : base(json)
        {
            var data = json["data"];
            GiftName = data["giftName"] + "";
            GiftId = int.Parse(data["giftId"] + "");
            GiftNum = short.Parse(data["num"] + "");
            Username = data["uname"] + "";
            UserId = long.Parse(data["uid"] + "");
            FaceUrl = data["face"] + "";
            GiftType = data["giftType"] + "";
            Price = data["price"].Value<double>();
            TotalCoin = long.Parse(data["total_coin"] + "");
            CoinType = data["coin_type"] + "";
            SendTime = (data["timestamp"] + "000").ConvertStringToDateTime();
        }

        public override DateTime SendTime { get; }
    }
}
