using System;
using System.Threading.Tasks;

namespace JsonRightClick
{
    /// <summary>复制时先等格式发布完成，再读取同一版本的剪贴板；绝不退回旧内容。</summary>
    static class SelectionCapture
    {
        internal sealed class Result
        {
            public readonly string Text;
            public readonly uint Sequence;
            public Result(string text, uint sequence) { Text = text; Sequence = sequence; }
        }
        public static Task<Result> ReadCopiedTextAsync(uint before)
        {
            return ReadCopiedTextAsync(before, Native.GetClipboardSequenceNumber, ClipboardUtil.TryGetText, Task.Delay);
        }
        // 延迟和读取接口可注入，回归测试不改用户剪贴板，也不需要模拟系统按键。
        internal static async Task<Result> ReadCopiedTextAsync(uint before, Func<uint> sequence, Func<string> read, Func<int, Task> delay)
        {
            uint observed = before;
            int stable = 0;
            for (int i = 0; i < 80; i++)
            {
                await delay(25);
                uint now = sequence();
                if (now == before) { stable = 0; continue; }
                if (now != observed) { observed = now; stable = 0; }
                // 浏览器可能先清空剪贴板，再依次发布 HTML/Unicode 等格式；40ms 不足以保证就绪。
                if (++stable < 4) continue;
                string text = read();
                uint after = sequence();
                if (after != now) { observed = after; stable = 0; continue; }
                if (!string.IsNullOrEmpty(text)) return new Result(text, now);
            }
            return new Result(null, sequence());
        }
    }
}
