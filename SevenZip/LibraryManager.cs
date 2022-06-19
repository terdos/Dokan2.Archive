namespace SevenZip
{
    using Microsoft.Win32;
    using Shaman.Dokan;
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
#if NET45 || NETSTANDARD2_0
    using System.Security.Permissions;
#endif
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;

#if UNMANAGED
    /// <summary>
    /// 7-zip library low-level wrapper.
    /// </summary>
    internal static class SevenZipLibraryManager
    {
        /// <summary>
        /// Synchronization root for all locking.
        /// </summary>
        private static readonly object _syncRoot = new object();

        /// <summary>
        /// Path to the 7-zip dll.
        /// </summary>
        /// <remarks>7zxa.dll supports only decoding from .7z archives.
        /// Features of 7za.dll: 
        ///     - Supporting 7z format;
        ///     - Built encoders: LZMA, PPMD, BCJ, BCJ2, COPY, AES-256 Encryption.
        ///     - Built decoders: LZMA, PPMD, BCJ, BCJ2, COPY, AES-256 Encryption, BZip2, Deflate.
        /// 7z.dll (from the 7-zip distribution) supports every InArchiveFormat for encoding and decoding.
        /// </remarks>
        private static string _libraryFileName;

        private static string DetermineLibraryFilePath()
        {
            var dir = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(dir))
            {
                dir = Path.GetDirectoryName(dir);
                if (Environment.Is64BitProcess)
                {
                    var file2 = Path.Combine(dir, "7z64.dll");
                    if (File.Exists(file2))
                        return file2;
                }
                var file = Path.Combine(dir, "7z.dll");
                if (File.Exists(file))
                    return file;
            }
            RegistryKey key1 = null, key2 = null, key3 = null;
            object def = null;
            try
            {
                key1 = Registry.ClassesRoot.OpenSubKey(".7z");
                var key = key1;
                def = key?.GetValue("");
                key2 = def is string def1 && def1.Length > 0 ? Registry.ClassesRoot.OpenSubKey(def1) : null;
                key = key2 ?? key1;
                key3 = key?.OpenSubKey("DefaultIcon");
                def = key3?.GetValue("");
            }
            catch (Exception) { }
            finally
            {
                key3?.Dispose();
                key2?.Dispose();
                key1?.Dispose();
            }
            if (def is string iconPath && iconPath.Length > 0)
            {
                var ind = iconPath.LastIndexOf(',');
                var file = ind >= 0 ? iconPath.Substring(0, ind) : iconPath;
                file = file.Length > 0 && File.Exists(file) ? file : "";
                if (file.Length == 0 && ind != 0)
                {
                    var ind2 = iconPath.IndexOf(',');
                    if (ind2 != ind && ind2 > 0)
                    {
                        file = iconPath.Substring(0, ind2);
                        file = File.Exists(file) ? file : "";
                    }
                }
                if (file.Length > 0)
                {
                    var file2 = Path.Combine(Path.GetDirectoryName(file), "7z.dll");
                    file = file2 != file && File.Exists(file2) ? file2 : file.EndsWith(".dll") ? file : null;
                    if (file != null && SevenZipProgram.VerboseOutput)
                        Console.WriteLine("  Auto import an installed {0}", file);
                    return file;
                }
            }
            return null;
        }

        /// <summary>
        /// 7-zip library handle.
        /// </summary>
        private static IntPtr _modulePtr;

        /// <summary>
        /// 7-zip library features.
        /// </summary>
        private static Dictionary<object, Dictionary<InArchiveFormat, IInArchive>> _inArchives;
        private static Dictionary<object, Dictionary<OutArchiveFormat, IOutArchive>> _outArchives;
        private static int _totalUsers;

        private static void InitUserInFormat(object user, InArchiveFormat format)
        {
            if (!_inArchives.ContainsKey(user))
            {
                _inArchives.Add(user, new Dictionary<InArchiveFormat, IInArchive>());
            }

            if (!_inArchives[user].ContainsKey(format))
            {
                _inArchives[user].Add(format, null);
                _totalUsers++;
            }
        }

        private static void InitUserOutFormat(object user, OutArchiveFormat format)
        {
            if (!_outArchives.ContainsKey(user))
            {
                _outArchives.Add(user, new Dictionary<OutArchiveFormat, IOutArchive>());
            }

            if (!_outArchives[user].ContainsKey(format))
            {
                _outArchives[user].Add(format, null);
                _totalUsers++;
            }
        }

        private static void Init()
        {
            _inArchives = new Dictionary<object, Dictionary<InArchiveFormat, IInArchive>>();
            _outArchives = new Dictionary<object, Dictionary<OutArchiveFormat, IOutArchive>>();
        }

        /// <summary>
        /// Loads the 7-zip library if necessary and adds user to the reference list
        /// </summary>
        /// <param name="user">Caller of the function</param>
        /// <param name="format">Archive format</param>
        public static void LoadLibrary(object user, Enum format)
        {
            lock (_syncRoot)
            {
                if (_inArchives == null || _outArchives == null)
                {
                    Init();
                }

                if (_modulePtr == IntPtr.Zero)
                {
                    if (_libraryFileName == null)
                    {
                        _libraryFileName = DetermineLibraryFilePath();
                    }

                    if (!File.Exists(_libraryFileName))
                    {
                        throw new SevenZipLibraryException("DLL file does not exist.");
                    }

                    if ((_modulePtr = NativeMethods.LoadLibrary(_libraryFileName)) == IntPtr.Zero)
                    {
                        throw new SevenZipLibraryException($"failed to load library from \"{_libraryFileName}\".");
                    }

                    if (NativeMethods.GetProcAddress(_modulePtr, "GetHandlerProperty") == IntPtr.Zero)
                    {
                        NativeMethods.FreeLibrary(_modulePtr);
                        throw new SevenZipLibraryException("library is invalid.");
                    }
                }

                if (format is InArchiveFormat archiveFormat)
                {
                    InitUserInFormat(user, archiveFormat);
                    return;
                }

                if (format is OutArchiveFormat outArchiveFormat)
                {
                    InitUserOutFormat(user, outArchiveFormat);
                    return;
                }

                throw new ArgumentException($"Enum {format} is not a valid archive format attribute!");
            }
        }

        static readonly string Namespace = Assembly.GetExecutingAssembly().GetManifestResourceNames()[0].Split('.')[0];

        /// <summary>
        /// Removes user from reference list and frees the 7-zip library if it becomes empty
        /// </summary>
        /// <param name="user">Caller of the function</param>
        /// <param name="format">Archive format</param>
        public static void FreeLibrary(object user, Enum format)
        {
#if NET45 || NETSTANDARD2_0
            var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
            sp.Demand();
#endif
            lock (_syncRoot)
			{
                if (_modulePtr != IntPtr.Zero)
                {
                    if (format is InArchiveFormat archiveFormat)
                    {
                        if (_inArchives != null && _inArchives.ContainsKey(user) &&
                            _inArchives[user].ContainsKey(archiveFormat) &&
                            _inArchives[user][archiveFormat] != null)
                        {
                            try
                            {                            
                                Marshal.ReleaseComObject(_inArchives[user][archiveFormat]);
                            }
                            catch (InvalidComObjectException) {}
                            
                            _inArchives[user].Remove(archiveFormat);
                            _totalUsers--;
                            
                            if (_inArchives[user].Count == 0)
                            {
                                _inArchives.Remove(user);
                            }
                        }
                    }

                    if (format is OutArchiveFormat outArchiveFormat)
                    {
                        if (_outArchives != null && _outArchives.ContainsKey(user) &&
                            _outArchives[user].ContainsKey(outArchiveFormat) &&
                            _outArchives[user][outArchiveFormat] != null)
                        {
                            try
                            {
                                Marshal.ReleaseComObject(_outArchives[user][outArchiveFormat]);
                            }
                            catch (InvalidComObjectException) {}
                            
                            _outArchives[user].Remove(outArchiveFormat);
                            _totalUsers--;
                            
                            if (_outArchives[user].Count == 0)
                            {
                                _outArchives.Remove(user);
                            }
                        }
                    }

                    if ((_inArchives == null || _inArchives.Count == 0) && (_outArchives == null || _outArchives.Count == 0))
                    {
                        _inArchives = null;
                        _outArchives = null;

                        if (_totalUsers == 0)
                        {
                            NativeMethods.FreeLibrary(_modulePtr);
                            _modulePtr = IntPtr.Zero;
                        }
                    }
                }
			}
        }

        /// <summary>
        /// Gets IInArchive interface to extract 7-zip archives.
        /// </summary>
        /// <param name="format">Archive format.</param>
        /// <param name="user">Archive format user.</param>
        public static IInArchive InArchive(InArchiveFormat format, object user)
        {
            lock (_syncRoot)
            {
                if (_inArchives[user][format] == null)
                {
#if NET45 || NETSTANDARD2_0
                    var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
                    sp.Demand();
#endif

                    if (_modulePtr == IntPtr.Zero)
                    {
                        LoadLibrary(user, format);

                        if (_modulePtr == IntPtr.Zero)
                        {
                            throw new SevenZipLibraryException();
                        }
                    }

                    var createObject = (NativeMethods.CreateObjectDelegate)
                        Marshal.GetDelegateForFunctionPointer(
                            NativeMethods.GetProcAddress(_modulePtr, "CreateObject"),
                            typeof(NativeMethods.CreateObjectDelegate));

                    if (createObject == null)
                    {
                        throw new SevenZipLibraryException();
                    }

                    object result;
                    var interfaceId = typeof(IInArchive).GUID;
                    var classID = Formats.InFormatGuids[format];

                    try
                    {
                        createObject(ref classID, ref interfaceId, out result);
                    }
                    catch (Exception)
                    {
                        throw new SevenZipLibraryException("Your 7-zip library does not support this archive type.");
                    }

                    InitUserInFormat(user, format);									
                    _inArchives[user][format] = result as IInArchive;
                }

                return _inArchives[user][format];
            }
        }

        public static void SetLibraryPath(string libraryPath)
        {
            if (_modulePtr != IntPtr.Zero && !Path.GetFullPath(libraryPath).Equals(Path.GetFullPath(_libraryFileName), StringComparison.OrdinalIgnoreCase))
            {
                throw new SevenZipLibraryException($"can not change the library path while the library \"{_libraryFileName}\" is being used.");
            }
            
            if (!File.Exists(libraryPath))
            {
                throw new SevenZipLibraryException($"can not change the library path because the file \"{libraryPath}\" does not exist.");
            }

            _libraryFileName = libraryPath;
        }
    }
#endif
}
