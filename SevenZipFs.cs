using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DokanNet;
using SevenZip;
using FileAccess = DokanNet.FileAccess;

namespace Shaman.Dokan
{
    public class SevenZipFs : ReadOnlyFs
    {

        public SevenZipExtractor _extractor;
        private string FileSystemName;
        private FsNode root;
        private MemoryStreamCache cache;
        private ulong TotalSize;
        private readonly string[] UserSwitches;
        public static readonly int kProcessors = Environment.ProcessorCount;
        private static bool HasWarnedSolidPackage = false;

        public string RootFolder { get; private set; }
        public string VolumeLabel;

        public SevenZipFs(string[] extractorSwitches)
        {
            FileSystemName = "ArchiveFS";
            VolumeLabel = FileSystemName;
            TotalSize = 0;
            UserSwitches = extractorSwitches.Where(i => i.Length > 0).ToArray();
            cache = new MemoryStreamCache();
        }

        public void LoadOneZip(string path)
        {
            var extractor = new SevenZipExtractor(path);
            InitProperties(extractor, UserSwitches);
            root = CreateTree(extractor);
            TotalSize += CollectInfo(root);
            if (!SevenZipProgram.MixMode)
                FileSystemName += "." + extractor.Format.ToString();
            if (!HasWarnedSolidPackage && (HasWarnedSolidPackage = extractor.IsSolid))
                Console.WriteLine("Warning: mounting performance of solid archives is very poor!");
        }

        private static void InitProperties(SevenZipExtractor extractor, string[] userSwitches)
        {
#pragma warning disable CS0078
            const long k1 = 1l;
#pragma warning restore CS0078
            var format = extractor.Format;
            List<string> defaultSwitches = new List<string>();
            if ((((k1 << (int)InArchiveFormat.SevenZip | k1 << (int)InArchiveFormat.XZ
                    | k1 << (int)InArchiveFormat.Zip) >> (int)format) & k1) != 0)
                defaultSwitches.Add("crc0");
            if ((((k1 << (int)InArchiveFormat.SevenZip | k1 << (int)InArchiveFormat.XZ
                    | k1 << (int)InArchiveFormat.Zip | k1 << (int)InArchiveFormat.BZip2
                    | k1 << (int)InArchiveFormat.GZip | k1 << (int)InArchiveFormat.Swf
                ) >> (int)format) & k1) != 0)
                defaultSwitches.Add("mt" + (kProcessors >= 8 ? 4 : kProcessors >= 4 ? 2 : 1));
            var extractorSwitches = defaultSwitches.Concat(userSwitches).ToArray();
            if (SevenZipProgram.DebugSelf > 0 && extractorSwitches.Length > defaultSwitches.Count)
                Console.WriteLine("  Extractor switches: {0} + {1}", string.Join(", ", defaultSwitches)
                    , string.Join(", ", extractorSwitches.Skip(defaultSwitches.Count)));
            else if (SevenZipProgram.VerboseOutput)
                Console.WriteLine("  Default extractor switches: {0}", string.Join(", ", defaultSwitches));
            extractor.SetProperties(extractorSwitches);
        }

        public Action<string> OnMount;

        public override NtStatus Mounted(string mountPoint, IDokanFileInfo info)
        {
            if (OnMount != null)
                OnMount(mountPoint ?? "");
            return DokanResult.Success;
        }

        public override NtStatus Unmounted(IDokanFileInfo info)
        {
            if (OnMount != null)
                OnMount(null);
            return DokanResult.Success;
        }

        public override NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode
            , FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            //if (IsBadName(fileName)) return NtStatus.ObjectNameInvalid;
            if ((access & ModificationAttributes) != 0) return NtStatus.DiskFull;

            var item = GetNode(root, fileName, out var _);
            if (item == null)
            {
                if (SevenZipProgram.DebugSelf > 1)
                {
                    int acc = (int) access;
                    string name = null;
                    for (int i = 0; i < 32; i++) {
                        if ((acc & (1 << i)) != 0)
                        {
                            var str = Enum.GetName(typeof(FileAccess), 1 << i) ?? string.Format("0x{0:X}", (1 << i));
                            name = name != null ? name + " | " + str : str;
                        }
                    }
                    Console.WriteLine("Warning: File not found: {0} with access = {1}", fileName, name);
                }
                return DokanResult.FileNotFound;
            }
            if (!item.Info.IsDirectory)
            {
                if ((access & (FileAccess.ReadData | FileAccess.GenericRead)) != 0)
                {
                    info.Context = cache.OpenStream(item);

                }
            }
            return NtStatus.Success;
        }

        public override NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            var item = GetNode(root, fileName, out var name);
            if (item == null)
            {
                fileInfo = default(FileInformation);
                return DokanResult.FileNotFound;
            }
            fileInfo = GetFileInformation(item, name ?? "(root)");
            return NtStatus.Success;
        }

        public override NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            fileSystemName = FileSystemName;
            volumeLabel = VolumeLabel;
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.ReadOnlyVolume | FileSystemFeatures.UnicodeOnDisk | FileSystemFeatures.VolumeIsCompressed;
            maximumComponentLength = 256;
            return NtStatus.Success;
        }

        public override NtStatus FindFiles(string fileName, out IEnumerable<FileInformation> files, IDokanFileInfo info)
        {
            return FindFilesWithPattern(fileName, "*", out files, info);
        }

        public override NtStatus FindFilesWithPattern(string fileName, string searchPattern
            , out IEnumerable<FileInformation> files, IDokanFileInfo info)
        {
            var item = GetNode(root, fileName, out var baseName);
            if (item == null)
            {
                if (SevenZipProgram.DebugSelf > 1)
                {
                    Console.WriteLine("Warning: search but not found: {0} with pattern = {1}", fileName, searchPattern);
                }
                files = null;
                return NtStatus.ObjectNameNotFound;
            }

            if (item.Info.IsDirectory)
            {
                if (searchPattern == "*")
                {
                    files = item.Children.Select(x => GetFileInformation(x.Value, x.Key));
                }
                else
                {
                    var matcher = GetMatcher(searchPattern);
                    files = item.Children.Where(x => matcher(x.Key)).Select(x => GetFileInformation(x.Value, x.Key));
                }
                return NtStatus.Success;
            }
            else
            {
                // When 7zFM.exe v2107 opens a .7z/.tar file in a mounted drive,
                // if double click a folder in the compressed package to show its children,
                // it will access \path\to\zip.file with searchPattern=<inner path to the folder>
                if (SevenZipProgram.DebugSelf > 0)
                    Console.WriteLine("Warning: Unexpected search: {0} with pattern = {1}", fileName, searchPattern);
                /**/
                files = new[] { GetFileInformation(item, baseName) }.Skip(0);
                return NtStatus.ObjectNameNotFound;
                /*/
                files = null;
                return NtStatus.ResourceNameNotFound;
                //*/
            }
        }

        private FileInformation GetFileInformation(FsNode item, string name)
        {
            return new FileInformation()
            {
                Attributes = (FileAttributes)(item.Info.Attributes & 2047),
                CreationTime = item.Info.CreationTime,
                FileName = name,
                LastAccessTime = item.Info.LastWriteTime,
                LastWriteTime = item.Info.LastWriteTime,
                Length = (long)item.Info.Size
            };
        }

        public override void Cleanup(string fileName, IDokanFileInfo info)
        {
        }

        public override NtStatus GetDiskFreeSpace(out long free, out long total, out long used, IDokanFileInfo info)
        {
            free = 0;
            var size = TotalSize > long.MaxValue ? long.MaxValue : (long) TotalSize;
            total = size;
            used = size;
            return size >= 0 ? NtStatus.Success : NtStatus.NotImplemented;
        }

        // should only be called up to once
        public bool SetRoot(string newRootFolder)
        {
            RootFolder = null;
            if (newRootFolder == ":auto")
            {
                if (root.Children.Count == 1)
                {
                    RootFolder = "/" + root.Children.First().Key;
                    root = root.Children.First().Value;
                }
                return true;
            }
            if (newRootFolder.Length == 0 || newRootFolder[0] == ':') { return true; }
            var usableName = newRootFolder[0] == '\\' ? newRootFolder.Substring(1) : newRootFolder;
            if (usableName.Length >= 2 && usableName[1] == ':')
                usableName = usableName[0] + "\\" + usableName.Substring(2);
            var item = GetNode(root, usableName, out var name);
            if (item == null) { return false; }
            if (item == root) { return true; }
            RootFolder = "/" + string.Join("/"
                , newRootFolder.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries));
            if (item.Info.IsDirectory)
            {
                root = item;
                ulong size = 0;
                foreach (var file in root.AllFilesWithContent())
                    size += file.Info.Size;
                TotalSize = size;
            }
            else
            {
                var newRoot = FsNode.NewDirectory();
                newRoot.Children[name] = item;
                root = newRoot;
                TotalSize = item.Info.Size;
                RootFolder = RootFolder.Length <= 20 ? RootFolder : ".../" + name;
            }
            TotalSize = Math.Max(TotalSize, 1024);
            return true;
        }

        public static ulong CollectInfo(FsNode root)
        {
            ulong total = 0;
            //ulong nameLen = 0;
            DateTime Now = DateTime.Now, MaxDate = new DateTime(2099, 12, 31);
            bool iter(FsNode dir)
            {
                DateTime ctime = MaxDate, mtime = DateTime.MinValue;
                bool hasChildren = false;
                foreach (var pair in dir.Children)
                {
                    //nameLen += (ulong) pair.Key.Length;
                    var item = pair.Value;
                    if (item.Info.IsDirectory)
                    {
                        if (!iter(item))
                            continue;
                    }
                    else
                        total += item.Info.Size;
                    ctime = ctime < item.Info.CreationTime ? ctime : item.Info.CreationTime;
                    mtime = mtime > item.Info.LastWriteTime ? mtime : item.Info.LastWriteTime;
                    hasChildren = true;
                }
                if (hasChildren)
                {
                    dir.Info.CreationTime = ctime;
                    dir.Info.LastWriteTime = mtime;
                }
                return hasChildren;
            }
            iter(root);
            //Console.WriteLine(">>> nameLen = {0}", nameLen);
            return total;
        }

        public FsNode FindFirstEncrypted()
        {
            var propVar = new PropVariant();
            return root.AllFilesWithContent().FirstOrDefault(file => file.Extractor.IsEncrypted(file, ref propVar));
        }

        internal bool TryDecompress()
        {
            var firstFile = root.AllFilesWithContent().FirstOrDefault();
            return firstFile?.Extractor.TryDecrypt(firstFile) ?? true;
        }
    }
}
