namespace SevenZip
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    using SevenZip.Sdk.Compression.Lzma;
    using Shaman.Dokan;
    using static Shaman.Dokan.FileSystemBase;

    /// <summary>
    /// Class to unpack data from archives supported by 7-Zip.
    /// </summary>
    /// <example>
    /// using (var extr = new SevenZipExtractor(@"C:\Test.7z"))
    /// {
    ///     extr.ExtractArchive(@"C:\TestDirectory");
    /// }
    /// </example>
    public sealed partial class SevenZipExtractor
#if UNMANAGED
        : SevenZipBase, IDisposable
#endif
    {
#if UNMANAGED
        public delegate void GetFileData(uint index, ref ArchiveFileInfo info, bool isDir);
        public IInArchive _archive;
        private IInStream _archiveStream;
        private int _offset;
        private ArchiveOpenCallback _openCallback;
        private string _fileName;
        private Stream _inStream;
        private long? _packedSize;
        //private long? _unpackedSize;
        private uint _filesCount;
        private bool _isSolid;
        private bool _opened;
        private bool _disposed;
        private InArchiveFormat _format = (InArchiveFormat)(-1);

        #region Constructors

        /// <summary>
        /// General initialization function.
        /// </summary>
        /// <param name="archiveFullName">The archive file name.</param>
        private void Init(string archiveFullName)
        {
            _fileName = archiveFullName;
            var isExecutable = false;
            
            if ((int)_format == -1)
            {
                _format = FileChecker.CheckSignature(archiveFullName, out _offset, out isExecutable);
            }
            
            PreserveDirectoryStructure = true;
            SevenZipLibraryManager.LoadLibrary(this, _format);
            
            try
            {
                _archive = SevenZipLibraryManager.InArchive(_format, this);
            }
            catch (SevenZipLibraryException)
            {
                SevenZipLibraryManager.FreeLibrary(this, _format);
                throw;
            }
            
            if (isExecutable && _format != InArchiveFormat.PE)
            {
                if (!Check())
                {
                    CommonDispose();
                    _format = InArchiveFormat.PE;
                    SevenZipLibraryManager.LoadLibrary(this, _format);
                    
                    try
                    {
                        _archive = SevenZipLibraryManager.InArchive(_format, this);
                    }
                    catch (SevenZipLibraryException)
                    {
                        SevenZipLibraryManager.FreeLibrary(this, _format);
                        throw;
                    }
                }
            }
        }

        public void SetProperties(params string[] args)
        {
            SetProperties(args, null);
        }

        public void SetProperties(string[] args, PropVariant[] props = null)
        {
            if (args.Length == 0)
                return;
            var setter = _archive as ISetProperties;
            if (setter == null)
            {
                Console.Error.WriteLine("The format {0} doesn't support property setter", _format);
                return;
            }
            if (props == null)
            {
                props = new PropVariant[args.Length];
                foreach (var prop in props)
                    prop.UnsafeClear();
            }
            int ret = setter.SetProperties(args, props, (uint)args.Length);
            if (ret != 0)
                Console.Error.WriteLine("The format {0} doesn't support {1}: error = {2}"
                    , _format, string.Join(", ", args), ret);
        }

        /// <summary>
        /// General initialization function.
        /// </summary>
        /// <param name="stream">The stream to read the archive from.</param>
        private void Init(Stream stream)
        {
            ValidateStream(stream);
            var isExecutable = false;
            
            if ((int)_format == -1)
            {
                _format = FileChecker.CheckSignature(stream, out _offset, out isExecutable);
            }            
            
            PreserveDirectoryStructure = true;
            SevenZipLibraryManager.LoadLibrary(this, _format);
            
            try
            {
                _inStream = new ArchiveEmulationStreamProxy(stream, _offset);
				_packedSize = stream.Length;
                _archive = SevenZipLibraryManager.InArchive(_format, this);
            }
            catch (SevenZipLibraryException)
            {
                SevenZipLibraryManager.FreeLibrary(this, _format);
                throw;
            }
            
            if (isExecutable && _format != InArchiveFormat.PE)
            {
                if (!Check())
                {
                    CommonDispose();
                    _format = InArchiveFormat.PE;
                    
                    try
                    {
                        _inStream = new ArchiveEmulationStreamProxy(stream, _offset);
                        _packedSize = stream.Length;
                        _archive = SevenZipLibraryManager.InArchive(_format, this);
                    }
                    catch (SevenZipLibraryException)
                    {
                        SevenZipLibraryManager.FreeLibrary(this, _format);
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of SevenZipExtractor class.
        /// </summary>
        /// <param name="archiveStream">The stream to read the archive from.
        /// Use SevenZipExtractor(string) to extract from disk, though it is not necessary.</param>
        /// <remarks>The archive format is guessed by the signature.</remarks>
        public SevenZipExtractor(Stream archiveStream)
        {
            Init(archiveStream);
        }

        /// <summary>
        /// Initializes a new instance of SevenZipExtractor class.
        /// </summary>
        /// <param name="archiveStream">The stream to read the archive from.
        /// Use SevenZipExtractor(string) to extract from disk, though it is not necessary.</param>
        /// <param name="format">Manual archive format setup. You SHOULD NOT normally specify it this way.
        /// Instead, use SevenZipExtractor(Stream archiveStream), that constructor
        /// automatically detects the archive format.</param>
        public SevenZipExtractor(Stream archiveStream, InArchiveFormat format)
        {
            _format = format;
            Init(archiveStream);
        }

        /// <summary>
        /// Initializes a new instance of SevenZipExtractor class.
        /// </summary>
        /// <param name="archiveFullName">The archive full file name.</param>
        public SevenZipExtractor(string archiveFullName)
        {
            Init(archiveFullName);
        }

        /// <summary>
        /// Initializes a new instance of SevenZipExtractor class.
        /// </summary>
        /// <param name="archiveFullName">The archive full file name.</param>
        /// <param name="format">Manual archive format setup. You SHOULD NOT normally specify it this way.
        /// Instead, use SevenZipExtractor(string archiveFullName), that constructor
        /// automatically detects the archive format.</param>
        public SevenZipExtractor(string archiveFullName, InArchiveFormat format)
        {
            _format = format;
            Init(archiveFullName);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets archive full file name
        /// </summary>
        public string FileName
        {
            get
            {
                DisposedCheck();

                return _fileName;
            }
        }


        /// <summary>
        /// Gets a value indicating whether the archive is solid
        /// </summary>
        public bool IsSolid => _isSolid;

        /// <summary>
        /// Gets the number of files in the archive
        /// </summary>
        // [CLSCompliant(false)]
        public uint FilesCount => _filesCount;

        /// <summary>
        /// Gets archive format
        /// </summary>
        public InArchiveFormat Format
        {
            get
            {
                DisposedCheck();
                
                return _format;
            }
        }

        internal bool IsEncrypted(FsNode item, ref PropVariant propVar)
        {
            _archive.GetProperty(item.Info.Index, ItemPropId.Encrypted, ref propVar);
            return propVar.EnsuredBool;
        }

        /// <summary>
        /// Gets or sets the value indicating whether to preserve the directory structure of extracted files.
        /// </summary>
        public bool PreserveDirectoryStructure { get; set; }
        
        #endregion                

        /// <summary>
        /// Checked whether the class was disposed.
        /// </summary>
        /// <exception cref="System.ObjectDisposedException" />
        private void DisposedCheck()
        {
            // if (_disposed)
            // {
            //     throw new ObjectDisposedException("SevenZipExtractor");
            // }
        }

        #region Core private functions

        private ArchiveOpenCallback GetArchiveOpenCallback()
        {
            return _openCallback ?? (_openCallback = new ArchiveOpenCallback(_fileName));
        }

        /// <summary>
        /// Gets the archive input stream.
        /// </summary>
        /// <returns>The archive input wrapper stream.</returns>
        private IInStream GetArchiveStream(bool dispose)
        {
            if (_archiveStream != null)
            {
                if (_archiveStream is DisposeVariableWrapper)
                {
                    (_archiveStream as DisposeVariableWrapper).DisposeStream = dispose;
                }
                return _archiveStream;
            }

            if (_inStream != null)
            {
                _inStream.Seek(0, SeekOrigin.Begin);
                _archiveStream = new InStreamWrapper(_inStream, false);
            }
            else
            {
                if (!_fileName.EndsWith(".001", StringComparison.Ordinal))
                {
                    _archiveStream = new InStreamWrapper(
                        new ArchiveEmulationStreamProxy(new FileStream(
                            _fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
                            _offset),
                        dispose);
                }
                else
                {
                    _archiveStream = new InMultiStreamWrapper(_fileName, dispose);
                    _packedSize = (_archiveStream as InMultiStreamWrapper).Length;
                }
            }

            return _archiveStream;
        }

        /// <summary>
        /// Opens the archive and throws exceptions or returns OperationResult.DataError if any error occurs.
        /// </summary>       
        /// <param name="archiveStream">The IInStream compliant class instance, that is, the input stream.</param>
        /// <param name="openCallback">The ArchiveOpenCallback instance.</param>
        /// <returns>OperationResult.Ok if Open() succeeds.</returns>
        private OperationResult OpenArchiveInner(IInStream archiveStream, IArchiveOpenCallback openCallback)
        {
            ulong checkPos = 1 << 15;
            var res = _archive.Open(archiveStream, ref checkPos, openCallback);
            
            return (OperationResult)res;
        }

        /// <summary>
        /// Opens the archive and throws exceptions or returns OperationResult.DataError if any error occurs.
        /// </summary>
        /// <param name="archiveStream">The IInStream compliant class instance, that is, the input stream.</param>
        /// <param name="openCallback">The ArchiveOpenCallback instance.</param>
        /// <returns>True if Open() succeeds; otherwise, false.</returns>
        private bool OpenArchive(IInStream archiveStream, ArchiveOpenCallback openCallback)
        {
            if (!_opened)
            {
                if (OpenArchiveInner(archiveStream, openCallback) != OperationResult.Ok)
                {
                    if (!ThrowException(null, new SevenZipArchiveException()))
                    {
                        return false;
                    }
                }
                
                _opened = true;
            }

            return true;
        }

        /// <summary>
        /// Retrieves all information about the archive.
        /// </summary>
        /// <exception cref="SevenZip.SevenZipArchiveException"/>
        public GetFileData GetArchiveInfo()
        {
            bool disposeStream = false;
            if (_archive == null)
            {
                ThrowException(null, new SevenZipArchiveException());
                return null;
            }
            {
                IInStream archiveStream;

                if (!_opened)
                    using ((archiveStream = GetArchiveStream(disposeStream)) as IDisposable)
                    {
                        var openCallback = GetArchiveOpenCallback();
                        if (!OpenArchive(archiveStream, openCallback))
                        {
                            return null;
                        }
                        if (openCallback.HasExceptions)
                            Console.WriteLine(">>> test 123", openCallback.Exceptions[0].ToString());
                        //openCallback.ThrowException();
                        _opened = !disposeStream;
                    }
                _filesCount = _archive.GetNumberOfItems();

                if (_filesCount == 0)
                    return null;
                {
                    if (_filesCount > 999 || Shaman.Dokan.SevenZipProgram.VerboseOutput)
                        Console.WriteLine("  Parsing {0} files in the archive ...", _filesCount);
                    var data = new PropVariant();

                    #region Getting archive properties
                    if (_format == InArchiveFormat.SevenZip)
                    {
                        _archive.GetArchiveProperty(ItemPropId.Solid, ref data);
                        var solid = data.OptionalBool;
                        if (solid is bool bSolid)
                            _isSolid = bSolid;
                    }
                    #endregion

                    #region Getting archive items data
                    {
                        return (uint i, ref ArchiveFileInfo fileInfo, bool isDir) =>
                        {
                            fileInfo.Index = i;
                            _archive.GetProperty(i, ItemPropId.LastWriteTime, ref data);
                            fileInfo.LastWriteTime = data.EnsuredDateTime;
                            _archive.GetProperty(i, ItemPropId.CreationTime, ref data);
                            fileInfo.CreationTime = data.EnsuredDateTime;
                            _archive.GetProperty(i, ItemPropId.Attributes, ref data);
                            var attrs = data.EnsuredUInt;
                            attrs = isDir ? (attrs | (uint)FileAttributes.Directory) & 2047
                                : attrs & (~(uint)FileAttributes.Directory & 2047);
                            if (!isDir)
                            {
                                _archive.GetProperty(i, ItemPropId.Size, ref data);
                                fileInfo.Size = data.EnsuredSize;
                                _archive.GetProperty(i, ItemPropId.Encrypted, ref data);
                                if (data.EnsuredBool)
                                    attrs |= (uint)FileAttributes.Encrypted;
                                // if (_isSolid)
                                // {
                                //     _archive.GetProperty(i, ItemPropId.Method, ref data);
                                //     if (data.isLiteralStrCopy())
                                //         attrs |= (uint)FileAttributes.Compressed;
                                // }
                            }
                            fileInfo.Attributes = attrs;
                        };
                    }

                    #endregion

                }
            }
        }

        private void ArchiveExtractCallbackCommonInit(ArchiveExtractCallback aec)
        {
            // aec.Open += ((s, e) => { _unpackedSize = (long)e.TotalSize; });
            // aec.FileExtractionStarted += FileExtractionStartedEventProxy;
            // aec.FileExtractionFinished += FileExtractionFinishedEventProxy;
            // aec.Extracting += ExtractingEventProxy;
            // aec.FileExists += FileExistsEventProxy;
        }

        /// <summary>
        /// Gets the IArchiveExtractCallback callback
        /// </summary>
        /// <param name="directory">The directory where extract the files</param>
        /// <param name="filesCount">The number of files to be extracted</param>
        /// <param name="actualIndexes">The list of actual indexes (solid archives support)</param>
        /// <returns>The ArchiveExtractCallback callback</returns>
        private ArchiveExtractCallback GetArchiveExtractCallback(string directory, int filesCount, List<uint> actualIndexes)
        {
            var aec =
                new ArchiveExtractCallback(_archive, "", filesCount, PreserveDirectoryStructure, actualIndexes, this);
            ArchiveExtractCallbackCommonInit(aec);

            return aec;
        }

        /// <summary>
        /// Gets the IArchiveExtractCallback callback
        /// </summary>
        /// <param name="stream">The stream where extract the file</param>
        /// <param name="index">The file index</param>
        /// <param name="filesCount">The number of files to be extracted</param>
        /// <returns>The ArchiveExtractCallback callback</returns>
        private ArchiveExtractCallback GetArchiveExtractCallback(MemoryStreamInternal stream, uint index, int filesCount)
        {
            var aec = new ArchiveExtractCallback(_archive, stream, filesCount, index, this);
            ArchiveExtractCallbackCommonInit(aec);

            return aec;
        }

        #endregion        
#endif

        /// <summary>
        /// Checks if the specified stream supports extraction.
        /// </summary>
        /// <param name="stream">The stream to check.</param>
        private static void ValidateStream(Stream stream)
        {
			if (stream == null)
			{
				throw new ArgumentNullException(nameof(stream));
			}

            if (!stream.CanSeek || !stream.CanRead)
            {
                throw new ArgumentException("The specified stream can not seek or read.", nameof(stream));
            }

            if (stream.Length == 0)
            {
                throw new ArgumentException("The specified stream has zero length.", nameof(stream));
            }
        }

#if UNMANAGED

        #region IDisposable Members

        private void CommonDispose()
        {
            if (_opened)
            {
                try
                {
                    _archive?.Close();
                }
                catch (Exception) { }
            }

            _archive = null;
            
	        if (_inStream != null)
	        {
                _inStream.Dispose();
                _inStream = null;
	        }
                
	        if (_openCallback != null)
            {
                try
                {
                    _openCallback.Dispose();
                }
                catch (ObjectDisposedException) { }
                _openCallback = null;
            }
            
            if (_archiveStream != null)
            {
                if (_archiveStream is IDisposable)
                {
                    try
                    {
                        if (_archiveStream is DisposeVariableWrapper)
                        {
                            (_archiveStream as DisposeVariableWrapper).DisposeStream = true;
                        }

                        (_archiveStream as IDisposable).Dispose();
                    }
                    catch (ObjectDisposedException) { }
                    _archiveStream = null;
                }
            }

            SevenZipLibraryManager.FreeLibrary(this, _format);
        }

        /// <summary>
        /// Releases the unmanaged resources used by SevenZipExtractor.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {                
                CommonDispose();
            }

            _disposed = true;            
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Core public Members

        #region Events


        #region Event proxies

        /// <summary>
        /// Event proxy for FileExtractionStarted.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void FileExtractionStartedEventProxy(object sender, FileInfoEventArgs e)
        {
        }

        /// <summary>
        /// Event proxy for FileExtractionFinished.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void FileExtractionFinishedEventProxy(object sender, FileInfoEventArgs e)
        {
        }

        /// <summary>
        /// Event proxy for Extracting.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void ExtractingEventProxy(object sender, ProgressEventArgs e)
        {
        }

        /// <summary>
        /// Event proxy for FileExists.
        /// </summary>
        /// <param name="sender">The sender of the event.</param>
        /// <param name="e">The event arguments.</param>
        private void FileExistsEventProxy(object sender, FileOverwriteEventArgs e)
        {
        }

        #endregion

        #endregion

        /// <summary>
        /// Performs the archive integrity test.
        /// </summary>
        /// <returns>True is the archive is ok; otherwise, false.</returns>
        public bool Check()
        {
            DisposedCheck();

            try
            {
                var archiveStream = GetArchiveStream(true);
                var openCallback = GetArchiveOpenCallback();
                
                if (!OpenArchive(archiveStream, openCallback))
                {
                    return false;
                }

                using (var aec = GetArchiveExtractCallback("", (int)_filesCount, null))
                {
                    //try
                    {
                        CheckedExecute(
                            _archive.Extract(null, uint.MaxValue, 1, aec),
                            SevenZipExtractionFailedException.DEFAULT_MESSAGE, aec);
                    }
                    // finally
                    // {
                    //     FreeArchiveExtractCallback(aec);
                    // }
                }
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _archive?.Close();
                if (_archiveStream is IDisposable)
                    ((IDisposable)_archiveStream).Dispose();
                _archiveStream = null;
                _opened = false;
            }

            return true;
        }

        #region ExtractFile overloads

        /// <summary>
        /// Unpacks the file by its index to the specified stream.
        /// </summary>
        /// <param name="index">Index in the archive file table.</param>
        /// <param name="stream">The stream where the file is to be unpacked.</param>
        public void ExtractFile(FsNode file, MemoryStreamInternal stream)
        {
            DisposedCheck();
            ClearExceptions();
            var index = file.Info.Index;

            // var archiveStream = GetArchiveStream(false);
            // var openCallback = GetArchiveOpenCallback();
            // 
            // if (!OpenArchive(archiveStream, openCallback))
            // {
            //     return;
            // }

            //try
            {
                var indexes = new[] { index };
                
                using (var aec = GetArchiveExtractCallback(stream, index, indexes.Length))
                {
                    //try
                    {
                        CheckedExecute(
                            _archive.Extract(indexes, (uint) indexes.Length, 0, aec),
                            SevenZipExtractionFailedException.DEFAULT_MESSAGE, aec);
                    }
                    // finally
                    // {
                    //     FreeArchiveExtractCallback(aec);
                    // }
                }
            }
            //catch (Exception)
            {
                // if (openCallback.ThrowException())
                // {
                //     throw;
                // }
            }

            //ThrowUserException();
        }

        #endregion

        #endregion

#endif

        #region LZMA SDK functions

        internal static byte[] GetLzmaProperties(Stream inStream, out long outSize)
        {
            var lzmAproperties = new byte[5];

            if (inStream.Read(lzmAproperties, 0, 5) != 5)
            {
                throw new LzmaException();
            }

            outSize = 0;

            for (var i = 0; i < 8; i++)
            {
                var b = inStream.ReadByte();

                if (b < 0)
                {
                    throw new LzmaException();
                }

                outSize |= ((long) (byte) b) << (i << 3);
            }

            return lzmAproperties;
        }

        /// <summary>
        /// Decompress the specified stream (C# inside)
        /// </summary>
        /// <param name="inStream">The source compressed stream</param>
        /// <param name="outStream">The destination uncompressed stream</param>
        /// <param name="inLength">The length of compressed data (null for inStream.Length)</param>
        /// <param name="codeProgressEvent">The event for handling the code progress</param>
        public static void DecompressStream(Stream inStream, Stream outStream, int? inLength, EventHandler<ProgressEventArgs> codeProgressEvent)
        {
            if (!inStream.CanRead || !outStream.CanWrite)
            {
                throw new ArgumentException("The specified streams are invalid.");
            }

            var decoder = new Decoder();
            var inSize = (inLength ?? inStream.Length) - inStream.Position;
            decoder.SetDecoderProperties(GetLzmaProperties(inStream, out var outSize));
            decoder.Code(inStream, outStream, inSize, outSize, new LzmaProgressCallback(inSize, codeProgressEvent));
        }

        /// <summary>
        /// Decompress byte array compressed with LZMA algorithm (C# inside)
        /// </summary>
        /// <param name="data">Byte array to decompress</param>
        /// <returns>Decompressed byte array</returns>
        public static byte[] ExtractBytes(byte[] data)
        {
            using (var inStream = new MemoryStream(data))
            {
                var decoder = new Decoder();
                inStream.Seek(0, 0);

                using (var outStream = new MemoryStream())
                {
                    decoder.SetDecoderProperties(GetLzmaProperties(inStream, out var outSize));
                    decoder.Code(inStream, outStream, inStream.Length - inStream.Position, outSize, null);
                    return outStream.ToArray();
                }
            }
        }

        #endregion

        public bool TryDecrypt(FsNode file)
        {
            DisposedCheck();
            ClearExceptions();
            var index = file.Info.Index;
            var indexes = new[] { index };

            using (var aec = GetArchiveExtractCallback(null, index, indexes.Length))
                //try
                {
                    aec.StopFakeStream();
                    var res = _archive.Extract(indexes, (uint)indexes.Length, 0, aec);
                    if (aec.HasExceptions || res != 0 && res != -88)
                        return false;
                }
                // finally
                // {
                //     FreeArchiveExtractCallback(aec);
                // }
            return true;
        }
    }
}
