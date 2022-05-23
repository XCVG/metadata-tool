using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace MetadataTool
{
    /// <summary>
    /// Separates files with metadata from ones without
    /// </summary>
    internal class Separator
    {
        private string InputFolder;
        private string MetadataOutputFolder;
        private string NoMetadataOutputFolder;

        public Separator(string[] args)
        {
            InputFolder = Utils.GetArg<string>(args, "-i");
            MetadataOutputFolder = Utils.GetArg<string>(args, "-ow");
            NoMetadataOutputFolder = Utils.GetArg<string>(args, "-on");

            if(InputFolder == null)
            {
                InputFolder = Program.BaseDirectory;
            }

            if(MetadataOutputFolder == null && NoMetadataOutputFolder == null)
            {
                MetadataOutputFolder = Path.Combine(InputFolder, "_WithMetadata");
                NoMetadataOutputFolder = Path.Combine(InputFolder, "_NoMetadata");
            }
            else if(MetadataOutputFolder == null) //missing one arg implies leaving it in place
            {
                NoMetadataOutputFolder = Path.Combine(InputFolder, "_NoMetadata");
            }
            else if (NoMetadataOutputFolder == null)
            {
                MetadataOutputFolder = Path.Combine(InputFolder, "_WithMetadata");
            }
        }

        public void Separate()
        {
            Console.WriteLine("Mode: Separate files with metadata from files without");
            Console.WriteLine("Input directory: " + InputFolder);
            Console.WriteLine("With-Metadata output directory: " + MetadataOutputFolder);
            Console.WriteLine("No-Metadata output directory: " + NoMetadataOutputFolder);
            Console.WriteLine("Press ENTER to continue or CTRL-C to abort!");

            Console.ReadLine();

            if(!string.IsNullOrEmpty(MetadataOutputFolder))
            {
                Directory.CreateDirectory(MetadataOutputFolder);
            }

            if (!string.IsNullOrEmpty(NoMetadataOutputFolder))
            {
                Directory.CreateDirectory(NoMetadataOutputFolder);
            }

            Thread.Sleep(1000); //anti-glitching

            var files = Directory.EnumerateFiles(InputFolder);
            foreach(var file in files)
            {
                try
                {
                    var fullFilePath = Path.GetFullPath(file);

                    bool hasMetadata = false;
                    
                    var dataString = GetFFProbeOutput(fullFilePath);
                    var data = JObject.Parse(dataString);

                    var tagData = data["format"]["tags"];
                    if(tagData != null && tagData.Type == JTokenType.Object)
                    {
                        if(tagData["PURL"] != null && tagData["PURL"].Type == JTokenType.String)
                        {
                            hasMetadata = true;
                        }
                        else if (tagData["purl"] != null && tagData["purl"].Type == JTokenType.String)
                        {
                            hasMetadata = true;
                        }
                    }                    

                    string targetFolder = hasMetadata ? MetadataOutputFolder : NoMetadataOutputFolder;
                    if(!string.IsNullOrEmpty(targetFolder))
                    {
                        string targetPath = Path.Combine(targetFolder, Path.GetFileName(file));

                        File.Move(file, targetPath);

                        Console.WriteLine($"{file} -> {targetPath} [{(hasMetadata ? "WITH-METADATA" : "NO-METADATA")}]");
                    }
                    else
                    {
                        Console.WriteLine($"{file} kept in place [{(hasMetadata ? "WITH-METADATA" : "NO-METADATA")}]");
                    }                    

                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to handle file {file} with {ex.GetType().Name}: {ex.Message}");
                }

            }
        }

        private static string GetFFProbeOutput(string filePath)
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
