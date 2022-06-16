using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using SevenZip;
using System.IO;
using DokanNet;

namespace Shaman.Dokan
{
    class SevenZipProgram
    {
        private static string _password = null;
        private static bool _hasReadPW = false;

        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: ArchiveFs.exe [-ovd] <archive-file> Drive: [root-folder] [-p [password]]");
            }
            var passwordIndex = Array.FindIndex(args, i => i == "-p");
            if (passwordIndex >= 0)
            {
                if (passwordIndex < args.Length - 1)
                {
                    _password = args[passwordIndex + 1];
                    args = args.Take(passwordIndex).Concat(args.Skip(passwordIndex + 2)).ToArray();
                }
            }
            var opts = " " + string.Join(" ", args.Where(i => i[0] == '-'));
            args = args.Where(i => i[0] != '-').ToArray();
            var file = args.FirstOrDefault();
            if (string.IsNullOrEmpty(file))
            {
                Console.WriteLine("Must specify a file.");
                return 1;
            }
            var mountPoint = args.Skip(1).FirstOrDefault();
            if (mountPoint != null && mountPoint.Length >= 2 && mountPoint.Length <= 3
                && ":\\:/".Contains(mountPoint.Substring(1)))
                mountPoint = mountPoint.Substring(0, 1);
            bool isDrive = mountPoint != null && mountPoint.Length == 1
                && 'a' <= mountPoint.ToLower()[0] && mountPoint.ToLower()[0] <= 'z';
            if (isDrive && Directory.Exists(mountPoint + ":\\"))
            {
                Console.WriteLine("The drive letter has been used.");
                return 1;
            }
            if (string.IsNullOrEmpty(mountPoint))
            {
                mountPoint = "X";
                isDrive = true;
            }
            var rootFolder = args.Skip(2).FirstOrDefault();
            file = file.Replace('/', '\\');
            rootFolder = rootFolder != null ? rootFolder.Replace('/', '\\') : null;
            args = null;

            if (file.Length > 3 && file[0] == '\\' && file[1] != '\\' && !File.Exists(file))
            {
                var prefix = new[] { @"\cygdrive\", @"\mnt\", @"\\" }.First(i => file.StartsWith(i));
                var file2 = file.Length > prefix.Length + 2 ? file.Substring(prefix.Length, 2).ToUpper() : "  ";
                if (file2[1] == '\\' && file2[0] >= 'A' && file2[0] <= 'Z')
                {
                    file = file2[0] + @":\" + file.Substring(prefix.Length + 2);
                    Console.WriteLine("Select an archive in {0}", file);
                }
            }

            SevenZipFs fs;
            try
            {
                fs = new SevenZipFs(file);
            }
            catch (Exception ex)
            {
                Console.Out.Flush();
                if (ex is SevenZipException || ex is FileNotFoundException)
                    Console.Error.WriteLine("Error: {0}", ex.Message);
                else
                    Console.Error.WriteLine("Error: {0}", ex.ToString());
                return 2;
            }
            if (fs.Encrypted && !_hasReadPW)
            {
                if (!fs.extractor.TryDecrypt())
                {
                    Console.Out.Flush();
                    Console.Error.WriteLine("Error: Password is wrong!");
                    return 3;
                }
            }
            if (!string.IsNullOrEmpty(rootFolder) && !fs.SetRoot(rootFolder))
            {
                Console.Error.WriteLine("Error: Root folder is not found in archive!");
                return 4;
            }
            if (!fs.Encrypted && !string.IsNullOrEmpty(_password) && !_hasReadPW)
                Console.WriteLine("Warning: archive is not encrypted!");
            _password = null;
            Console.WriteLine("  Has loaded {0}", file);
            if (fs.extractor.IsSolid)
                Console.WriteLine("Warning: mounting performance of solid archives is very poor!");

            fs.OnMount = (drive) =>
            {
                if (drive != null)
                    Console.WriteLine("  Has mounted as {0} .", drive.EndsWith("\\") ? drive : drive + "\\");
                else
                    Console.In.Close();
            };
            try
            {
                using (var dokan = new DokanNet.Dokan(new DokanNet.Logging.NullLogger()))
                {
                    var builder = new DokanNet.DokanInstanceBuilder(dokan);
                    builder.ConfigureOptions(options =>
                    {
                        options.MountPoint = isDrive ? mountPoint.ToUpper() + ":" : mountPoint;
                        options.Options = DokanOptions.CurrentSession | DokanOptions.WriteProtection
                            | DokanOptions.CaseSensitive
                            | DokanOptions.MountManager | DokanOptions.RemovableDrive;
                        if (opts.Contains(" -v") || opts.Contains(" -d"))
                            options.Options |= DokanOptions.DebugMode | DokanOptions.StderrOutput;
                    });
                    using (var instance = builder.Build(fs))
                    {
                        if (opts.Contains(" -o") || opts.Contains(" --open"))
                            Process.Start(isDrive ? mountPoint.ToUpper() + ":\\" : mountPoint);
                        builder = null;
                        opts = null;
                        while (Console.ReadLine() != null) { }
                        fs.OnMount = null;
                    }
                }
            }
            catch (DokanException ex)
            {
                Console.Out.Flush();
                Console.Error.Flush();
                Console.Error.WriteLine(ex.ToString());
                return 9;
            }
            return 0;
        }

        public static string InputPassword()
        {
            bool hasRead = _hasReadPW;
            _hasReadPW = true;
            if (hasRead || !string.IsNullOrEmpty(_password)) { return _password; }
            Console.Write("Please type archive password: ");
            Console.Out.Flush();
            _password = "";
            try
            {
                while (string.IsNullOrEmpty(_password))
                    _password = Console.ReadLine();
            }
            catch (Exception)
            {
                Environment.Exit(0);
            }
            return _password;
        }
    }
}
