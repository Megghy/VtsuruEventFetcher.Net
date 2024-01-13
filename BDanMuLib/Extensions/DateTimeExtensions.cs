using System;

namespace BDanMuLib.Extensions
{
    public static class DateTimeExtensions
    {
        public static DateTime ConvertStringToDateTime(this string timeStamp)
        {
            DateTime dtStart = new(1970, 1, 1, 8, 0, 0);
            long lTime = long.Parse(timeStamp + "0000");
            TimeSpan toNow = new(lTime);
            return dtStart.Add(toNow);
        }
        public static DateTime ConvertLongToDateTime(this long timeStamp)
        {
            DateTime dtStart = new(1970, 1, 1, 8, 0, 0);
            long lTime = long.Parse(timeStamp + "0000");
            TimeSpan toNow = new(lTime);
            return dtStart.Add(toNow);
        }
        public static DateTime FromUnix(this long timeStamp)
        {
            DateTime dtStart = new(1970, 1, 1, 8, 0, 0);
            TimeSpan toNow = new(timeStamp * 10000 * (timeStamp > 1000000000 && timeStamp < 1000000000000 ? 1000 : 0));
            return dtStart.Add(toNow);
        }
        public static readonly DateTime UnixStartDate = new(1970, 1, 1, 8, 0, 0, 0);
        public static long ToUnix(this DateTime time)
        {
            var min = new DateTime(1970, 1, 1, 8, 0, 0);
            if (time < min)
                return 0;
            else
                return (time - UnixStartDate).Ticks / 10000;
        }
    }
}
