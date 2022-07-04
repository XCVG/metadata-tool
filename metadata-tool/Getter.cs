using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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

        private string SiteOverride;
        private bool RenameFile;
        private bool UseFilenameAsId;

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

            SiteOverride = Utils.GetArg<string>(args, "-site");
            RenameFile = args.Contains("-rename");
            UseFilenameAsId = args.Contains("-use-filename");
        }

        public void Get()
        {
            Console.WriteLine("Mode: GET metadata from the internet for files with known id");
            Console.WriteLine("Input directory: " + InputFolder);
            Console.WriteLine("Retrieved-Metadata output directory: " + MetadataOutputFolder);
            Console.WriteLine("Missing-Metadata output directory: " + NoMetadataOutputFolder);
            Console.WriteLine("Site override: " + (SiteOverride ?? "none"));
            Console.WriteLine("Use filename as ID? " + (UseFilenameAsId ? "yes" : "no"));
            Console.WriteLine("Rename files? " + (RenameFile ? "yes" : "no"));
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

                    if(data["format"] == null || data["format"]["tags"] == null)
                    {
                        RejectFile(file, "invalid ffprobe result");
                        continue;
                    }

                    //these are needed
                    string site = null, id = null;

                    //these are desired
                    DateTime? uploadDate = null;
                    double? duration = null;

                    var tagData = data["format"]["tags"];
                    if (tagData != null && tagData.Type == JTokenType.Object)
                    {
                        if (tagData["MTOOL_ID"] != null && tagData["MTOOL_ID"].Type == JTokenType.String)
                        {
                            id = tagData["MTOOL_ID"].ToString();
                        }
                        else if (tagData["PURL"] != null && tagData["PURL"].Type == JTokenType.String)
                        {
                            string purl = tagData["PURL"].ToString();
                            if (purl.Contains("youtube", StringComparison.OrdinalIgnoreCase) || purl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                            {
                                site = "youtube";
                                string pattern = "watch\\?v=([A-Za-z0-9_\\-]+)";
                                var match = Regex.Match(purl, pattern);
                                if (match.Success)
                                {
                                    id = match.Groups[1].Value;
                                }

                            }
                        }
                        //TODO other sites
                        else
                        {
                            if (tagData["MTOOL_BESTGUESS_SITE"] != null && tagData["MTOOL_BESTGUESS_SITE"].Type == JTokenType.String)
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

                    if (!string.IsNullOrEmpty(SiteOverride))
                        site = SiteOverride;

                    if (string.IsNullOrEmpty(id) && UseFilenameAsId)
                        id = Path.GetFileNameWithoutExtension(file);

                    //if site or id missing, treat as no metadata
                    if(site == null || id == null)
                    {
                        RejectFile(file, "missing id or site");
                        continue;
                    }

                    //actually get metadata (delegated to handlers)
                    RetrievedMetadata retrievedMetadata = null;
                    try
                    {
                        FileMetadata fileMetadata = new FileMetadata()
                        {
                            Id = id,
                            Site = site,
                            Duration = duration,
                            Date = uploadDate
                        };                        

                        if (site == "youtube")
                        {
                            if (!Regex.IsMatch(id, "^[A-Za-z0-9_\\-]{11}$"))
                            {
                                RejectFile(file, "id not in correct format for youtube");
                                continue;
                            }

                            retrievedMetadata = GetMetadataYoutube(fileMetadata);
                        }
                        else if(site == "imgur")
                        {
                            retrievedMetadata = GetMetadataImgur(fileMetadata);
                        }
                        else
                        {
                            throw new NotImplementedException("other sites not implemented yet");
                        }
                    }
                    catch(MetadataDownloadException mde)
                    {
                        RejectFile(file, mde.Message);
                        continue;
                    }

                    string destinationPath = null;
                    string newName = null;

                    if(RenameFile)
                    {
                        string cleanTitle = string.Join("_", retrievedMetadata.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                        if (cleanTitle.Length > 128)
                            cleanTitle = cleanTitle.Substring(0, 128);
                        newName = $"{cleanTitle} - {id}{Path.GetExtension(file)}";
                    }

                    if(!string.IsNullOrEmpty(MetadataOutputFolder))
                    {
                        string targetPath = Path.Combine(MetadataOutputFolder, newName ?? Path.GetFileName(file));
                        destinationPath = Utils.SetTagsAndCopy(file, targetPath, false, retrievedMetadata.Tags);
                        Console.WriteLine($"{file} -> {targetPath} [OK]");
                    }
                    else if (newName != null)
                    {
                        string targetPath = Path.Combine(InputFolder, newName);
                        destinationPath = Utils.SetTagsAndCopy(file, targetPath, false, retrievedMetadata.Tags);
                        Console.WriteLine($"{file} -> {targetPath} [OK]");
                    }
                    else
                    {
                        destinationPath = Utils.SetTagsAndCopy(file, Path.Combine(TempFolder, Path.GetFileName(file)), false, retrievedMetadata.Tags);
                        Thread.Sleep(100);
                        string newDestPath = Path.GetFileName(destinationPath);
                        File.Move(destinationPath, Path.Combine(InputFolder, newDestPath));
                        Thread.Sleep(100);
                        destinationPath = newDestPath;
                        Console.WriteLine($"{file} kept in place [OK]");
                    }

                    if(retrievedMetadata.UploadDate.HasValue)
                    {
                        File.SetLastWriteTime(destinationPath, retrievedMetadata.UploadDate.Value);
                    }

                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to handle file {file} with {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private RetrievedMetadata GetMetadataYoutube(FileMetadata data)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>();

            string url = $"https://www.youtube.com/watch?v={data.Id}";

            JObject metadataObject = null;
            string metadataString = DownloadMetadataDlp(url);

            try
            {
                metadataObject = JObject.Parse(metadataString);
            }
            catch (Exception e)
            {
                throw new MetadataDownloadException("couldn't parse metadata json");
            }

            DateTime uploadDate;
            uploadDate = DateTime.ParseExact(metadataObject["upload_date"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture);

            //upload date and duration sanity checks if available
            if (data.Date != null)
            {
                if(uploadDate.Date != data.Date)
                {
                    throw new MetadataDownloadException("failed upload date check");
                }
            }

            if (data.Duration != null)
            {
                if (metadataObject["duration"] != null)
                {
                    if (double.TryParse(metadataObject["duration"].ToString(), out var mdDuration))
                    {
                        if (Math.Abs(mdDuration - data.Duration.Value) > DURATION_EPSILON)
                        {
                            throw new MetadataDownloadException("failed duration check");
                        }
                    }
                }
            }

            //prepare and set metadata
            tags = new Dictionary<string, string>()
            {
                { "title", metadataObject["title"].ToString() },
                { "COMMENT", metadataObject["description"].ToString() },
                { "ARTIST", metadataObject["uploader"].ToString() },
                { "DATE", metadataObject["upload_date"].ToString() },
                { "DESCRIPTION", metadataObject["description"].ToString() },
                { "PURL", metadataObject["webpage_url"].ToString() },
                { "CHANNEL_ID", metadataObject["channel_id"].ToString() },
                { "MTOOL_ID", data.Id},
                { "MTOOL_SITE", data.Site }
            };

            return new RetrievedMetadata()
            {
                MetadataObject = metadataObject,
                MetadataString = metadataString,
                UploadDate = uploadDate,
                Tags = tags,
                Title = metadataObject["title"].ToString()
            };
        }

        private RetrievedMetadata GetMetadataImgur(FileMetadata data)
        {
            string metadataString = DownloadMetadataImgur($"https://imgur.com/gallery/{data.Id}");
            if(metadataString == null)
            {
                //also try url of the format https://imgur.com/{id}
                metadataString = DownloadMetadataImgur($"https://imgur.com/{data.Id}");
            }
            if (metadataString == null)
            {
                throw new MetadataDownloadException("can't find json data in response (probably missing)");
            }

            JObject metadataObject = JObject.Parse(metadataString);
            
            DateTime creationDate = DateTime.Parse(metadataObject["created_at"].ToString());

            //sanity checks
            if(data.Date.HasValue)
            {
                if(Math.Abs((creationDate - data.Date.Value).TotalHours) > 24)
                {
                    throw new MetadataDownloadException("failed creation date check");
                }
            }

            if (metadataObject["media"][0] != null && 
                metadataObject["media"][0] != null &&
                metadataObject["media"][0]["type"] != null &&
                metadataObject["media"][0]["type"].ToString() != "image" &&
                metadataObject["media"][0]["metadata"] != null && 
                metadataObject["media"][0]["metadata"]["duration"] != null)
            {
                if (double.TryParse(metadataObject["media"][0]["metadata"]["duration"].ToString(), out var mdDuration))
                {
                    if (Math.Abs(mdDuration - data.Duration.Value) > DURATION_EPSILON)
                    {
                        throw new MetadataDownloadException("failed duration check");
                    }
                }
            }

            var tags = new Dictionary<string, string>()
            {
                { "title", metadataObject["title"].ToString() },
                { "COMMENT", metadataObject["description"].ToString() },
                { "ARTIST", metadataObject["account"]["username"].ToString() },
                { "DATE", creationDate.Date.ToString("yyyyMMdd") },
                { "DESCRIPTION", metadataObject["description"].ToString() },
                { "PURL", metadataObject["url"].ToString() },
                { "UPLOADER_ID", metadataObject["account_id"].ToString() },
                { "MTOOL_ID", data.Id},
                { "MTOOL_SITE", data.Site },
                { "MTOOL_RAW_DATE", metadataObject["created_at"].ToString() }
            };

            return new RetrievedMetadata()
            {
                MetadataObject = metadataObject,
                MetadataString = metadataString,
                UploadDate = creationDate,
                Tags = tags,
                Title = metadataObject["title"].ToString()
            };
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

        private static string DownloadMetadataDlp(string url)
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

        private static string DownloadMetadataImgur(string url)
        {
            string htmlData = null;

            Task.Run(async () =>
            {
                using (var httpClient = new HttpClient())
                {
                    using (var request = new HttpRequestMessage(new HttpMethod("GET"), url))
                    {
                        var response = await httpClient.SendAsync(request);
                        htmlData = await response.Content.ReadAsStringAsync();
                    }
                }
            }).Wait();

            if (string.IsNullOrWhiteSpace(htmlData))
                return null;

            string matchPattern = "<script>[^\"]+\"{.*}\"<\\/script>";
            var match = Regex.Match(htmlData, matchPattern);
            if (!match.Success)
            {
                return null;
            }

            int startIndex = match.Value.IndexOf('{');
            int endIndex = match.Value.LastIndexOf('}') - startIndex + 1;
            string metadataString = match.Value.Substring(startIndex, endIndex).Replace("\\\"", "\"").Replace("\\\\", "\\");

            return metadataString;
        }

        private class FileMetadata
        {
            public string Site;
            public string Id;
            public DateTime? Date;
            public double? Duration;
        }

        private class RetrievedMetadata
        {
            public string MetadataString;
            public JObject MetadataObject;
            public Dictionary<string, string> Tags;
            public string Title;
            public DateTime? UploadDate;
        }

        private class MetadataDownloadException : Exception
        {
            public MetadataDownloadException(string message) : base(message) { }
        }

    }
}
