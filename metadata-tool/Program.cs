using System;
using System.IO;

namespace MetadataTool
{
    internal class Program
    {
        public static bool Interactive { get; private set; }
        public static string BaseDirectory { get; private set; }

        static void Main(string[] args)
        {
            int modeArgIdx = Array.IndexOf(args, "-mode");
            string modeArg = "";
            if (modeArgIdx >= 0)
            {
                modeArg = args[modeArgIdx + 1];
            }

            Interactive = Array.IndexOf(args, "-automate") == -1;
            BaseDirectory = (Array.IndexOf(args, "-use_exe_dir") == -1) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

            switch (modeArg.ToLowerInvariant())
            {
                case "separate":
                    {
                        var separator = new Separator(args);
                        separator.Separate();
                    }
                    break;
                case "guess":
                    {
                        var guesser = new Guesser(args);
                        guesser.Guess();
                    }
                    break;
                case "find":
                    {
                        var finder = new Finder(args);
                        finder.Find();
                    }
                    break;
                case "get":
                    {
                        var getter = new Getter(args);
                        getter.Get();
                    }
                    break;
                default:
                    Console.WriteLine($"Unknown mode {modeArg}");
                    break;
            }
        }
    }
}
