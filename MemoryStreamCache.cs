using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Shaman.Dokan.FileSystemBase;

namespace Shaman.Dokan
{
    public class MemoryStreamCache
    {
        private Action<FsNode, Stream> load;
        public MemoryStreamCache(Action<FsNode, Stream> load)
        {
            this.load = load;
        }

        public static volatile int Total;

        public static readonly Dictionary<FsNode, MemoryStreamManager> streams
            = new Dictionary<FsNode, MemoryStreamManager>();
        public Stream OpenStream(FsNode item)
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
                    Total++;

                ms = new MemoryStreamManager(load, item);
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
                    Total--;
                }

            }
        }

        public long GetLength(FsNode item)
        {
            return Math.Max((long)item.Info.Size, 0);
        }
    }
}
