using System;
using Newtonsoft.Json.Linq;

namespace BDanMuLib.Models
{
    public interface IBaseMessage
    {
        public JObject Metadata { get; init; }
    }
    public record BaseMessage : IBaseMessage
    {
        public BaseMessage(JObject json)
        {
            Metadata = json;
        }
        /// <summary>
        /// 原始数据 json字符串
        /// </summary>
        public JObject Metadata { get; init; }
    }
    public abstract record BaseMessage<T> : IBaseMessage
    {
        public BaseMessage(JObject json)
        {
            Metadata = json;
        }
        /// <summary>
        /// 原始数据 json字符串
        /// </summary>
        public JObject Metadata { get; init; }
        public abstract DateTime SendTime { get; }
    }
}
