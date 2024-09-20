# Archive mounter for Dokan

Mounts archive files (read only) using Dokany (Dokan 2).

Supports all the formats supported by [7-Zip](https://www.7-zip.org/).

This fork implements a GitHub action that provides a working executable automatically at each release.

**Note**: At the moment a `nupkg` package is provided that includes the main executable (check the *Actions* tab of GitHub).

## Installation

Requires [Dokan 2](http://dokan-dev.github.io/).

Copy the `Dokan2.Archive.exe` file to a working *Path* (such as `C:\Windows`) for a seamless experience.

## Usage

`Dokan2.Archive.exe <path-to-archive> X: [-ovd] [-p [password]]`

The archive is mounted in `X:\`. When `-o` or `--open` is specified, the mounted drive is opened in Explorer.

## License

This repository is a fork of [@nedex](https://github.com/nedex), based on [Shaman.Dokan.Archive](https://github.com/antiufo/Shaman.Dokan.Archive).

All rights belong to their project owners, please refer to the source repositories for further informations.
