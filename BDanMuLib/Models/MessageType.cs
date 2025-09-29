namespace BDanmuLib.Models
{
    /// <summary>
    /// 具体命令
    /// </summary>
    public enum MessageType
    {

        /// <summary>
        /// 无命令
        /// </summary>
        NONE,

        /// <summary>
        /// 弹幕信息
        /// </summary>
        DANMU_MSG,

        /// <summary>
        /// 礼物信息
        /// </summary>
        SEND_GIFT,

        /// <summary>
        /// 欢迎信息
        /// </summary>
        WELCOME,

        /// <summary>
        /// 欢迎房管
        /// </summary>
        WELCOME_GUARD,

        /// <summary>
        /// 系统信息
        /// </summary>
        SYS_MSG,

        /// <summary>
        /// 主播准备中
        /// </summary>
        PREPARING,

        /// <summary>
        /// 正在直播
        /// </summary>
        LIVE,

        /// <summary>
        /// 许愿瓶
        /// </summary>
        WISH_BOTTLE,

        /// <summary>
        /// 进入房间
        /// </summary>
        INTERACT_WORD,

        /// <summary>
        /// 进入房间 V2
        /// </summary>
        INTERACT_WORD_V2,

        /// <summary>
        /// 榜单排名数
        /// </summary>
        ONLINE_RANK_COUNT,

        /// <summary>
        /// 消息通知
        /// </summary>
        NOTICE_MSG,

        /// <summary>
        /// 暂时无用
        /// </summary>
        STOP_LIVE_ROOM_LIST,

        /// <summary>
        /// 观看次数
        /// </summary>
        WATCHED_CHANGE,

        /// <summary>
        /// 房间即时信息更新
        /// </summary>
        ROOM_REAL_TIME_MESSAGE_UPDATE,

        /// <summary>
        /// 投喂相关
        /// </summary>
        LIVE_INTERACTIVE_GAME,

        /// <summary>
        /// 热度排名
        /// </summary>
        HOT_RANK_CHANGED,

        HOT_ROOM_NOTIFY,

        WIDGET_BANNER,

        /// <summary>
        /// 全站实时排名
        /// </summary>
        HOT_RANK_CHANGED_V2,

        /// <summary>
        /// 榜单前三更新
        /// </summary>
        ONLINE_RANK_TOP3,

        /// <summary>
        /// 榜单排名
        /// </summary>
        ONLINE_RANK_V2,

        /// <summary>
        /// 榜单排名 V3
        /// </summary>
        ONLINE_RANK_V3,

        /// <summary>
        /// 礼物连击
        /// </summary>
        COMBO_SEND,


        /// <summary>
        /// 舰长进入
        /// </summary>
        ENTRY_EFFECT,

        /// <summary>
        /// 上舰
        /// </summary>
        GUARD_BUY,

        /// <summary>
        /// 上舰
        /// </summary>
        USER_TOAST_MSG,

        /// <summary>
        /// 房间信息改变
        /// </summary>
        ROOM_CHANGE,

        /// <summary>
        /// sc
        /// </summary>
        SUPER_CHAT_MESSAGE,

        /// <summary>
        /// sc
        /// </summary>
        SUPER_CHAT_MESSAGE_JP,

        /// <summary>
        /// 子项
        /// </summary>
        Title_Change,

        /// <summary>
        /// 禁言
        /// </summary>
        ROOM_BLOCK_MSG,

        /// <summary>
        /// 点赞
        /// </summary>
        LIKE_INFO_V3_UPDATE,

        /// <summary>
        /// 大法师
        /// </summary>
        USER_VIRTUAL_MVP,

        /// <summary>
        /// 沙软未登录限制, 会让uid变0, 用户名打码
        /// </summary>
        LOG_IN_NOTICE,

        /// <summary>
        /// 警告
        /// </summary>
        WARNING,
    }
}
