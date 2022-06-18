using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using SevenZip;
using System.IO;
using DokanNet;

namespace Shaman.Dokan
{
    public class SevenZipProgram
    {
        delegate int ExtractArg(string argName, out string value);

        private static string _password = null;
        private static bool _hasReadPW = false;

        public static bool VerboseOutput = false;
        public static bool DebugDokan = false;

        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: ArchiveFs.exe [-ovd] <archive-file> Drive: [root-folder] [-l label] [-p [password]]");
            }
            ExtractArg extractArg = (string argName, out string value) =>
            {
                var index = Array.FindIndex(args, i => i.StartsWith(argName));
                value = null;
                if (index < 0) { }
                else if (args[index].Length > argName.Length)
                {
                    value = args[index].Substring(argName.Length);
                    value = value.StartsWith("=") ? value.Substring(1) : value;
                    args = args.Take(index).Concat(args.Skip(index + 1)).ToArray();
                }
                else if (index < args.Length - 1)
                {
                    value = args[index + 1];
                    args = args.Take(index).Concat(args.Skip(index + 2)).ToArray();
                }
                else
                    args = args.Take(index).ToArray();
                return index;
            };
            var labelIndex = extractArg("-l", out var labelName);
            var passwordIndex = extractArg("-p", out _password);
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
                var prefix = new[] { @"\cygdrive\", @"\mnt\", @"\" }.First(i => file.StartsWith(i));
                var file2 = file.Length > prefix.Length + 2 ? file.Substring(prefix.Length, 2).ToUpper() : "  ";
                if (file2[1] == '\\' && file2[0] >= 'A' && file2[0] <= 'Z')
                {
                    file = file2[0] + @":\" + file.Substring(prefix.Length + 2);
                    Console.WriteLine("Select an archive in {0}", file);
                }
            }
            DebugDokan = opts.Contains(" -d");
            VerboseOutput = DebugDokan || opts.Contains(" -v");

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
                if (!fs.TryDecrypt())
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
            if (!_hasReadPW)
                fs.TryDecrypt();
            Console.WriteLine("  Has loaded {0}", file);
            if (fs.extractor.IsSolid)
                Console.WriteLine("Warning: mounting performance of solid archives is very poor!");
            if (opts.Contains(" -e")) // test extraction only
            {
                Console.WriteLine("  Test passed.");
                return 0;
            }

            bool doesOpen = opts.Contains(" -o") || opts.Contains(" --open");
            bool testOnly = opts.Contains(" -t") || opts.Contains(" --dry");
            if (!string.IsNullOrWhiteSpace(labelName))
                fs.VolumeLabel = labelName.Trim();
            else
            {
                var label = fs.VolumeLabel = Path.GetFileNameWithoutExtension(file).Trim();
                var root = fs.RootFolder;
                if (!string.IsNullOrEmpty(root) && root.StartsWith(label + "/"))
                    root = "./" + root.Substring(label.Length + 1);
                label = !string.IsNullOrEmpty(label) ? $"{label} ({root})" : root;
                fs.VolumeLabel = label;
            }
            fs.OnMount = (drive) =>
            {
                if (drive != null)
                {
                    Console.WriteLine("  Has mounted as {0} .", drive.EndsWith("\\") ? drive : drive + "\\");
                    if (testOnly)
                    {
                        Console.WriteLine("  Test passed.");
                        Environment.Exit(0);
                    }
                    else if (doesOpen)
                        Process.Start(drive.EndsWith("\\") ? drive : drive + "\\");
                }
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
                        if (DebugDokan)
                            options.Options |= DokanOptions.DebugMode | DokanOptions.StderrOutput;
                    });
                    using (var instance = builder.Build(fs))
                    {
                        builder = null;
                        opts = null;
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false, true);
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
