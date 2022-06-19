using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static Shaman.Dokan.FileSystemBase;

namespace Shaman.Dokan
{
    public class MemoryStreamManager
    {
        private Action<FsNode, Stream> write;
        public readonly FsNode Item;
        public long Length;

        private MemoryStreamInternal ms;

        private volatile bool completed;
        public MemoryStreamManager(Action<FsNode, Stream> load, FsNode item)
        {
            write = load;
            Item = item;
            Length = (long)item.Info.Size;
        }

        public Stream CreateStream()
        {
            return new ConsumerStream(this);
        }

        internal int Read(long position, byte[] buffer, int offset, int count)
        {
            if (exception != null) throw exception;
            var waitTime = 8;
            while (ms.Length < position + count && !completed)
            {
                Interlocked.MemoryBarrier();
                Thread.Sleep(waitTime);
                waitTime *= 2;
                if (exception != null) throw exception;
            }

            lock (ms)
            {
                var data = ms.data;
                var tocopy = Math.Max((int)Math.Min(count, ms.Length - position), 0);
                if (position > ms.length) return 0;
                Buffer.BlockCopy(data, (int)position, buffer, offset, tocopy);
                return tocopy;
            }

        }
        private int usageToken;
        internal void DecrementUsage()
        {
            lock (this)
            {
                users--;
                if (users == 0)
                {
                    if (MemoryStreamCache.Total >= 50)
                    {
                        isdisposed = true;
                        this.ms.Dispose();
                        MemoryStreamCache.DeleteItem(Item, this);
                        return;
                    }
                    var tok = this.usageToken; 
                    Timer timer = null;
                    timer = new System.Threading.Timer(dummy =>
                    {
                        timer.Dispose();
                        lock (this)
                        {
                            if (tok == this.usageToken)
                            {
                                isdisposed = true;
                                this.ms.Dispose();
                                MemoryStreamCache.DeleteItem(Item, this);
                            }
                        }
                    }, null, MemoryStreamCache.Total > 16 ? 3000 :
                    Configuration_KeepFileInMemoryTimeMs, Timeout.Infinite);
                }
            }
        }
        private volatile Exception exception;
        internal void IncrementUsage()
        {
            lock (this)
            {
                usageToken++;
                users++;
                if (ms == null)
                {
                    ms = new MemoryStreamInternal((int)Length);
                    Task.Run(() =>
                    {
                        try
                        {
                            write(Item, ms);
                            if (Length != ms.length)
                                throw new Exception("Promised length was different from actual length.");
                            this.Length = ms.length;
                        }
                        catch (Exception ex)
                        {
                            this.exception = ex;
                        }
                        finally
                        {
                            this.completed = true;
                        }
                    });
                }
            }
        }

        private int users;
        private bool isdisposed;
        //[Configuration]
        private static int Configuration_KeepFileInMemoryTimeMs = 30000;

        public bool IsDisposed => isdisposed;
    }

    internal class ConsumerStream : Stream
    {
        private MemoryStreamManager memoryStreamManager;
        private long position;
        static private int lastId;
        private int id;
        public ConsumerStream(MemoryStreamManager memoryStreamManager)
        {
            this.memoryStreamManager = memoryStreamManager;
            memoryStreamManager.IncrementUsage();
            id = Interlocked.Increment(ref lastId);
            //Console.WriteLine("Open: " + id);
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => memoryStreamManager.Length;

        public override long Position
        {
            get => position;
            set
            {
                if (position < 0) throw new ArgumentException();
                if (position > memoryStreamManager.Length) throw new ArgumentException();
                position = value;
            }
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (released != 0) return 0;
            var r = memoryStreamManager.Read(position, buffer, offset, count);
            position += r;
            return r;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin) Position = offset;
            else if (origin == SeekOrigin.Current) Position += offset;
            else if (origin == SeekOrigin.End) Position = Length + offset;
            else throw new ArgumentException();
            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }


        private int released;
        public override void Close()
        {
            if (Interlocked.Increment(ref released) == 1)
            {
                //Console.WriteLine("Close: " + id);
                memoryStreamManager.DecrementUsage();
            }
        }
    }
}