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
        static int Main(string[] args)
        {
            SevenZipExtractor.SetLibraryPath(Path.Combine(Path.GetDirectoryName(typeof(SevenZipProgram).Assembly.Location), "7z.dll"));
            var file = args.FirstOrDefault();
            if (string.IsNullOrEmpty(file))
            {
                Console.WriteLine("Must specify a file.");
                return 1;
            }
            var mountPoint = args.FirstOrDefault();
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

            try
            {
                using (var mre = new System.Threading.ManualResetEvent(false))
                using (var dokan = new DokanNet.Dokan(new DokanNet.Logging.NullLogger()))
                {
                    Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
                    {
                        e.Cancel = true;
                        mre.Set();
                    };
                    var builder = new DokanNet.DokanInstanceBuilder(dokan);
                    var fs = new SevenZipFs(file);
                    builder.ConfigureOptions(options =>
                    {
                        options.Options = DokanOptions.DebugMode | DokanOptions.StderrOutput;
                        options.SingleThread = true;
                        //if (isDrive)
                        //    options.Options = DokanNet.DokanOptions.RemovableDrive;
                        options.MountPoint = isDrive ? mountPoint.ToUpper() + ":\\" : mountPoint;
                    });
                    using (var instance = builder.Build(fs))
                    {
                        builder = null;
                        mre.WaitOne();
                    }
                }
            }
            catch (DokanException ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return 0;
        }
    }
}
