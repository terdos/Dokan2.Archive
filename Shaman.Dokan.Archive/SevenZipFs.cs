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
        public SevenZipFs(string path, string password = null)
        {
            zipfile = path;
            extractor = new SevenZipExtractor(path, password);
            TotalSize = 0;
            root = CreateTree(extractor.ArchiveFileData, x => x.FileName, x =>
            {
                if (x.IsDirectory) return true;
                TotalSize += x.Size;
                Encrypted = Encrypted || x.Encrypted;
                return false;
            });
            cache = new MemoryStreamCache<FsNode<ArchiveFileInfo>>((item, stream) =>
            {
                lock (readerLock)
                {
                    extractor.ExtractFile(item.Info.Index, stream);
                }
            });
        }
        private string zipfile;

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
            var item = GetFile(fileName);
            if (item == null)
            {
                fileInfo = default(FileInformation);
                return DokanResult.FileNotFound;
            }
            fileInfo = GetFileInformation(item);
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
            return GetNode(root, fileName);
        }

        protected override IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            var item = GetFile(fileName);
            if (item == null) return null;

            if (item == root || IsDirectory(item.Info.Attributes))
            {
                if (item.Children == null) return new FileInformation[] { };
                var matcher = GetMatcher(searchPattern);
                return item.Children.Where(x => matcher(x.Name)).Select(x => GetFileInformation(x)).ToList();
            }
            return null;
        }

        private FileInformation GetFileInformation(FsNode<ArchiveFileInfo> item)
        {
            return new FileInformation()
            {
                Attributes = item == root ? FileAttributes.Directory : (FileAttributes)item.Info.Attributes,
                CreationTime = item.Info.CreationTime,
                FileName = item.Name,
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

    }
}
