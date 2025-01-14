﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

namespace BooruDownloader.Utilities
{
    public static class RealBooruUtility
    {
        public static string GetPostsUrl(long startId, string booruSite)
        {
            string query = $"-webm -gif -animated -sound -video -user:rb id:>={startId} sort:id:asc";
            string urlEncodedQuery = WebUtility.UrlEncode(query);
            return $"{booruSite}/index.php?page=dapi&s=post&q=index&tags={urlEncodedQuery}&pid=0&limit=10";
        }

        public static async Task<JObject[]> GetPosts(long startId, string booruSite)
        {
            using (HttpClient client = new HttpClient())
            {
                string url = GetPostsUrl(startId, booruSite);
                string xmlResp = await client.GetStringAsync(url);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlResp);
                string jsonText = JsonConvert.SerializeXmlNode(doc);

                JObject root = JObject.Parse(jsonText);
                JArray jsonArray = (JArray)root["posts"]["post"];
                return jsonArray.Cast<JObject>().ToArray();
            }
        }
    }
}
