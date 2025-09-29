using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Google.Protobuf;
using BDanMuLib.Protos;

namespace BDanMuLib.Models
{
  /// <summary>
  /// 互动事件 V2 (protobuf格式)
  /// </summary>
  public class InteractWordV2Message : IBaseMessage
  {
    public JObject Metadata { get; init; }

    public ulong Uid { get; set; }
    public string Username { get; set; }
    public uint MsgType { get; set; }
    public uint RoomId { get; set; }
    public uint Timestamp { get; set; }
    public ulong TimestampMs { get; set; }
    public Medal Medal { get; set; }
    public ulong TriggerTime { get; set; }
    public uint GuardType { get; set; }
    public int F17 { get; set; }
    public UInfo UserInfo { get; set; }
    public RelationTail RelationTail { get; set; }

    public InteractWordV2Message(JObject json)
    {
      Metadata = json;

      try
      {
        var data = json["data"];

        // 从 protobuf 数据解析
        var pbData = data["pb"]?.Value<string>();
        if (!string.IsNullOrEmpty(pbData))
        {
          ParseFromProtobuf(pbData);
        }

        // 备用：从可能存在的 JSON 字段解析
        if (data["uid"] != null)
          Uid = data["uid"].Value<ulong>();
        if (data["uname"] != null)
          Username = data["uname"].Value<string>();
        if (data["msg_type"] != null)
          MsgType = data["msg_type"].Value<uint>();
        if (data["roomid"] != null)
          RoomId = data["roomid"].Value<uint>();
        if (data["timestamp"] != null)
          Timestamp = data["timestamp"].Value<uint>();
      }
      catch (Exception)
      {
        // 解析失败时的默认值
        Uid = 0;
        Username = string.Empty;
        MsgType = 0;
        RoomId = 0;
        Timestamp = 0;
        TimestampMs = 0;
        TriggerTime = 0;
        GuardType = 0;
      }
    }

    /// <summary>
    /// 从protobuf数据解析InteractWordV2消息
    /// </summary>
    /// <param name="base64Data">Base64编码的protobuf数据</param>
    private void ParseFromProtobuf(string base64Data)
    {
      try
      {
        var data = Convert.FromBase64String(base64Data);
        var pbMessage = InteractWordV2.Parser.ParseFrom(data);

        // 映射protobuf消息到当前对象
        Uid = pbMessage.Uid;
        Username = pbMessage.Username;
        MsgType = pbMessage.MsgType;
        RoomId = pbMessage.RoomId;
        Timestamp = pbMessage.Timestamp;
        TimestampMs = pbMessage.TimestampMs;
        TriggerTime = pbMessage.TriggerTime;
        GuardType = pbMessage.GuardType;
        F17 = pbMessage.F17;

        // 映射Medal
        if (pbMessage.Medal != null)
        {
          Medal = new Medal
          {
            Ruid = pbMessage.Medal.Ruid,
            Level = pbMessage.Medal.Level,
            Name = pbMessage.Medal.Name,
            F4 = pbMessage.Medal.F4,
            F5 = pbMessage.Medal.F5,
            F6 = pbMessage.Medal.F6,
            F7 = pbMessage.Medal.F7,
            IsLighted = pbMessage.Medal.IsLighted,
            GuardLevel = pbMessage.Medal.GuardLevel,
            RoomId = pbMessage.Medal.RoomId,
            F13 = pbMessage.Medal.F13
          };
        }

        // 映射UInfo
        if (pbMessage.Uinfo != null)
        {
          UserInfo = new UInfo
          {
            Uid = pbMessage.Uinfo.Uid,
            Base = pbMessage.Uinfo.Base != null ? new Base
            {
              Username = pbMessage.Uinfo.Base.Username,
              Avatar = pbMessage.Uinfo.Base.Avatar,
              F3 = pbMessage.Uinfo.Base.F3
            } : null,
            Medal = pbMessage.Uinfo.Medal != null ? new UInfoMedal
            {
              Name = pbMessage.Uinfo.Medal.Name,
              Level = pbMessage.Uinfo.Medal.Level,
              ColorStart = pbMessage.Uinfo.Medal.ColorStart,
              ColorEnd = pbMessage.Uinfo.Medal.ColorEnd,
              ColorBorder = pbMessage.Uinfo.Medal.ColorBorder,
              Color = pbMessage.Uinfo.Medal.Color,
              Id = pbMessage.Uinfo.Medal.Id,
              F9 = pbMessage.Uinfo.Medal.F9,
              Ruid = pbMessage.Uinfo.Medal.Ruid,
              F11 = pbMessage.Uinfo.Medal.F11,
              F12 = pbMessage.Uinfo.Medal.F12,
              GuardIcon = pbMessage.Uinfo.Medal.GuardIcon,
              V2MedalColorStart = pbMessage.Uinfo.Medal.V2MedalColorStart,
              V2MedalColorEnd = pbMessage.Uinfo.Medal.V2MedalColorEnd,
              V2MedalColorBorder = pbMessage.Uinfo.Medal.V2MedalColorBorder,
              V2MedalText = pbMessage.Uinfo.Medal.V2MedalText,
              V2MedalLevel = pbMessage.Uinfo.Medal.V2MedalLevel
            } : null,
            Wealth = pbMessage.Uinfo.Wealth != null ? new Wealth
            {
              Level = pbMessage.Uinfo.Wealth.Level,
              Icon = pbMessage.Uinfo.Wealth.Icon
            } : null,
            F6 = pbMessage.Uinfo.F6 != null ? new Message6
            {
              F1 = pbMessage.Uinfo.F6.F1,
              F2 = pbMessage.Uinfo.F6.F2
            } : null
          };
        }

        // 映射RelationTail
        if (pbMessage.RelationTail != null)
        {
          RelationTail = new RelationTail
          {
            TailIcon = pbMessage.RelationTail.TailIcon,
            TailGuideText = pbMessage.RelationTail.TailGuideText,
            TailType = pbMessage.RelationTail.TailType
          };
        }
      }
      catch (Exception ex)
      {
        // 解析失败时记录错误但不抛出异常
        Console.WriteLine($"解析InteractWordV2 protobuf数据失败: {ex.Message}");
      }
    }
  }

  /// <summary>
  /// 粉丝牌信息
  /// </summary>
  public class Medal
  {
    public ulong Ruid { get; set; }
    public uint Level { get; set; }
    public string Name { get; set; }
    public int F4 { get; set; }
    public int F5 { get; set; }
    public int F6 { get; set; }
    public int F7 { get; set; }
    public uint IsLighted { get; set; }
    public uint GuardLevel { get; set; }
    public uint RoomId { get; set; }
    public int F13 { get; set; }
  }

  /// <summary>
  /// 用户信息
  /// </summary>
  public class UInfo
  {
    public ulong Uid { get; set; }
    public Base Base { get; set; }
    public UInfoMedal Medal { get; set; }
    public Wealth Wealth { get; set; }
    public Message6 F6 { get; set; }
  }

  /// <summary>
  /// 基础用户信息
  /// </summary>
  public class Base
  {
    public string Username { get; set; }
    public string Avatar { get; set; }
    public string F3 { get; set; }
  }

  /// <summary>
  /// 用户信息中的粉丝牌
  /// </summary>
  public class UInfoMedal
  {
    /// <summary>
    /// 粉丝牌名称，例如：奶糖花
    /// </summary>
    public string Name { get; set; }
    /// <summary>
    /// 粉丝牌等级
    /// </summary>
    public uint Level { get; set; }
    public int ColorStart { get; set; }
    public int ColorEnd { get; set; }
    public int ColorBorder { get; set; }
    public int Color { get; set; }
    public int Id { get; set; }
    public int F9 { get; set; }
    public ulong Ruid { get; set; }
    public int F11 { get; set; }
    public int F12 { get; set; }
    public string GuardIcon { get; set; }
    /// <summary>
    /// V2粉丝牌起始颜色，例如：#4775EFCC
    /// </summary>
    public uint V2MedalColorStart { get; set; }
    /// <summary>
    /// V2粉丝牌结束颜色，例如：#4775EFCC
    /// </summary>
    public uint V2MedalColorEnd { get; set; }
    /// <summary>
    /// V2粉丝牌边框颜色，例如：#58A1F8FF
    /// </summary>
    public uint V2MedalColorBorder { get; set; }
    /// <summary>
    /// V2粉丝牌文字颜色，例如：#FFFFFFFF
    /// </summary>
    public uint V2MedalText { get; set; }
    /// <summary>
    /// V2粉丝牌等级颜色，例如：#000B7099
    /// </summary>
    public uint V2MedalLevel { get; set; }
  }

  /// <summary>
  /// 荣耀等级
  /// </summary>
  public class Wealth
  {
    public uint Level { get; set; }
    /// <summary>
    /// 荣耀等级图标，例如：ChronosWealth_5.png
    /// </summary>
    public string Icon { get; set; }
  }

  /// <summary>
  /// 关系尾标
  /// </summary>
  public class RelationTail
  {
    /// <summary>
    /// 尾标图标，例如：https://i0.hdslb.com/bfs/live/b9de5d510125c6f14cd68391d5a4878fe16356b3.png
    /// </summary>
    public string TailIcon { get; set; }
    /// <summary>
    /// 尾标引导文字，例如：TA常看你的直播，但还没有关注
    /// </summary>
    public string TailGuideText { get; set; }
    /// <summary>
    /// 尾标类型，例如：1
    /// </summary>
    public uint TailType { get; set; }
  }

  /// <summary>
  /// Message6 信息
  /// </summary>
  public class Message6
  {
    public int F1 { get; set; }
    /// <summary>
    /// 时间字符串，例如：2025-07-27 23:59:59
    /// </summary>
    public string F2 { get; set; }
  }
}
