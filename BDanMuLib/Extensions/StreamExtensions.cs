using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace BDanMuLib.Extensions
{
    internal static class StreamExtensions
    {
        /// <summary>
        /// 返回是否还可以继续读
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        internal static async Task<bool> ReadBAsync(this Stream stream, byte[] buffer, int offset, int count)
        {
            if (offset + count > buffer.Length)
                throw new ArgumentException();

            var read = 0;
            while (read < count)
            {
                var available = await stream.ReadAsync(buffer.AsMemory(offset, count - read));

                read += available;
                offset += available;

                if (available == 0)
                {
                    return false;
                }
            }
            return true;
        }
        internal static async Task<bool> ReadBAsync(this PipeReader stream, byte[] buffer, int offset, int count)
        {
            if (offset + count > buffer.Length)
                throw new ArgumentException();

            var read = 0;
            while (read < count)
            {
                var result = await stream.ReadAsync();

                read += (int)result.Buffer.Length;
                offset += (int)result.Buffer.Length;

                if (result.Buffer.Length == 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
