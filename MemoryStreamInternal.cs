using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Dokan2.Archive
{
    public sealed class MemoryStreamInternal: IDisposable
    {
        public MemoryStreamInternal(int length)
        {
            this.capacity = length;
        }

        public readonly int capacity;
        public volatile int length;
        internal volatile byte[] data;

        public void Write(IntPtr buffer, int offset, int count)
        {
            lock (this)
            {
                var newlength = length + count;
                if (newlength > data.Length)
                {
                    var newdata = new byte[Math.Max(newlength, (long)(data.Length * 1.414))];
                    Buffer.BlockCopy(data, 0, newdata, 0, length);
                    data = newdata;
                };
                Marshal.Copy(buffer + offset, data, length, count);
                length = newlength;
            }
        }

        internal void DoAlloc()
        {
            data = new byte[capacity];
        }

        public void Dispose()
        {
            data = null;
        }

    }
}
