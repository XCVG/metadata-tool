using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MetadataTool
{
    internal static class Utils
    {
        public static T GetArg<T>(string[] args, string argName, T defaultValue = default)
        {
            int index = Array.IndexOf(args, argName);
            if (index >= 0 && args.Length > index + 1)
            {
                string rawArg = args[index + 1];
                if (typeof(T).IsAssignableFrom(typeof(string)))
                    return (T)(object)rawArg;

                return (T)Convert.ChangeType(rawArg, typeof(T));
            }

            return defaultValue;
        }

        private static readonly IReadOnlySet<string> VideoFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mkv", "webm", "mp4", "mov"};

        public static bool IsVideoFileExtension(string extension)
        {
            return VideoFileExtensions.Contains(extension.Trim('.'));
        }

        public static string SetTagsAndCopy(string source, string destination, bool keepOriginal, IDictionary<string, string> tags)
        {
            //ffmpeg -i ''  -c:v copy -c:a copy -c:s copy -map 0 -metadata  MTOOL_BESTGUESS_ID="UG6x5w6TiUI" -metadata MTOOL_BESTGUESS_SITE="youtube" ''

            //special case where an mp4 and a webm exists
            string originalExtension = "";
            if (File.Exists(Path.Combine(Path.GetDirectoryName(source), Path.GetFileNameWithoutExtension(source) + ".webm")) && File.Exists(Path.Combine(Path.GetDirectoryName(source), Path.GetFileNameWithoutExtension(source) + ".mp4")))
            {
                originalExtension = Path.GetExtension(source);
            }

            destination = Path.Combine(Path.GetDirectoryName(destination), Path.GetFileNameWithoutExtension(destination) + originalExtension + ".mkv");
            if (File.Exists(destination))
            {
                Path.Combine(Path.GetDirectoryName(destination), Path.GetFileNameWithoutExtension(destination) + " (1)" + originalExtension + ".mkv");
            }

            source = Path.GetFullPath(source);
            destination = Path.GetFullPath(destination);

            string tagString = string.Join(" ", tags.Select(t => $"-metadata {t.Key}=\"{t.Value}\""));

            using (Process p = new Process())
            {
                p.StartInfo.FileName = "ffmpeg";
                p.StartInfo.WorkingDirectory = Path.GetDirectoryName(source);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                //p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.Arguments = $"-i \"{source}\" -c:v copy -c:a copy -c:s copy -map 0 {tagString} \"{destination}\"";

                p.Start();

                p.WaitForExit(30000);

                if (!p.HasExited)
                {
                    throw new Exception("ffmpeg took too long");
                }
            }

            Thread.Sleep(100); //make sure FS changes are committed

            if (!File.Exists(destination))
            {
                throw new FileNotFoundException("ffmpeg failed to create file");
            }

            var modifiedDate = File.GetLastWriteTime(source);
            File.SetLastWriteTime(destination, modifiedDate);

            if (!keepOriginal)
            {
                File.Delete(source);
            }

            Thread.Sleep(100); //make sure FS changes are committed

            return destination;
        }

        public static string GetFFProbeOutput(string filePath)
        {
            //ffprobe -i '' -print_format json -show_format -v quiet
            string output = null;
            using (Process p = new Process())
            {
                p.StartInfo.FileName = "ffprobe";
                p.StartInfo.WorkingDirectory = Path.GetDirectoryName(filePath);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.Arguments = $"-i \"{filePath}\" -print_format json -show_format -v quiet";

                p.Start();

                p.WaitForExit(10000);

                output = p.StandardOutput.ReadToEnd();

                if (!p.HasExited)
                {
                    throw new Exception("ffprobe took too long");
                }
            }

            return output;
        }

    }
}
