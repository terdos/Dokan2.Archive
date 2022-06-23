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
        private string FileSystemName;
        private FsNode root;
        private MemoryStreamCache cache;
        private ulong TotalSize;
        public bool Encrypted { get; private set; } = false;
        public string RootFolder { get; private set; }
        public string VolumeLabel;

        public SevenZipFs(string path)
        {
            extractor = new SevenZipExtractor(path);
            FileSystemName = "ArchiveFS." + extractor.Format.ToString();
            VolumeLabel = FileSystemName;
            TotalSize = 0;
            root = CreateTree(extractor);
            CollectInfo();
            cache = new MemoryStreamCache(ExtractFile);
        }

        public void ExtractFile(FsNode item, MemoryStreamInternal stream)
        {
            //lock (readerLock)
            {
                extractor.ExtractFile(item, stream);
            }
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

        private FsNode GetFile(string fileName)
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
                TotalSize = 0;
                ForEach(root, file => TotalSize += file.Info.Size);
            }
            else
            {
                var newRoot = NewDirectory();
                newRoot.Children[name] = item;
                root = newRoot;
                TotalSize = item.Info.Size;
                RootFolder = RootFolder.Length <= 20 ? RootFolder : ".../" + name;
            }
            TotalSize = Math.Max(TotalSize, 1024);
            return true;
        }

        public static void ForEach(FsNode root, Action<FsNode> OnFile)
        {
            IEnumerator<FsNode> top = root.Children.Values.GetEnumerator();
            var stack = new Stack<IEnumerator<FsNode>>();
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
            bool iter(FsNode dir)
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

        internal bool TryDecompress(bool onlyEncrypted = false)
        {
            IEnumerator<FsNode> top = root.Children.Values.GetEnumerator();
            var stack = new Stack<IEnumerator<FsNode>>();
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
                    else if ((!onlyEncrypted || next.Info.Encrypted) && next.Info.Size > 0)
                        return extractor.TryDecrypt(next);
                }
            }
            return true;
        }
    }
}
