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
    /// Gets metadata for a file with known ID and site
    /// </summary>
    internal class Getter
    {
        private const double DURATION_EPSILON = 2d;

        private string InputFolder;
        private string MetadataOutputFolder;
        private string NoMetadataOutputFolder;

        private string TempFolder;

        public Getter(string[] args)
        {
            InputFolder = Utils.GetArg<string>(args, "-i");
            MetadataOutputFolder = Utils.GetArg<string>(args, "-ow");
            NoMetadataOutputFolder = Utils.GetArg<string>(args, "-on");

            if (InputFolder == null)
            {
                InputFolder = Program.BaseDirectory;
            }

            if (MetadataOutputFolder == null && NoMetadataOutputFolder == null)
            {
                MetadataOutputFolder = Path.Combine(InputFolder, "_RetrievedMetadata");
                NoMetadataOutputFolder = Path.Combine(InputFolder, "_MissingMetadata");
            }
            else if (MetadataOutputFolder == null) //missing one arg implies leaving it in place
            {
                NoMetadataOutputFolder = Path.Combine(InputFolder, "_MissingMetadata");
            }
            else if (NoMetadataOutputFolder == null)
            {
                MetadataOutputFolder = Path.Combine(InputFolder, "_RetrievedMetadata");
            }

            TempFolder = Path.Combine(InputFolder, "_TEMP");
        }

        public void Get()
        {
            Console.WriteLine("Mode: GET metadata from the internet for files with known id");
            Console.WriteLine("Input directory: " + InputFolder);
            Console.WriteLine("Retrieved-Metadata output directory: " + MetadataOutputFolder);
            Console.WriteLine("Missing-Metadata output directory: " + NoMetadataOutputFolder);
            Console.WriteLine("Press ENTER to continue or CTRL-C to abort!");

            if (Program.Interactive)
                Console.ReadLine();

            if (!string.IsNullOrEmpty(MetadataOutputFolder))
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
                    if (!Utils.IsVideoFileExtension(Path.GetExtension(file)))
                    {
                        Console.WriteLine($"{file} [IGNORE: NOT A VIDEO FILE]");
                        continue;
                    }

                    var fullFilePath = Path.GetFullPath(file);

                    var dataString = Utils.GetFFProbeOutput(fullFilePath);
                    var data = JObject.Parse(dataString);

                    //these are needed
                    string site = null, id = null;

                    //these are desired
                    DateTime? uploadDate = null;
                    double? duration = null;

                    var tagData = data["format"]["tags"];
                    if (tagData != null && tagData.Type == JTokenType.Object)
                    {
                        if (tagData["PURL"] != null && tagData["PURL"].Type == JTokenType.String)
                        {
                            string purl = tagData["PURL"].ToString();
                            if (purl.Contains("youtube", StringComparison.OrdinalIgnoreCase) || purl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                            {
                                site = "youtube";
                                string pattern = "watch\\?v=([A-Za-z0-9_\\-]+)";
                                var match = Regex.Match(purl, pattern);
                                if(match.Success)
                                {
                                    id = match.Groups[1].Value;
                                }

                            }
                        }
                        //TODO other sites
                        else
                        {
                            if(tagData["MTOOL_BESTGUESS_SITE"] != null && tagData["MTOOL_BESTGUESS_SITE"].Type == JTokenType.String)
                            {
                                site = tagData["MTOOL_BESTGUESS_SITE"].ToString();
                            }
                            if (tagData["MTOOL_BESTGUESS_ID"] != null && tagData["MTOOL_BESTGUESS_ID"].Type == JTokenType.String)
                            {
                                id = tagData["MTOOL_BESTGUESS_ID"].ToString();
                            }
                        }

                        if (tagData["DATE"] != null && tagData["DATE"].Type == JTokenType.String)
                        {
                            uploadDate = DateTime.ParseExact(tagData["DATE"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture);
                        }

                    }

                    if(data["format"]["duration"] != null)
                    {
                        if (double.TryParse(data["format"]["duration"].ToString(), out var d))
                            duration = d;
                    }

                    //if site or id missing, treat as no metadata
                    if(site == null || id == null)
                    {
                        RejectFile(file, "missing id or site");
                        continue;
                    }

                    string url = null;
                    if(site == "youtube")
                    {
                        if(!Regex.IsMatch(id, "^[A-Za-z0-9_\\-]{11}$"))
                        {
                            RejectFile(file, "id not in correct format for youtube");
                            continue;
                        }

                        url = $"https://www.youtube.com/watch?v={id}";
                    }
                    else
                    {
                        throw new NotImplementedException("other sites not implemented yet");
                    }

                    JObject metadataObject = null;
                    string metadataString = DownloadMetadata(url);

                    try
                    {
                        metadataObject = JObject.Parse(metadataString);
                    }
                    catch(Exception e)
                    {
                        RejectFile(file, "couldn't parse metadata json");
                        continue;
                    }

                    //upload date and duration sanity checks if available
                    if(uploadDate != null)
                    {
                        if (metadataObject["upload_date"] != null && metadataObject["upload_date"].Type == JTokenType.String)
                        {
                            DateTime mdUploadDate = DateTime.ParseExact(tagData["DATE"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture);
                            if (mdUploadDate.Date != uploadDate.Value.Date)
                            {
                                RejectFile(file, "failed upload date check");
                                continue;
                            }                                
                        }
                    }

                    if(duration != null)
                    {
                        if (metadataObject["duration"] != null)
                        {
                            if(double.TryParse(metadataObject["duration"].ToString(), out var mdDuration))
                            {
                                if(Math.Abs(mdDuration - duration.Value) > DURATION_EPSILON)
                                {
                                    RejectFile(file, "failed duration check");
                                    continue;
                                }
                            }
                        }
                    }

                    //prepare and set metadata
                    var tags = new Dictionary<string, string>()
                    {
                        { "title", metadataObject["title"].ToString() },
                        { "COMMENT", metadataObject["description"].ToString() },
                        { "ARTIST", metadataObject["uploader"].ToString() },
                        { "DATE", metadataObject["upload_date"].ToString() },
                        { "DESCRIPTION", metadataObject["description"].ToString() },
                        { "PURL", metadataObject["webpage_url"].ToString() },
                        { "CHANNEL_ID", metadataObject["channel_id"].ToString() },
                        { "MTOOL_ID", id},
                        { "MTOOL_SITE", site }
                    };

                    string destinationPath = null;
                    if(!string.IsNullOrEmpty(MetadataOutputFolder))
                    {
                        string targetPath = Path.Combine(MetadataOutputFolder, Path.GetFileName(file));
                        destinationPath = Utils.SetTagsAndCopy(file, targetPath, false, tags);
                        Console.WriteLine($"{file} -> {targetPath} [OK]");
                    }
                    else
                    {
                        destinationPath = Utils.SetTagsAndCopy(file, Path.Combine(TempFolder, Path.GetFileName(file)), false, tags);
                        Thread.Sleep(100);
                        string newDestPath = Path.GetFileName(destinationPath);
                        File.Move(destinationPath, Path.Combine(InputFolder, newDestPath));
                        Thread.Sleep(100);
                        destinationPath = newDestPath;
                        Console.WriteLine($"{file} kept in place [OK]");
                    }

                    if(DateTime.TryParseExact(metadataObject["upload_date"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var uDate))
                    {
                        File.SetLastWriteTime(destinationPath, uDate);
                    }

                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to handle file {file} with {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private void RejectFile(string file, string message)
        {
            if (!string.IsNullOrEmpty(NoMetadataOutputFolder))
            {
                string targetPath = Path.Combine(NoMetadataOutputFolder, Path.GetFileName(file));

                File.Move(file, targetPath);

                Console.WriteLine($"{file} -> {targetPath} [FAIL: {message}]");
            }
            else
            {
                Console.WriteLine($"{file} kept in place [FAIL: {message}]");
            }
        }

        private static string DownloadMetadata(string url)
        {
            string output = null;
            using (Process p = new Process())
            {
                p.StartInfo.FileName = "yt-dlp";
                p.StartInfo.WorkingDirectory = Path.GetDirectoryName(Program.BaseDirectory);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.Arguments = $"--skip-download --no-clean-infojson --dump-json {url}";

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

    }
}
