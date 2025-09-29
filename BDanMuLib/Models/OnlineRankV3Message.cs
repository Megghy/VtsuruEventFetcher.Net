using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using BDanMuLib.Protos;

namespace BDanMuLib.Models
{
  /// <summary>
  /// 高能用户列表 V3 (protobuf格式)
  /// </summary>
  public class OnlineRankV3Message : IBaseMessage
  {
    public JObject Metadata { get; init; }

    /// <summary>
    /// 排行榜类型，例如：online_rank
    /// </summary>
    public string RankType { get; set; }
    public List<OnlineListItem> OnlineList { get; set; }

    public OnlineRankV3Message(JObject json)
    {
      Metadata = json;

      try
      {
        var data = json["data"];
        OnlineList = new List<OnlineListItem>();

        // 从 protobuf 数据解析
        var pbData = data["pb"]?.Value<string>();
        if (!string.IsNullOrEmpty(pbData))
        {
          ParseFromProtobuf(pbData);
        }

        // 备用：从可能存在的 JSON 字段解析
        if (data["rank_type"] != null)
          RankType = data["rank_type"].Value<string>();

        if (data["online_list"] != null && data["online_list"].Type == JTokenType.Array)
        {
          foreach (var item in data["online_list"])
          {
            OnlineList.Add(new OnlineListItem(item as JObject));
          }
        }
      }
      catch (Exception)
      {
        // 解析失败时的默认值
        RankType = "online_rank";
        OnlineList = new List<OnlineListItem>();
      }
    }

    /// <summary>
    /// 从protobuf数据解析OnlineRankV3消息
    /// </summary>
    /// <param name="base64Data">Base64编码的protobuf数据</param>
    private void ParseFromProtobuf(string base64Data)
    {
      try
      {
        var data = Convert.FromBase64String(base64Data);
        var pbMessage = OnlineRankV3.Parser.ParseFrom(data);

        // 映射protobuf消息到当前对象
        RankType = pbMessage.RankType;
        OnlineList = new List<OnlineListItem>();

        foreach (var pbItem in pbMessage.OnlineList)
        {
          var item = new OnlineListItem(new JObject()) // 创建空的JObject，因为我们直接从protobuf填充
          {
            Uid = pbItem.Uid,
            Face = pbItem.Face,
            Score = pbItem.Score,
            Uname = pbItem.Uname,
            Rank = pbItem.Rank,
            GuardLevel = pbItem.HasGuardLevel ? pbItem.GuardLevel : null,
            IsMystery = pbItem.HasIsMystery ? pbItem.IsMystery : null
          };

          // 映射UInfo
          if (pbItem.Uinfo != null)
          {
            item.UInfo = new OnlineRankUInfo(new JObject())
            {
              Uid = pbItem.Uinfo.Uid,
              Base = pbItem.Uinfo.Base != null ? new OnlineRankBase(new JObject())
              {
                Name = pbItem.Uinfo.Base.Name,
                Face = pbItem.Uinfo.Base.Face,
                NameColor = pbItem.Uinfo.Base.NameColor,
                IsMystery = pbItem.Uinfo.Base.IsMystery
              } : null,
              Guard = pbItem.Uinfo.Guard != null ? new OnlineRankGuard(new JObject())
              {
                Level = pbItem.Uinfo.Guard.Level,
                ExpiredStr = pbItem.Uinfo.Guard.ExpiredStr
              } : null
            };
          }

          OnlineList.Add(item);
        }
      }
      catch (Exception ex)
      {
        // 解析失败时记录错误但不抛出异常
        Console.WriteLine($"解析OnlineRankV3 protobuf数据失败: {ex.Message}");
        // 设置默认值
        RankType = "online_rank";
        OnlineList = new List<OnlineListItem>();
      }
    }
  }

  /// <summary>
  /// 在线榜单项
  /// </summary>
  public class OnlineListItem
  {
    public ulong Uid { get; set; }
    public string Face { get; set; }
    /// <summary>
    /// 贡献值，打赏电池数，string 格式
    /// </summary>
    public string Score { get; set; }
    public string Uname { get; set; }
    /// <summary>
    /// 当前排名
    /// </summary>
    public uint Rank { get; set; }
    public uint? GuardLevel { get; set; }
    public bool? IsMystery { get; set; }
    public OnlineRankUInfo UInfo { get; set; }

    public OnlineListItem(JObject json)
    {
      try
      {
        if (json["uid"] != null)
          Uid = json["uid"].Value<ulong>();
        if (json["face"] != null)
          Face = json["face"].Value<string>();
        if (json["score"] != null)
          Score = json["score"].Value<string>();
        if (json["uname"] != null)
          Uname = json["uname"].Value<string>();
        if (json["rank"] != null)
          Rank = json["rank"].Value<uint>();
        if (json["guard_level"] != null)
          GuardLevel = json["guard_level"].Value<uint>();
        if (json["is_mystery"] != null)
          IsMystery = json["is_mystery"].Value<bool>();
        if (json["uinfo"] != null)
          UInfo = new OnlineRankUInfo(json["uinfo"] as JObject);
      }
      catch (Exception)
      {
        // 解析失败时的默认值
        Uid = 0;
        Face = string.Empty;
        Score = "0";
        Uname = string.Empty;
        Rank = 0;
      }
    }
  }

  /// <summary>
  /// 在线榜单用户信息
  /// </summary>
  public class OnlineRankUInfo
  {
    public ulong Uid { get; set; }
    public OnlineRankBase Base { get; set; }
    public OnlineRankGuard Guard { get; set; }

    public OnlineRankUInfo(JObject json)
    {
      try
      {
        if (json["uid"] != null)
          Uid = json["uid"].Value<ulong>();
        if (json["base"] != null)
          Base = new OnlineRankBase(json["base"] as JObject);
        if (json["guard"] != null)
          Guard = new OnlineRankGuard(json["guard"] as JObject);
      }
      catch (Exception)
      {
        // 解析失败时的默认值
        Uid = 0;
      }
    }
  }

  /// <summary>
  /// 在线榜单基础信息
  /// </summary>
  public class OnlineRankBase
  {
    public string Name { get; set; }
    public string Face { get; set; }
    public uint NameColor { get; set; }
    public bool IsMystery { get; set; }

    public OnlineRankBase(JObject json)
    {
      try
      {
        if (json["name"] != null)
          Name = json["name"].Value<string>();
        if (json["face"] != null)
          Face = json["face"].Value<string>();
        if (json["name_color"] != null)
          NameColor = json["name_color"].Value<uint>();
        if (json["is_mystery"] != null)
          IsMystery = json["is_mystery"].Value<bool>();
      }
      catch (Exception)
      {
        // 解析失败时的默认值
        Name = string.Empty;
        Face = string.Empty;
        NameColor = 0;
        IsMystery = false;
      }
    }
  }

  /// <summary>
  /// 在线榜单舰长信息
  /// </summary>
  public class OnlineRankGuard
  {
    public uint Level { get; set; }
    /// <summary>
    /// 过期时间字符串，例如：2025-08-30 23:59:59
    /// </summary>
    public string ExpiredStr { get; set; }

    public OnlineRankGuard(JObject json)
    {
      try
      {
        if (json["level"] != null)
          Level = json["level"].Value<uint>();
        if (json["expired_str"] != null)
          ExpiredStr = json["expired_str"].Value<string>();
      }
      catch (Exception)
      {
        // 解析失败时的默认值
        Level = 0;
        ExpiredStr = string.Empty;
      }
    }
  }
}
