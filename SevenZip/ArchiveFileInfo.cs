#if UNMANAGED

namespace SevenZip
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Struct for storing information about files in the 7-zip archive.
    /// </summary>
    public struct ArchiveFileInfo
    {
        /// <summary>
        /// Gets or sets index of the file in the archive file table.
        /// </summary>
        // [CLSCompliant(false)]
        public uint Index;

        /// <summary>
        /// Gets or sets file attributes.
        /// </summary>
        // [CLSCompliant(false)]
        public uint Attributes;

        /// <summary>
        /// Gets or sets size of the file (unpacked).
        /// </summary>
        // [CLSCompliant(false)]
        public ulong Size;

        /// <summary>
        /// Gets or sets the file last write time.
        /// </summary>
        public DateTime LastWriteTime;

        /// <summary>
        /// Gets or sets the file creation time.
        /// </summary>
        public DateTime CreationTime;

        /// <summary>
        /// Gets or sets CRC checksum of the file.
        /// </summary>
        // [CLSCompliant(false)]
        //public uint Crc;

        /// <summary>
        /// Gets or sets being a directory.
        /// </summary>
        public bool IsDirectory => (Attributes & (uint)System.IO.FileAttributes.Directory) != 0;

        /// <summary>
        /// Gets or sets being encrypted.
        /// </summary>
        public bool Encrypted => (Attributes & (uint)System.IO.FileAttributes.Encrypted) != 0;

        /// <summary>
        /// Compression method for the file.
        /// </summary>
        //public bool IsCopy => (Attributes & (uint)System.IO.FileAttributes.Compressed) != 0;

        /// <summary>
        /// Determines whether the specified System.Object is equal to the current ArchiveFileInfo.
        /// </summary>
        /// <param name="obj">The System.Object to compare with the current ArchiveFileInfo.</param>
        /// <returns>true if the specified System.Object is equal to the current ArchiveFileInfo; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return obj is ArchiveFileInfo info && Equals(info);
        }

        /// <summary>
        /// Determines whether the specified ArchiveFileInfo is equal to the current ArchiveFileInfo.
        /// </summary>
        /// <param name="afi">The ArchiveFileInfo to compare with the current ArchiveFileInfo.</param>
        /// <returns>true if the specified ArchiveFileInfo is equal to the current ArchiveFileInfo; otherwise, false.</returns>
        public bool Equals(ArchiveFileInfo afi)
        {
            return afi.Index == Index;
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <returns> A hash code for the current ArchiveFileInfo.</returns>
        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }

        /// <summary>
        /// Returns a System.String that represents the current ArchiveFileInfo.
        /// </summary>
        /// <returns>A System.String that represents the current ArchiveFileInfo.</returns>
        public override string ToString()
        {
            return "[" + Index.ToString(CultureInfo.CurrentCulture) + "] ";
        }

        /// <summary>
        /// Determines whether the specified ArchiveFileInfo instances are considered equal.
        /// </summary>
        /// <param name="afi1">The first ArchiveFileInfo to compare.</param>
        /// <param name="afi2">The second ArchiveFileInfo to compare.</param>
        /// <returns>true if the specified ArchiveFileInfo instances are considered equal; otherwise, false.</returns>
        public static bool operator ==(ArchiveFileInfo afi1, ArchiveFileInfo afi2)
        {
            return afi1.Equals(afi2);
        }

        /// <summary>
        /// Determines whether the specified ArchiveFileInfo instances are not considered equal.
        /// </summary>
        /// <param name="afi1">The first ArchiveFileInfo to compare.</param>
        /// <param name="afi2">The second ArchiveFileInfo to compare.</param>
        /// <returns>true if the specified ArchiveFileInfo instances are not considered equal; otherwise, false.</returns>
        public static bool operator !=(ArchiveFileInfo afi1, ArchiveFileInfo afi2)
        {
            return !afi1.Equals(afi2);
        }
    }
}

#endif
