using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MetadataTool
{

    /// <summary>
    /// Guesses ID and origin info based on filename for files without metadata
    /// </summary>
    internal class Guesser
    {
        private string InputFolder;
        private string BaseOutputFolder;
        //private bool Online; //whether to go out and actually check or not

        public Guesser(string[] args)
        {
            InputFolder = Utils.GetArg<string>(args, "-i");
            BaseOutputFolder = Utils.GetArg<string>(args, "-o");

            if (InputFolder == null)
            {
                InputFolder = Program.BaseDirectory;
            }

            if (BaseOutputFolder == null)
            {
                BaseOutputFolder = InputFolder;
            }
        }

        public void Guess()
        {
            Console.WriteLine("Mode: Guess ID and website for files based on filename");
            Console.WriteLine("Input directory: " + InputFolder);
            Console.WriteLine("Output base directory: " + BaseOutputFolder);
            Console.WriteLine("Press ENTER to continue or CTRL-C to abort!");

            if (Program.Interactive)
                Console.ReadLine();

            string youtubeDir = Path.Combine(BaseOutputFolder, "_youtube");
            Directory.CreateDirectory(youtubeDir);
            string redditDir = Path.Combine(BaseOutputFolder, "_reddit");
            Directory.CreateDirectory(redditDir);
            string imgurDir = Path.Combine(BaseOutputFolder, "_imgur");
            Directory.CreateDirectory(imgurDir);
            string twitterDir = Path.Combine(BaseOutputFolder, "_twitter");
            Directory.CreateDirectory(twitterDir);
            string unknownDir = Path.Combine(BaseOutputFolder, "_unknown");
            Directory.CreateDirectory(unknownDir);

            Thread.Sleep(1000); //anti-glitching

            var files = Directory.EnumerateFiles(InputFolder);
            foreach(var file in files)
            {
                try
                {
                    string website = null;
                    string id = null;

                    var extension = Path.GetExtension(file);

                    if (!Utils.IsVideoFileExtension(extension))
                    {
                        Console.WriteLine($"{file} [NOT A VIDEO FILE]");
                        continue;
                    }

                    var fileName = Path.GetFileNameWithoutExtension(file);

                    //"redditsave.com": reddit, id is probably at the end after last -
                    if (fileName.StartsWith("redditsave.com", StringComparison.OrdinalIgnoreCase))
                    {
                        website = "reddit";
                        id = fileName.Substring(fileName.LastIndexOf('-') + 1).Trim();
                    }                   
                    //"Imgur", square brackets with text: imgur, id is in square brackets
                    else if(fileName.StartsWith("Imgur") && fileName.Contains('[') && fileName.Contains(']'))
                    {
                        website = "imgur";
                        id = fileName.Substring(fileName.IndexOf('[')).Trim('[', ']');
                    }
                    //closes on ], has opening [: possibly youtube, id is in square brackets
                    else if(fileName.EndsWith(']') && fileName.Contains('['))
                    {
                        id = fileName.Substring(fileName.IndexOf('[')).Trim('[', ']');
                        if(id.Length == 11)
                        {
                            website = "youtube";
                        }
                        else if(id.Length > 11 && ulong.TryParse(id, out _))
                        {
                            website = "twitter";
                        }
                    }
                    //ends on a run of >10 chars, after -: probably youtube, last run of chars is id
                    else if(Regex.Match(fileName, "-\\s*[a-z,A-Z,0-9]{10,}").Success)
                    {
                        website = "youtube";
                        id = fileName.Substring(fileName.LastIndexOf('-') + 1).Trim();
                    }
                    //no spaces, <15 characters, no brackets: probably imgur or twitter, entire filename is probably id
                    else if(!fileName.Contains(' ') && !fileName.Contains('[') && !fileName.Contains(']'))
                    {
                        id = fileName;
                        if (id.Length > 11 && ulong.TryParse(id, out _))
                        {
                            website = "twitter";
                        }
                    }

                    //out of scope now: if in online mode, go and check if video exists (somehow)

                    if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(website))
                    {
                        Console.WriteLine($"{file} [UNKNOWN ID, UNKNOWN SITE]");
                        continue;
                    }

                    Console.WriteLine($"{file} [{(string.IsNullOrEmpty(id) ? "UNKNOWN ID" : id)}, {(string.IsNullOrEmpty(website) ? "UNKNOWN SITE" : website)}]");

                    string destinationDir;

                    switch (website)
                    {
                        case "youtube":
                            destinationDir = youtubeDir;
                            break;
                        case "imgur":
                            destinationDir = imgurDir;
                            break;
                        case "reddit":
                            destinationDir = redditDir;
                            break;
                        case "twitter":
                            destinationDir = twitterDir;
                            break;
                        default:
                            destinationDir = unknownDir;
                            break;
                    }

                    var tags = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(id))
                        tags.Add("MTOOL_BESTGUESS_ID", id);
                    if (!string.IsNullOrEmpty(website))
                        tags.Add("MTOOL_BESTGUESS_SITE", website);

                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        SetTagsAndCopy(file, Path.Combine(destinationDir, Path.GetFileName(file)), false, tags);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"{file} [ERROR {ex.GetType().Name}: {ex.Message}]");
                }
            }

        }

        private static void SetTagsAndCopy(string source, string destination, bool keepOriginal, IDictionary<string, string> tags)
        {
            //ffmpeg -i ''  -c:v copy -c:a copy -c:s copy -map 0 -metadata  MTOOL_BESTGUESS_ID="UG6x5w6TiUI" -metadata MTOOL_BESTGUESS_SITE="youtube" ''

            //special case where an mp4 and a webm exists
            string originalExtension = "";
            if(File.Exists(Path.Combine(Path.GetDirectoryName(source), Path.GetFileNameWithoutExtension(source) + ".webm")) && File.Exists(Path.Combine(Path.GetDirectoryName(source), Path.GetFileNameWithoutExtension(source) + ".mp4")))
            {
                originalExtension = Path.GetExtension(source);
            }

            destination = Path.Combine(Path.GetDirectoryName(destination), Path.GetFileNameWithoutExtension(destination) + originalExtension + ".mkv");
            if(File.Exists(destination))
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
                
            if(!File.Exists(destination))
            {
                throw new FileNotFoundException("ffmpeg failed to create file");
            }

            var modifiedDate = File.GetLastWriteTime(source);
            File.SetLastWriteTime(destination, modifiedDate);

            if(!keepOriginal)
            {
                File.Delete(source);
            }

            Thread.Sleep(100); //make sure FS changes are committed

        }

    }
}
