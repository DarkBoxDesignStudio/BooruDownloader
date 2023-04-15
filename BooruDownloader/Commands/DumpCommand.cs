﻿using BooruDownloader.Utilities;
using HtmlAgilityPack;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDownloader.Commands
{
    public class DumpCommand
    {
        static Logger Log = LogManager.GetCurrentClassLogger();
        
        private const string weirdDateFormat = "ddd MMM d HH:mm:ss zz00 yyyy";

        public static async Task Run(string path, long startId, long endId, int parallelDownloads, bool ignoreHashCheck, bool includeDeleted, string booruSite)
        {
            string tempFolderPath = Path.Combine(path, "_temp");
            string imageFolderPath = Path.Combine(path, "images");
            string metadataDatabasePath = Path.Combine(path, "realbooru.sqlite");
            string lastPostJsonPath = Path.Combine(path, "last_post.json");
            
            PathUtility.CreateDirectoryIfNotExists(path);
            PathUtility.CreateDirectoryIfNotExists(tempFolderPath);
            PathUtility.CreateDirectoryIfNotExists(imageFolderPath);


            using (SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = metadataDatabasePath,
            }.ToString()))
            {
                connection.Open();

                SQLiteUtility.TryCreateTable(connection);

                while (true)
                {
                    // Get posts metadata as json
                    JObject[] postJObjects = null;

                    await TaskUtility.RunWithRetry(async () =>
                    {
                        Log.Info($"Downloading metadata ... ({startId} ~ )");
                        postJObjects = await RealBooruUtility.GetPosts(startId, booruSite);
                    }, e =>
                    {
                        Log.Error(e);
                        return true;
                    },
                    10,
                    3000);

                    if (postJObjects.Length == 0)
                    {
                        Log.Info("There is no posts.");
                        break;
                    }

                    // Validate post
                    Log.Info($"Checking {postJObjects.Length} posts ...");
                    Post[] posts = postJObjects.Select(p =>
                    {
                        Log.Trace($"Converting JObject to Post: {p}");
                        var post = ConvertToPost(p, booruSite);
                        Log.Trace($"Converted JObject to Post: {post}");
                        return post;
                    })
                    .Where(p => p != null && (endId <= 0 || long.Parse(p.Id) <= endId))
                    .ToArray();

                    Parallel.ForEach(posts, post =>
                    {
                        if (string.IsNullOrEmpty(post.Md5))
                        {
                            Log.Debug($"Skip for empty MD5 : Id={post.Id}");
                            return;
                        }

                        if (string.IsNullOrEmpty(post.ImageUrl))
                        {
                            Log.Debug($"Skip for empty image URL : Id={post.Id}");
                            return;
                        }

                        if (post.IsDeleted && !includeDeleted)
                        {
                            return;
                        }

                        if (post.IsPending)
                        {
                            return;
                        }

                        post.IsValid = true;

                        string metadataPath = GetPostLocalMetadataPath(imageFolderPath, post);

                        try
                        {
                            if (File.Exists(metadataPath))
                            {
                                Post cachedPost = ConvertToPost(JObject.Parse(File.ReadAllText(metadataPath)), booruSite);

                                if (cachedPost == null)
                                {
                                    post.ShouldSaveMetadata = true;
                                    post.ShouldUpdateImage = true;
                                }
                            }
                            else
                            {
                                post.ShouldSaveMetadata = true;
                                post.ShouldUpdateImage = true;
                            }
                        }
                        catch (Exception e)
                        {
                            post.ShouldSaveMetadata = true;
                            Log.Error(e);
                        }

                        string imagePath = GetPostLocalImagePath(imageFolderPath, post);

                        if (!File.Exists(imagePath))
                        {
                            post.ShouldDownloadImage = true;
                            return;
                        }
                        else
                        {
                            if (post.ShouldUpdateImage || !ignoreHashCheck)
                            {
                                string cachedImageMd5 = GetMd5Hash(imagePath);

                                if (post.Md5 != cachedImageMd5)
                                {
                                    post.ShouldDownloadImage = true;
                                    Log.Info($"MD5 is different to cached image : Id={post.Id}, {post.Md5} (new) != {cachedImageMd5} (cached)");
                                    return;
                                }
                            }
                        }
                    });

                    int shouldDownloadCount = posts.Where(p => p.ShouldDownloadImage).Count();
                    int shouldUpdateCount = posts.Where(p => p.ShouldSaveMetadata).Count();
                    int pendingCount = posts.Where(p => p.IsPending).Count();

                    if (shouldUpdateCount > 0 || shouldDownloadCount > 0)
                    {
                        Log.Info($"{shouldUpdateCount}/{posts.Length} posts are updated. {pendingCount} posts are pending. Downloading {shouldDownloadCount} posts ...");
                    }
                    var semaphore = new Semaphore(parallelDownloads, parallelDownloads);
                    Parallel.ForEach(posts, async post =>
                    {
                        if (!post.IsValid)
                        {
                            return;
                        }

                        string metadataPath = GetPostLocalMetadataPath(imageFolderPath, post);
                        string imagePath = GetPostLocalImagePath(imageFolderPath, post);
                        string tempImagePath = GetPostTempImagePath(tempFolderPath, post);

                        PathUtility.CreateDirectoryIfNotExists(Path.GetDirectoryName(imagePath));
                        semaphore.WaitOne();
                        try
                        {
                            await TaskUtility.RunWithRetry(async () =>
                            {
                                if (post.ShouldDownloadImage)
                                {
                                    Log.Info($"Downloading post {post.Id} at {post.ImageUrl} ...");
                                    await Download(post.ImageUrl, tempImagePath);
                                    Log.Info($"Downloaded image saved to: {tempImagePath}");


                                    string downloadedMd5 = GetMd5Hash(tempImagePath);
                                    Log.Trace($"Calculated MD5 hash for downloaded image: {downloadedMd5}");


                                    if (downloadedMd5 != post.Md5)
                                    {
                                        Log.Warn($"MD5 hash of downloaded image is different : Id={post.Id}, {post.Md5} (metadata) != {downloadedMd5} (downloaded)");
                                        try
                                        {
                                            File.Delete(tempImagePath);
                                        }
                                        finally { }

                                        try
                                        {
                                            File.Delete(metadataPath);
                                        }
                                        finally { }
                                        throw new Exception();
                                    }

                                    File.Delete(imagePath);
                                    File.Move(tempImagePath, imagePath);
                                }

                                if (post.ShouldDownloadImage || post.ShouldSaveMetadata)
                                {
                                    PathUtility.ChangeFileTimestamp(imagePath, post.CreatedDate, post.UpdatedDate);
                                }

                                if (post.ShouldSaveMetadata)
                                {
                                    File.WriteAllText(metadataPath, post.JObject.ToString());
                                }
                            }, e =>
                            {
                                return !(e is NotRetryableException);
                            }, 10, 3000);
                        }
                        catch (NotRetryableException)
                        {
                            Log.Error($"Unretryable exception was occured : Id={post.Id}");
                            post.IsValid = false;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    Log.Info("Updating database ...");
                    SQLiteUtility.InsertOrReplace(connection, posts.Where(p => p.IsValid).Select(p => p.JObject));

                    long lastId = long.Parse(posts.Last().Id);

                    startId = lastId + 1;
                }

                try
                {
                    Directory.Delete(tempFolderPath, true);
                }
                catch (Exception e)
                {
                    Log.Warn(e);
                }
                Log.Info("Dump command is complete.");
            }
        }

        static Post ConvertToPost(JObject jsonObject, string baseUrl)
        {
            try
            {
                if (jsonObject.GetValue("@id") == null && jsonObject.GetValue("id") != null)
                {
                    //This is garbage code but thats what happens when we reuse half the code of another app.
                    //Console.WriteLine(jsonObject.ToString());
                    throw new Exception("File id " + jsonObject.GetValue("id") + " already present in the database. Skipping.");
                }
                var id = jsonObject.GetValue("@id").ToString();
                var md5 = jsonObject.GetValue("@md5")?.ToString() ?? "";
                //Console.WriteLine(jsonObject.ToString());
                //var imageUrl = jsonObject.GetValue("@file_url")?.ToString() ?? "";
                var createdDate = DateTime.ParseExact(jsonObject.GetValue("@created_at").ToString(), weirdDateFormat, new CultureInfo("en-GB"));
                var updatedDate = jsonObject.GetValue("@updated_at") != null ? DateTime.ParseExact(jsonObject.GetValue("@updated_at").ToString(), weirdDateFormat, new CultureInfo("en-GB")) : createdDate;
                var isDeleted = jsonObject.GetValue("is_deleted")?.ToObject<bool>() ?? false;
                var isPending = jsonObject.GetValue("is_pending")?.ToObject<bool>() ?? false;

                HtmlAgilityPack.HtmlWeb web = new HtmlWeb();
                HtmlAgilityPack.HtmlDocument doc = web.Load(baseUrl + "/index.php?page=post&s=view&id=" + id);
                var imageUrl = doc.DocumentNode.SelectSingleNode("//img[@id='image']").Attributes["src"].Value;
                var extension = System.IO.Path.GetExtension(imageUrl);
                JObject newJsonObject = new JObject();
                newJsonObject["id"] = id;
                newJsonObject["md5"] = md5;
                newJsonObject["file_ext"] = extension;
                newJsonObject["tag_string"] = jsonObject.GetValue("@tags").ToString().Trim();
                newJsonObject["tag_count_general"] = newJsonObject["tag_string"].ToString().Split(" ").Length;
                Post post = new Post()
                {
                    Id = id,
                    Md5 = md5,
                    Extension = extension,
                    ImageUrl = imageUrl,
                    CreatedDate = createdDate,
                    UpdatedDate = updatedDate,
                    IsDeleted = isDeleted,
                    IsPending = isPending,
                    JObject = newJsonObject,
                    IsValid = ((extension == "jpeg") || (extension == "jpg") || (extension == "png"))
                };

                if (post.UpdatedDate < post.CreatedDate)
                {
                    post.UpdatedDate = post.CreatedDate;
                }
                return post;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        static string GetPostLocalMetadataPath(string imageFolderPath, Post post)
        {
            return Path.Combine(imageFolderPath, post.Md5.Substring(0, 2), $"{post.Md5}-realbooru.json");
        }

        static string GetPostLocalImagePath(string imageFolderPath, Post post)
        {
            return Path.Combine(imageFolderPath, post.Md5.Substring(0, 2), $"{post.Md5}.{post.Extension}");
        }

        static string GetPostTempImagePath(string tempFolderPath, Post post)
        {
            return Path.Combine(tempFolderPath, $"{post.Md5}.{post.Extension}");
        }

        static string GetMd5Hash(string path)
        {
            using (MD5 md5 = MD5.Create())
            {
                using (FileStream stream = new FileStream(path, FileMode.Open))
                {
                    byte[] hashBytes = md5.ComputeHash(stream);

                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
        }

        static async Task Download(string uri, string path)
        {
            //Console.WriteLine(uri);
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.Forbidden:
                        Console.WriteLine("Forbiden");
                        throw new NotRetryableException();
                    case HttpStatusCode.NotFound:
                        Console.WriteLine("NotFound");
                        throw new NotRetryableException();
                }

                response.EnsureSuccessStatusCode();

                using (FileStream fileStream = File.Create(path))
                {
                    using (Stream httpStream = await response.Content.ReadAsStreamAsync())
                    {
                        httpStream.CopyTo(fileStream);
                        fileStream.Flush();
                    }
                }
            }
        }

        class Post
        {
            public string Id;
            public string Md5;
            public string Extension;
            public string ImageUrl;
            public DateTime CreatedDate;
            public DateTime UpdatedDate;
            public bool IsPending;
            public bool IsDeleted;
            public JObject JObject;

            public bool IsValid;
            public bool ShouldSaveMetadata;
            public bool ShouldDownloadImage;
            public bool ShouldUpdateImage;
        }

        class NotRetryableException : Exception { }
    }
}