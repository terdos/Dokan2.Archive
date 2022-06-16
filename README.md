## 7zip mounter
Mounts 7zip/ZIP/RAR files (read only) using Dokany (Dokan 2).

Usage: `Dokan2.Archive.exe <path-to-archive> X: [-ovd] [-p [password]]`

The archive is mounted in `X:\`. When `-o` or `--open` is specified, the mounted drive is opened in Explorer.

Supports all the formats supported by [7-Zip](https://www.7-zip.org/).

Requires [Dokan 2.0+](http://dokan-dev.github.io/).

## License

The code in `Shaman.Dokan` namespace is based on [Andrea Martinelli (@antiufo)](https://github.com/antiufo)'s
  [Shaman.Dokan.Archive](https://github.com/antiufo/Shaman.Dokan.Archive),
  and modified by [@gdh1995](https://github.com/gdh1995).
  These code has no license, and is PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

The folder named `DokanNet` and `SevenZip` are copied from other projects,
so all rights belong to their project owners.
