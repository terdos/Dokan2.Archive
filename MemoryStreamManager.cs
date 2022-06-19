using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static Shaman.Dokan.FileSystemBase;

namespace Shaman.Dokan
{
    public sealed class MemoryStreamManager
    {
        private Action<FsNode, MemoryStreamInternal> write;
        public readonly FsNode Item;
        public long Length;

        private MemoryStreamInternal ms;

        private volatile bool completed;
        public MemoryStreamManager(Action<FsNode, MemoryStreamInternal> load, FsNode item)
        {
            write = load;
            Item = item;
            Length = (long)item.Info.Size;
        }

        public ConsumerStream CreateStream()
        {
            return new ConsumerStream(this);
        }

        internal int Read(long position, IntPtr buffer, int offset, int count)
        {
            if (exception != null) throw new Exception(exception);
            var waitTime = 8;
            while (ms.Length < position + count && !completed)
            {
                Interlocked.MemoryBarrier();
                Thread.Sleep(waitTime);
                waitTime *= 2;
                if (exception != null) throw new Exception(exception);
            }

            lock (ms)
            {
                var data = ms.data;
                var tocopy = (int)Math.Min(count, ms.Length - position);
                if (position > ms.length) return 0;
                if (tocopy <= 0)
                    return 0;
                Marshal.Copy(data, (int)position, buffer + offset, tocopy);
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
                    if (MemoryStreamCache.TotalActive >= 20)
                    {
                        MemoryStreamCache.DoGC(1000);
                        if (MemoryStreamCache.TotalActive >= 30)
                        {
                            isdisposed = true;
                            this.ms.Dispose();
                            MemoryStreamCache.DeleteItem(Item, this);
                            if (SevenZipProgram.DebugSelf)
                                Console.WriteLine("Release reading buffer for 0x{0:X} at once"
                                    , Item.GetHashCode());
                            return;
                        }
                    }
                    var tok = this.usageToken;
                    var timeout = MemoryStreamCache.TotalActive >= 12 ? 3000 : MaxKeepFileInMemoryTimeMs;
                    MemoryStreamCache.DelayGC(timeout, () =>
                    {
                        if (tok != this.usageToken) return;
                        lock (this)
                        {
                            if (tok == this.usageToken)
                            {
                                isdisposed = true;
                                this.ms.Dispose();
                                MemoryStreamCache.DeleteItem(Item, this);
                                if (SevenZipProgram.DebugSelf)
                                    Console.WriteLine("Release reading buffer for 0x{0:X} and there're {1} left"
                                        , Item.GetHashCode(), MemoryStreamCache.TotalActive);
                            }
                        }
                    });
                }
            }
        }
        private volatile string exception;
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
                            ms.DoAlloc();
                            if (SevenZipProgram.DebugSelf)
                                Console.WriteLine("Now extract {0} bytes from 0x{1:X} in Worker #{2:X}"
                                    , Length, Item.GetHashCode(), Thread.CurrentThread.ManagedThreadId);
                            write(Item, ms);
                        }
                        catch (Exception ex)
                        {
                            // if isdisposed, then `ex` is because the Dokan2 output stream was closed
                            this.exception = ex.GetType().Name + ": " + ex.Message;
                            if (SevenZipProgram.VerboseOutput && !isdisposed)
                                Console.WriteLine("Error: Can not extract 0x{0:X}: {1}", Item.GetHashCode()
                                    , this.exception);
                        }
                        finally
                        {
                            if (SevenZipProgram.DebugSelf && exception == null)
                                Console.WriteLine("Got {0} {1} bytes from 0x{2:X}"
                                    , ms.length == Length ? "all of" : "only " + ms.length + " in"
                                    , Length, Item.GetHashCode());

                            this.completed = true;
                        }
                    });
                }
            }
        }

        private int users;
        private bool isdisposed;
        //[Configuration]
        public const int MaxKeepFileInMemoryTimeMs = 30000;

        public bool IsDisposed => isdisposed;
    }

    public sealed class ConsumerStream : Stream
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
            throw new NotSupportedException();
        }

        public int Read(IntPtr buffer, int offset, int count)
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