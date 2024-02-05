namespace SevenZip
{
    using Dokan2.Archive;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;

#if UNMANAGED

    /// <summary>
    /// A class that has DisposeStream property.
    /// </summary>
    internal class DisposeVariableWrapper
    {
        public bool DisposeStream { protected get; set; }

        protected DisposeVariableWrapper(bool disposeStream) { DisposeStream = disposeStream; }
    }

    /// <summary>
    /// Stream wrapper used in InStreamWrapper
    /// </summary>
    internal class StreamWrapper : DisposeVariableWrapper, IDisposable
    {
        /// <summary>
        /// Worker stream for reading, writing and seeking.
        /// </summary>
        protected IDisposable _baseStream;

        /// <summary>
        /// Initializes a new instance of the StreamWrapper class
        /// </summary>
        /// <param name="baseStream">Worker stream for reading, writing and seeking</param>
        /// <param name="disposeStream">Indicates whether to dispose the baseStream</param>
        protected StreamWrapper(IDisposable baseStream, bool disposeStream)
            : base(disposeStream)
        {
            _baseStream = baseStream;            
        }

        #region IDisposable Members

        /// <summary>
        /// Cleans up any resources used and fixes file attributes.
        /// </summary>
        public void Dispose()
        {
            if (_baseStream != null && DisposeStream)
            {               
                try
                {
                    _baseStream.Dispose();
                }
                catch (ObjectDisposedException) { }
                _baseStream = null;                                
            }    
            
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    /// <summary>
    /// IInStream wrapper used in stream read operations.
    /// </summary>
    internal sealed class InStreamWrapper : StreamWrapper, ISequentialInStream, IInStream
    {
        private Stream BaseStream => (Stream)_baseStream;
        /// <summary>
        /// Initializes a new instance of the InStreamWrapper class.
        /// </summary>
        /// <param name="baseStream">Stream for writing data</param>
        /// <param name="disposeStream">Indicates whether to dispose the baseStream</param>
        public InStreamWrapper(Stream baseStream, bool disposeStream) : base(baseStream, disposeStream) { }

        #region ISequentialInStream Members

        /// <summary>
        /// Reads data from the stream.
        /// </summary>
        /// <param name="data">A data array.</param>
        /// <param name="size">The array size.</param>
        /// <returns>The read bytes count.</returns>
        public int Read(byte[] data, uint size)
        {
            int readCount = 0;
            if (BaseStream != null)
            {
                readCount = BaseStream.Read(data, 0, (int) size);
            }
            return readCount;
        }

        public void Seek(long offset, SeekOrigin seekOrigin, IntPtr newPosition)
        {
            if (BaseStream != null)
            {
                long position = BaseStream.Seek(offset, seekOrigin);
                if (newPosition != IntPtr.Zero)
                {
                    Marshal.WriteInt64(newPosition, position);
                }
            }
        }

        #endregion

    }

    /// <summary>
    /// IOutStream wrapper used in stream write operations.
    /// </summary>
    internal sealed class OutStreamWrapper : StreamWrapper, ISequentialOutStream, IOutStream
    {
        public int ResultCode = 0;

        /// <summary>
        /// Gets the worker stream for reading, writing and seeking.
        /// </summary>
        private MemoryStreamInternal BaseStream => (MemoryStreamInternal)_baseStream;

        /// <summary>
        /// Initializes a new instance of the OutStreamWrapper class
        /// </summary>
        /// <param name="baseStream">Stream for writing data</param>
        /// <param name="disposeStream">Indicates whether to dispose the baseStream</param>
        public OutStreamWrapper(MemoryStreamInternal baseStream, bool disposeStream) :
            base(baseStream, disposeStream) {}

        #region IOutStream Members

        public int SetSize(long newSize)
        {
            return 0;
        }

        public void Seek(long offset, SeekOrigin seekOrigin, IntPtr newPosition)
        {
        }
        #endregion

        #region ISequentialOutStream Members

        /// <summary>
        /// Writes data to the stream
        /// </summary>
        /// <param name="data">Data array</param>
        /// <param name="size">Array size</param>
        /// <param name="processedSize">Count of written bytes</param>
        /// <returns>Zero if Ok</returns>
        public int Write(IntPtr data, uint size, IntPtr processedSize)
        {
            BaseStream.Write(data, 0, (int) size);
            if (processedSize != IntPtr.Zero)
            {
                Marshal.WriteInt32(processedSize, (int) size);
            }
            return ResultCode;
        }

        #endregion
    }

    /// <summary>
    /// Base multi volume stream wrapper class.
    /// </summary>
    internal class MultiStreamWrapper : DisposeVariableWrapper, IDisposable
    {
        protected readonly Dictionary<int, KeyValuePair<long, long>> StreamOffsets = new Dictionary<int, KeyValuePair<long, long>>();

        protected readonly List<Stream> Streams = new List<Stream>();
        protected int CurrentStream;
        protected long Position;
        protected long StreamLength;

        /// <summary>
        /// Initializes a new instance of the MultiStreamWrapper class.
        /// </summary>
        /// <param name="dispose">Perform Dispose() if requested to.</param>
        protected MultiStreamWrapper(bool dispose) : base(dispose) {}

        /// <summary>
        /// Gets the total length of input data.
        /// </summary>
        public long Length => StreamLength;

        #region IDisposable Members

        /// <summary>
        /// Cleans up any resources used and fixes file attributes.
        /// </summary>
        public virtual void Dispose()
        {
            if (DisposeStream)
            {
                foreach (Stream stream in Streams)
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch (ObjectDisposedException) {}
                }
                Streams.Clear();
            }
            GC.SuppressFinalize(this);
        }

        #endregion

        protected static string VolumeNumber(int num)
        {
            if (num < 10)
            {
                return ".00" + num.ToString(CultureInfo.InvariantCulture);
            }
            if (num > 9 && num < 100)
            {
                return ".0" + num.ToString(CultureInfo.InvariantCulture);
            }
            if (num > 99 && num < 1000)
            {
                return "." + num.ToString(CultureInfo.InvariantCulture);
            }
            return String.Empty;
        }

        private int StreamNumberByOffset(long offset)
        {
            foreach (var pair in StreamOffsets)
            {
                if (pair.Value.Key <= offset &&
                    pair.Value.Value >= offset)
                {
                    return pair.Key;
                }
            }
            return -1;
        }

        public void Seek(long offset, SeekOrigin seekOrigin, IntPtr newPosition)
        {
            long absolutePosition;
            switch (seekOrigin) {
                case SeekOrigin.Begin:
                    absolutePosition = offset;
                    break;
                case SeekOrigin.Current:
                    absolutePosition = Position + offset;
                    break;
                case SeekOrigin.End:
                    absolutePosition = Length + offset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(seekOrigin));
            }
            CurrentStream = StreamNumberByOffset(absolutePosition);
            long delta = Streams[CurrentStream].Seek(
                absolutePosition - StreamOffsets[CurrentStream].Key, SeekOrigin.Begin);
            Position = StreamOffsets[CurrentStream].Key + delta;
            if (newPosition != IntPtr.Zero)
            {
                Marshal.WriteInt64(newPosition, Position);
            }
        }
    }

    /// <summary>
    /// IInStream wrapper used in stream multi volume read operations.
    /// </summary>
    internal sealed class InMultiStreamWrapper : MultiStreamWrapper, ISequentialInStream, IInStream
    {
        /// <summary>
        /// Initializes a new instance of the InMultiStreamWrapper class.
        /// </summary>
        /// <param name="fileName">The archive file name.</param>
        /// <param name="dispose">Perform Dispose() if requested to.</param>
        public InMultiStreamWrapper(string fileName, bool dispose) :
            base(dispose)
        {
            string baseName = fileName.Substring(0, fileName.Length - 4);
            int i = 0;
            while (File.Exists(fileName))
            {
                Streams.Add(new FileStream(fileName, FileMode.Open));
                long length = Streams[i].Length;
                StreamOffsets.Add(i++, new KeyValuePair<long, long>(StreamLength, StreamLength + length));
                StreamLength += length;
                fileName = baseName + VolumeNumber(i + 1);
            }
        }

        #region ISequentialInStream Members

        /// <summary>
        /// Reads data from the stream.
        /// </summary>
        /// <param name="data">A data array.</param>
        /// <param name="size">The array size.</param>
        /// <returns>The read bytes count.</returns>
        public int Read(byte[] data, uint size)
        {
            var readSize = (int) size;
            int readCount = Streams[CurrentStream].Read(data, 0, readSize);
            readSize -= readCount;
            Position += readCount;
            while (readCount < (int) size)
            {
                if (CurrentStream == Streams.Count - 1)
                {
                    return readCount;
                }
                CurrentStream++;
                Streams[CurrentStream].Seek(0, SeekOrigin.Begin);
                int count = Streams[CurrentStream].Read(data, readCount, readSize);
                readCount += count;
                readSize -= count;
                Position += count;
            }
            return readCount;
        }

        #endregion
    }

    internal sealed class FakeOutStreamWrapper : ISequentialOutStream, IDisposable
    {
        public int ResultCode = 0;

        #region IDisposable Members

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        #endregion

        #region ISequentialOutStream Members

        /// <summary>
        /// Does nothing except calling the BytesWritten event
        /// </summary>
        /// <param name="data">Data array</param>
        /// <param name="size">Array size</param>
        /// <param name="processedSize">Count of written bytes</param>
        /// <returns>Zero if Ok</returns>
        public int Write(IntPtr data, uint size, IntPtr processedSize)
        {
            if (processedSize != IntPtr.Zero)
            {
                Marshal.WriteInt32(processedSize, (int) size);
            }
            return ResultCode;
        }

        #endregion
    }
#endif
}