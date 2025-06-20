using System;
using System.Diagnostics;
using System.Linq;
using BDanMuLib.Extensions;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public record DanmuMessage : BaseMessage<DanmuMessage>
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
        /// 弹幕内容
        /// </summary>
        public string Content;

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
        public string MedalOwnerName;

        /// <summary>
        /// 舰队等级
        /// 0 为非船员 1 为总督 2 为提督 3 为舰长
        /// </summary>
        public int UserGuardLevel;
        /// <summary>
        /// 是不是房管
        /// </summary>
        public bool Admin;
        /// <summary>
        /// 是不是老爷
        /// </summary>
        public bool Vip;
        /// <summary>
        /// 不包含 "emoji表情" 这个包里的表情, 那个里的只要符合[表情id]都会被转换成表情
        /// </summary>
        public bool IsEmoji;
        /// <summary>
        /// 是否是动态里的表情
        /// </summary>
        public bool IsDynamic;
        public string EmojiUrl;
        public string EmojiName;
        public string CT;
        public DateTime Timestamp;

        public DanmuMessage(JObject json) : base(json)
        {
            var info = json["info"];
            try
            {
                var medal = "";
                var medalLevel = 0;
                var medalOwnerName = "";
                //判断有没有佩戴粉丝勋章
                if (info[3].ToArray().Length != 0)
                {
                    medal = info[3][1].ToString();
                    medalLevel = int.Parse(info[3][0].ToString());
                    medalOwnerName = info[3][2].ToString();
                }
                UserId = long.Parse(info[2][0].ToString());
                Username = info[2][1].ToString();
                Content = info[1].ToString();
                SendTime = info[0][4].ToString().ConvertStringToDateTime();
                Medal = medal;
                MedalLevel = medalLevel;
                MedalOwnerName = medalOwnerName;
                Admin = info[2][2].ToString().Equals("1");
                Vip = info[2][3].ToString().Equals("1");
                UserGuardLevel = int.Parse(info[7].ToString());
                CT = info[9]["ct"].ToString();
                IsEmoji = info[0]?[13]?.Type == JTokenType.Object && info[0]?[13]?.HasValues == true;
                if (IsEmoji)
                {
                    EmojiUrl = info[0]?[13]?.Value<string>("url").Replace("http://", "https://");
                    EmojiName = info[0]?[13]?.Value<string>("emoticon_unique");
                }
            }
            catch (Exception e)
            {
                if (DanMuCore.LogError)
                {
                    Console.WriteLine(e);
                }
                Debug.WriteLine(e);
                throw;
            }
        }

        public override DateTime SendTime { get; }
    }
}
