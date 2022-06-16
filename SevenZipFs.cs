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

        public SevenZipExtractor extractor;
        private FsNode<ArchiveFileInfo> root;
        private ulong TotalSize;
        public bool Encrypted { get; private set; } = false;
        public string RootFolder { get; private set; }
        public string VolumeLabel;
        public SevenZipFs(string path)
        {
            extractor = new SevenZipExtractor(path);
            TotalSize = 0;
            root = CreateTree(extractor.ArchiveFileData, x =>
            {
                var name = x.FileName;
                x.FileName = null;
                return name;
            }, x => x.IsDirectory, NewDirectory);
            extractor.ArchiveFileData = null;
            CollectInfo();
            cache = new MemoryStreamCache<FsNode<ArchiveFileInfo>>((item, stream) =>
            {
                lock (readerLock)
                {
                    extractor.ExtractFile(item.Info.Index, stream);
                }
            });
        }

        private object readerLock = new object();

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

        public override NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            //if (IsBadName(fileName)) return NtStatus.ObjectNameInvalid;
            if ((access & ModificationAttributes) != 0) return NtStatus.DiskFull;

            var item = GetNode(root, fileName, out var _);
            if (item == null) return DokanResult.FileNotFound;
            if (!item.Info.IsDirectory)
            {
                if ((access & (FileAccess.ReadData | FileAccess.GenericRead)) != 0)
                {
                    //Console.WriteLine("ReadData: " + fileName);
                    info.Context = cache.OpenStream(item, (long)item.Info.Size);

                }
            }
            return NtStatus.Success;
        }


        private MemoryStreamCache<FsNode<ArchiveFileInfo>> cache;
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
            fileSystemName = "ArchiveFs";
            volumeLabel = VolumeLabel ?? fileSystemName;
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.ReadOnlyVolume | FileSystemFeatures.UnicodeOnDisk | FileSystemFeatures.VolumeIsCompressed;
            maximumComponentLength = 256;
            return NtStatus.Success;
        }

        private FsNode<ArchiveFileInfo> GetFile(string fileName)
        {
            return GetNode(root, fileName, out var _);
        }

        protected override IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            var item = GetFile(fileName);
            if (item == null) return null;

            if (item.Info.IsDirectory)
            {
                if (item.Children == null) return new FileInformation[] { };
                if (searchPattern == "*")
                {
                    return item.Children.Select(x => GetFileInformation(x.Value, x.Key)).ToList();
                }
                var matcher = GetMatcher(searchPattern);
                return item.Children.Where(x => matcher(x.Key)).Select(x => GetFileInformation(x.Value, x.Key)).ToList();
            }
            return null;
        }

        private FileInformation GetFileInformation(FsNode<ArchiveFileInfo> item, string name)
        {
            return new FileInformation()
            {
                Attributes = (FileAttributes)item.Info.Attributes,
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

        public FsNode<ArchiveFileInfo> NewDirectory()
        {
            var item = new FsNode<ArchiveFileInfo>()
            {
                Children = new SortedDictionary<string, FsNode<ArchiveFileInfo>>()
            };
            item.Info.Attributes = (uint)FileAttributes.Directory;
            return item;
        }

        public bool SetRoot(string newRootFolder)
        {
            if (newRootFolder == ":auto")
            {
                if (root.Children.Count == 1)
                {
                    RootFolder = "/" + root.Children.First().Key;
                    root = root.Children.First().Value;
                }
                return true;
            }
            if (newRootFolder[0] == ':') { return true; }
            var item = GetNode(root, newRootFolder, out var name);
            if (item == null) { return false; }
            if (item == root) { return true; }
            if (item.Info.IsDirectory)
            {
                root = item;
                TotalSize = 0;
                ForEach(root, file => TotalSize += file.Info.Size);
                RootFolder = string.Join("/", newRootFolder.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries));
            }
            else
            {
                var newRoot = NewDirectory();
                newRoot.Children[name] = item;
                root = newRoot;
                TotalSize = item.Info.Size;
                RootFolder = ".../" + (newRootFolder.Length < 20 ? newRootFolder : name);
            }
            TotalSize = Math.Max(TotalSize, 1024);
            return true;
        }

        public static void ForEach(FsNode<ArchiveFileInfo> root, Action<FsNode<ArchiveFileInfo>> OnFile)
        {
            IEnumerator<FsNode<ArchiveFileInfo>> top = root.Children.Values.GetEnumerator();
            var stack = new Stack<IEnumerator<FsNode<ArchiveFileInfo>>>();
            stack.Push(top);
            while (stack.Count > 0)
            {
                top = stack.Pop();
                while (top.MoveNext())
                {
                    var next = top.Current;
                    if (next.Info.IsDirectory)
                    {
                        if (next.Children.Count > 0)
                        {
                            stack.Push(top);
                            top = next.Children.Values.GetEnumerator();
                        }
                    }
                    else
                        OnFile(next);
                }
            }
        }

        public void CollectInfo()
        {
            ulong total = 0;
            bool encrypt = false;
            ulong nameLen = 0;
            DateTime Now = DateTime.Now, MaxDate = new DateTime(2099, 12, 31);
            bool iter(FsNode<ArchiveFileInfo> dir)
            {
                DateTime ctime = MaxDate, mtime = DateTime.MinValue;
                bool hasChildren = false;
                foreach (var pair in dir.Children)
                {
                    nameLen += (ulong) pair.Key.Length;
                    var item = pair.Value;
                    if (item.Info.IsDirectory)
                    {
                        if (!iter(item))
                            continue;
                    }
                    else
                    {
                        encrypt = encrypt || item.Info.Encrypted;
                        total += item.Info.Size;
                        item.Info.Attributes |= (uint)FileAttributes.ReadOnly;
                    }
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
            Encrypted = encrypt;
            TotalSize = Math.Max(total, 1024);
        }

        internal bool TryDecrypt()
        {
            IEnumerator<FsNode<ArchiveFileInfo>> top = root.Children.Values.GetEnumerator();
            var stack = new Stack<IEnumerator<FsNode<ArchiveFileInfo>>>();
            stack.Push(top);
            while (stack.Count > 0)
            {
                top = stack.Pop();
                while (top.MoveNext())
                {
                    var next = top.Current;
                    if (next.Info.IsDirectory)
                    {
                        if (next.Children.Count > 0)
                        {
                            stack.Push(top);
                            top = next.Children.Values.GetEnumerator();
                        }
                    }
                    else if (next.Info.Size > 0)
                        return extractor.TryDecrypt(next.Info.Index);
                }
            }
            return true;
        }
    }
}
