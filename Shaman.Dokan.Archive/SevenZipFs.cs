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
        public SevenZipFs(string path)
        {
            extractor = new SevenZipExtractor(path);
            TotalSize = 0;
            root = CreateTree(extractor.ArchiveFileData, x => x.FileName, x =>
            {
                if (x.IsDirectory) return true;
                TotalSize += x.Size;
                Encrypted = Encrypted || x.Encrypted;
                return false;
            });
            TotalSize = Math.Max(TotalSize, 1024);
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
            if (IsBadName(fileName)) return NtStatus.ObjectNameInvalid;
            if ((access & ModificationAttributes) != 0) return NtStatus.DiskFull;

            var item = GetFile(fileName);
            if (item == null) return DokanResult.FileNotFound;
            if (item.Info.FileName != null && !item.Info.IsDirectory)
            {
                if ((access & (FileAccess.ReadData | FileAccess.GenericRead)) != 0)
                {
                    //Console.WriteLine("ReadData: " + fileName);
                    info.Context = cache.OpenStream(item, (long)item.Info.Size);

                }
                return NtStatus.Success;
            }
            else
            {
                info.IsDirectory = true;
                return NtStatus.Success;
            }
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
            fileSystemName = volumeLabel = "ArchiveFs";
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

            if (item == root || IsDirectory(item.Info.Attributes))
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
                Attributes = item == root ? FileAttributes.Directory : (FileAttributes)item.Info.Attributes,
                CreationTime = item.Info.CreationTime,
                FileName = name,
                LastAccessTime = item.Info.LastAccessTime,
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

        public bool SetRoot(string newRootFolder)
        {
            if (newRootFolder == ":auto")
            {
                if (root.Children.Count == 1)
                    root = root.Children.First().Value;
                return true;
            }
            if (newRootFolder[0] == ':') { return true; }
            var item = GetNode(root, newRootFolder, out var name);
            if (item == null) { return false; }
            if (item.Info.IsDirectory)
            {
                root = item;
                TotalSize = 0;
                ForEachFile(root, info => TotalSize += info.Size);
            }
            else if (name != null)
            {
                var newRoot = new FsNode<ArchiveFileInfo>()
                {
                    Children = new Dictionary<string, FsNode<ArchiveFileInfo>>() { }
                };
                newRoot.Children[name] = item;
                root = newRoot;
                TotalSize = item.Info.Size;
            }
            TotalSize = Math.Max(TotalSize, 1024);
            return true;
        }
    }
}
