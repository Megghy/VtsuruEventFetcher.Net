using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BDanMuLib
{
    internal static class Utils
    {
        private static readonly int[] MixinKeyEncTab =
        [
            46,
            47,
            18,
            2,
            53,
            8,
            23,
            32,
            15,
            50,
            10,
            31,
            58,
            3,
            45,
            35,
            27,
            43,
            5,
            49,
            33,
            9,
            42,
            19,
            29,
            28,
            14,
            39,
            12,
            38,
            41,
            13,
            37,
            48,
            7,
            16,
            24,
            55,
            40,
            61,
            26,
            17,
            0,
            1,
            60,
            51,
            30,
            4,
            22,
            25,
            54,
            21,
            56,
            59,
            6,
            63,
            57,
            62,
            11,
            36,
            20,
            34,
            44,
            52
        ];

        //对 imgKey 和 subKey 进行字符顺序打乱编码
        private static string GetMixinKey(string orig)
        {
            return MixinKeyEncTab.Aggregate("", (s, i) => s + orig[i])[..32];
        }
        static DateTime lastGetWbiKey = DateTime.MinValue;
        static string wbiKey;
        static string wbiSubKey;
        public static string EncWbi(Dictionary<string, string> parameters)
        {
            if (DateTime.Now.AddHours(-1) > lastGetWbiKey)
            {
                var result = GetWbiKeys().Result;
                wbiKey = result.Item1;
                wbiSubKey = result.Item2;
                lastGetWbiKey = DateTime.Now;
            }
            string mixinKey = GetMixinKey(wbiKey + wbiSubKey);
            string currTime = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            //添加 wts 字段
            parameters["wts"] = currTime;
            // 按照 key 重排参数
            parameters = parameters.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value);
            //过滤 value 中的 "!'()*" 字符
            parameters = parameters.ToDictionary(
                kvp => kvp.Key,
                kvp => new string(kvp.Value.Where(chr => !"!'()*".Contains(chr)).ToArray())
            );
            // 序列化参数
            string query = new FormUrlEncodedContent(parameters).ReadAsStringAsync().Result;
            //计算 w_rid
            byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(query + mixinKey));
            string wbiSign = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            parameters["w_rid"] = wbiSign;

            return new FormUrlEncodedContent(parameters).ReadAsStringAsync().Result;
        }

        // 获取最新的 img_key 和 sub_key
        private static async Task<(string, string)> GetWbiKeys()
        {
            using HttpClient _client = new HttpClient();
            HttpResponseMessage responseMessage = await _client.SendAsync(new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri("https://api.bilibili.com/x/web-interface/nav"),
            });

            JsonNode response = JsonNode.Parse(await responseMessage.Content.ReadAsStringAsync())!;

            string imgUrl = (string)response["data"]!["wbi_img"]!["img_url"]!;
            imgUrl = imgUrl.Split("/")[^1].Split(".")[0];

            string subUrl = (string)response["data"]!["wbi_img"]!["sub_url"]!;
            subUrl = subUrl.Split("/")[^1].Split(".")[0];
            return (imgUrl, subUrl);
        }
    }
}
