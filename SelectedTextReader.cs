using System;
using System.Threading;
using System.Threading.Tasks;

namespace JsonRightClick
{
    /// <summary>先直接读取已确认的选区，再关闭原菜单；避免 Esc 清除选区后 Ctrl+C 什么也复制不到。</summary>
    static class SelectedTextReader
    {
        internal const int MaxCharacters = 20 * 1024 * 1024;
        static readonly Reader runner = new Reader();
        internal static bool Accept(string text) { return !string.IsNullOrEmpty(text) && text.Length <= MaxCharacters; }
        internal sealed class Reader
        {
            int running;
            internal Task<string> Read(Func<string> read)
            {
                if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return Task.FromResult<string>(null);
                return Task.Run(() =>
                {
                    try { string text = read(); return Accept(text) ? text : null; }
                    catch { return null; }
                    finally { Interlocked.Exchange(ref running, 0); }
                });
            }
        }
        internal static Task<string> ReadAsync(SelectionTarget target)
        {
            return runner.Read(() =>
            {
                if (target == null || !target.IsCurrent) return null;
                string text = null;
                SelectionProbe.Check(target, value => text = value);
                return target.IsCurrent ? text : null;
            });
        }
        internal static string CompletedText(Task<string> task)
        {
            if (task == null || task.Status != TaskStatus.RanToCompletion) return null;
            return Accept(task.Result) ? task.Result : null;
        }
        internal static Task<string> BeforeDismissAsync(SelectionTarget target)
        { return BeforeDismissAsync(() => ReadAsync(target), Task.Delay); }
        internal static async Task<bool> OpenBeforeCopyAsync(Func<Task<string>> read, Func<bool> isCurrent, Func<Task> dismiss, Action<string> open)
        {
            string text = await read();
            if (!Accept(text) || !isCurrent()) return false;
            // 保留的是刚刚取到的完整原文；dismiss 即使清掉源选区，也不能让它丢失。
            await dismiss();
            open(text);
            return true;
        }
        // 可注入接口用于验证“先读选区，后 Esc/复制”的真实取词顺序，不触碰用户剪贴板。
        internal static async Task<string> BeforeDismissAsync(Func<Task<string>> read, Func<int, Task> delay)
        {
            var task = read();
            await Task.WhenAny(task, delay(1200));
            return CompletedText(task);
        }
    }
}
