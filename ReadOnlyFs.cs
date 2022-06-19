using System;
using System.IO;
using System.Security.AccessControl;
using DokanNet;

namespace Shaman.Dokan
{
    public abstract class ReadOnlyFs : FileSystemBase
    {
        public override NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            bytesWritten = 0;
            return NtStatus.DiskFull;
        }

        public override NtStatus WriteFile(string fileName, IntPtr buffer, uint bufferLength, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            bytesWritten = 0;
            return NtStatus.DiskFull;
        }

        public override NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }

        public override NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            return NtStatus.DiskFull;
        }




        public override void Cleanup(string fileName, IDokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine($"{nameof(Cleanup)}('{fileName}', {info} - entering");
#endif

            (info.Context as Stream)?.Dispose();
            info.Context = null;
            Trace(nameof(Cleanup), fileName, info, DokanResult.Success);
        }

    }
}