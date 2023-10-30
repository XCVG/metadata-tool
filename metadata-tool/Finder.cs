using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MetadataTool
{

    /// <summary>
    /// Finds the ID of files with no ID in the name
    /// </summary>
    internal class Finder
    {
        private string InputFolder;
        private string FoundOutputFolder;
        private string NotFoundOutputFolder;

        private string SiteOverride;
        private bool RenameFile;

        private bool MatchTitle;

        public Finder(string[] args)
        {
            InputFolder = Utils.GetArg<string>(args, "-i");
            FoundOutputFolder = Utils.GetArg<string>(args, "-ow");
            NotFoundOutputFolder = Utils.GetArg<string>(args, "-on");

            if (InputFolder == null)
            {
                InputFolder = Program.BaseDirectory;
            }

            if (FoundOutputFolder == null && NotFoundOutputFolder == null)
            {
                FoundOutputFolder = Path.Combine(InputFolder, "_WithMetadata");
                NotFoundOutputFolder = Path.Combine(InputFolder, "_NoMetadata");
            }
            else if (FoundOutputFolder == null) //missing one arg implies leaving it in place
            {
                NotFoundOutputFolder = Path.Combine(InputFolder, "_NoMetadata");
            }
            else if (NotFoundOutputFolder == null)
            {
                FoundOutputFolder = Path.Combine(InputFolder, "_WithMetadata");
            }

            SiteOverride = Utils.GetArg<string>(args, "-site");
            RenameFile = args.Contains("-rename");

            MatchTitle = args.Contains("-matchtitle");
        }

        public void Find()
        {
            Console.WriteLine("Mode: FIND id and metadata for files without an id even in the filename");
            Console.WriteLine("Input directory: " + InputFolder);
            Console.WriteLine("Found output directory: " + FoundOutputFolder);
            Console.WriteLine("Not-Found output directory: " + NotFoundOutputFolder);
            Console.WriteLine("Site override: " + (SiteOverride ?? "none"));
            Console.WriteLine("Rename files? " + (RenameFile ? "yes" : "no"));
            Console.WriteLine("Match title? " + (MatchTitle ? "yes" : "no"));
            Console.WriteLine("Press ENTER to continue or CTRL-C to abort!");

            //only support YouTube for now
            if(string.IsNullOrEmpty(SiteOverride))
            {
                SiteOverride = "youtube";
            }
            if(SiteOverride != "youtube")
            {
                throw new NotSupportedException("Only YouTube is supported for Finder");
            }

            if (Program.Interactive)
                Console.ReadLine();

            if (!string.IsNullOrEmpty(FoundOutputFolder))
            {
                Directory.CreateDirectory(FoundOutputFolder);
            }

            if (!string.IsNullOrEmpty(NotFoundOutputFolder))
            {
                Directory.CreateDirectory(NotFoundOutputFolder);
            }

            Thread.Sleep(1000); //anti-glitching

            var files = Directory.EnumerateFiles(InputFolder);
            foreach (var file in files)
            {
                try
                {
                    if (!Utils.IsVideoFileExtension(Path.GetExtension(file)))
                    {
                        Console.WriteLine($"{file} [IGNORE: NOT A VIDEO FILE]");
                        continue;
                    }

                    var fullFilePath = Path.GetFullPath(file);

                    //ffprobe for vital statistics
                    var dataString = Utils.GetFFProbeOutput(fullFilePath);
                    var data = JObject.Parse(dataString);

                    if (data["format"] == null || data["format"]["tags"] == null)
                    {
                        Console.WriteLine($"Skipping {file} because ffprobe returned no result");
                        continue;
                    }

                    double fileDuration = data["format"]["duration"].ToObject<double>();

                    //strip chars
                    string cleanedName = Path.GetFileNameWithoutExtension(file);
                    if (Regex.Match(cleanedName, "-\\s*[a-z,A-Z,0-9,_]{10,}").Success)
                    {
                        cleanedName = cleanedName.Substring(0, cleanedName.LastIndexOf('-') + 1).Trim();
                    }
                    cleanedName = RemoveSpecialCharactersCustom(cleanedName);

                    if(cleanedName.Length == 0)
                    {
                        Console.WriteLine($"Skipping {file} because name after cleaning is blank");
                        continue;
                    }

                    Console.WriteLine(cleanedName);                    

                    var tags = new Dictionary<string, string>();

                    /*
                    if (!string.IsNullOrEmpty(id))
                        tags.Add("MTOOL_BESTGUESS_ID", id);
                    if (!string.IsNullOrEmpty(website))
                        tags.Add("MTOOL_BESTGUESS_SITE", website);
                    */
                    tags.Add("MTOOL_ORIGINAL_FILENAME", Path.GetFileName(file));
                    tags.Add("MTOOL_BESTGUESS_SITE", SiteOverride);
                    tags.Add("MTOOL_SITE", SiteOverride);
                    tags.Add("MTOOL_FINDER_CONFIDENCE", "unspecified");

                    string id = null;

                    string searchResult = DoSearchDlp(cleanedName);
                    JObject searchObject = JObject.Parse(searchResult);
                    if (searchObject["entries"] != null && searchObject["entries"].Type == JTokenType.Array)
                    {
                        foreach(JObject entry in ((JArray)searchObject["entries"]))
                        {
                            double entryDuration = entry["duration"].ToObject<double>();

                            if(Math.Abs(fileDuration - entryDuration) > 1d)
                            {
                                continue;
                            }

                            if(MatchTitle)
                            {
                                string fileMinTitle = RemoveSpecialCharactersAggressive(Path.GetFileNameWithoutExtension(file));
                                string entryMinTitle = RemoveSpecialCharactersAggressive(entry["title"].ToString());
                                if (!fileMinTitle.Equals(entryMinTitle, StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }

                            //have id, save metadata to tags dictionary and continue
                            id = entry["id"].ToString();

                            var moreTags = GetMetadataTags(entry);
                            foreach(var tag in moreTags)
                            {
                                if(!tags.ContainsKey(tag.Key))
                                    tags[tag.Key] = tag.Value;
                            }

                            break;

                        }
                    }

                    if (id == null)
                    {
                        string nfTargetPath = Path.Combine(NotFoundOutputFolder, Path.GetFileName(file));

                        File.Move(file, nfTargetPath);

                        Console.WriteLine($"{file} -> {nfTargetPath} [NO MATCH]");

                        continue;
                    }

                    tags.Add("MTOOL_BESTGUESS_ID", id);
                    tags.Add("MTOOL_ID", id);

                    string destinationPath = null;
                    string newName = null;

                    if (RenameFile && tags.ContainsKey("Title"))
                    {
                        string cleanTitle = string.Join("_", tags["Title"].Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                        if (cleanTitle.Length > 128)
                            cleanTitle = cleanTitle.Substring(0, 128);
                        newName = $"{cleanTitle} - {id}{Path.GetExtension(file)}";
                    }
                    else
                    {
                        newName = Path.GetFileName(file);
                    }

                    string targetPath = Path.Combine(FoundOutputFolder, newName);
                    destinationPath = Utils.SetTagsAndCopy(file, targetPath, false, tags);
                    Thread.Sleep(100);
                    if(tags.ContainsKey("DATE"))
                    {
                        DateTime uploadDate;
                        uploadDate = DateTime.ParseExact(tags["DATE"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture);
                        File.SetLastWriteTime(destinationPath, uploadDate);
                    }

                    Console.WriteLine($"{file} -> {targetPath} ({id}) [OK]");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to handle file {file} with {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static string RemoveSpecialCharactersCustom(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(' ');
                }    
            }
            return sb.ToString();
        }

        private static string RemoveSpecialCharactersAggressive(string str)
        {
            return Regex.Replace(str, "[^a-zA-Z0-9]+", "");
        }


        private static string DoSearchDlp(string term)
        {
            string output = null;
            using (Process p = new Process())
            {
                p.StartInfo.FileName = "yt-dlp";
                p.StartInfo.WorkingDirectory = Path.GetDirectoryName(Program.BaseDirectory);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.Arguments = $" ytsearch10:\"{term}\" --skip-download  --dump-single-json";

                p.Start();

                StringBuilder sb = new StringBuilder();
                p.OutputDataReceived += new DataReceivedEventHandler((s, e) =>
                {
                    sb.Append(e.Data);
                }
                );
                p.BeginOutputReadLine();

                p.WaitForExit(60000);

                if (!p.HasExited)
                {
                    throw new Exception("yt-dlp took too long");
                }

                output = sb.ToString();
            }

            return output;
        }

        private static IReadOnlyDictionary<string, string> GetMetadataTags(JObject metadataObject)
        {
            var tags = new Dictionary<string, string>()
            {
                { "title", metadataObject["title"].ToString() },
                { "COMMENT", metadataObject["description"].ToString() },
                { "ARTIST", metadataObject["uploader"].ToString() },
                { "DATE", metadataObject["upload_date"].ToString() },
                { "DESCRIPTION", metadataObject["description"].ToString() },
                { "PURL", metadataObject["webpage_url"].ToString() },
                { "CHANNEL_ID", metadataObject["channel_id"].ToString() }
            };

            return tags;
        }
    }
}