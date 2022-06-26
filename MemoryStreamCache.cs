using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Shaman.Dokan.FileSystemBase;

namespace Shaman.Dokan
{
    public class MemoryStreamCache
    {
        public static volatile int TotalActive = 0;

        public static readonly Dictionary<FsNode, MemoryStreamManager> streams
            = new Dictionary<FsNode, MemoryStreamManager>();
        public ConsumerStream OpenStream(FsNode item)
        {
            lock (streams)
            {
                streams.TryGetValue(item, out var ms);
                if (ms != null)
                {
                    lock (ms)
                    {
                        if (!ms.IsDisposed)
                        {
                            return ms.CreateStream();
                        }
                    }
                }
                else
                {
                    TotalActive++;
                }
                if (SevenZipProgram.DebugSelf > 0)
                    Console.WriteLine("  Now read a file of 0x{0:X} by Thread #{1:X}", item.GetHashCode()
                        , Thread.CurrentThread.GetHashCode());

                ms = new MemoryStreamManager(item);
                streams[item] = ms;
                return ms.CreateStream();

                //Console.WriteLine("  (" + access + ")");
            }
        }

        public static void DeleteItem(FsNode key, MemoryStreamManager value)
        {
            lock (streams)
            {
                if (streams.TryGetValue(key, out var cur) && cur == value)
                {
                    streams.Remove(key);
                    TotalActive--;
                }

            }
        }

        public long GetLength(FsNode item)
        {
            return Math.Max((long)item.Info.Size, 0);
        }

        private static List<Tuple<DateTime, Action>> toGC = new List<Tuple<DateTime, Action>>();
        private static Timer gcTimer;
        private static DateTime lastMinEnd = DateTime.MinValue;
        public static void DelayGC(int timeout, Action gcCallback)
        {
            lock (toGC)
            {
                var now = DateTime.Now;
                var end = now.AddMilliseconds(timeout);
                toGC.Add(new Tuple<DateTime, Action>(end, gcCallback));
                updateTimer(now);
            }
        }

        private static void DoGC(object _) { DoGC(0); }

        public static void DoGC(int tolerance)
        {
            lock (toGC)
            {
                var now = DateTime.Now;
                var allowedMin = now.AddMilliseconds(-tolerance);
                var allowedMax = now.AddMilliseconds(31_000);
                var shouldGC = toGC.Where(i => i.Item1 < allowedMin || i.Item1 > allowedMax).ToArray();
                toGC.RemoveAll(i => i.Item1 < allowedMin || i.Item1 > allowedMax);

                foreach (var cb in shouldGC)
                    cb.Item2();

                if (toGC.Count == 0)
                {
                    if (shouldGC.Length == 0)
                    {
                        gcTimer.Dispose();
                        gcTimer = null;
                    }
                    else
                    {
                        gcTimer.Change(10_000, Timeout.Infinite);
                        lastMinEnd = now.AddMilliseconds(10_000);
                    }
                }
                else
                    updateTimer(DateTime.Now);
            }
        }
        private static void updateTimer(DateTime now)
        {
            var minEnd = toGC.Min(i => i.Item1);
            var delta = Math.Min(Math.Max(1000, (int) (minEnd - now).TotalMilliseconds)
                , MemoryStreamManager.MaxKeepFileInMemoryTimeMs + 1000);
            if (gcTimer == null)
                gcTimer = new Timer(DoGC, null, delta, Timeout.Infinite);
            else
                gcTimer.Change(delta, Timeout.Infinite);
            lastMinEnd = minEnd;
        }
    }
}
