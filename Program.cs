// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Standard.EBooks.Downloader
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// The main program class.
    /// </summary>
    internal static class Program
    {
        private static readonly string Uri = "https://standardebooks.org/ebooks/?page={0}";

        /// <summary>
        /// The main entry point.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        private static void Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.SystemDefault | System.Net.SecurityProtocolType.Tls12;

            using (var calibreLibrary = new CalibreLibrary(args[0]))
            {
                var page = 1;
                while (true)
                {
                    var any = false;
                    foreach (var value in ProcessPage(page))
                    {
                        foreach (var epub in ProcessBook(value))
                        {
                            // download this
                            var epubInfo = EpubInfo.Parse(DownloadEpub(epub, ".\\"));

                            if (calibreLibrary.UpdateIfExists(epubInfo))
                            {
                                System.Console.WriteLine("\tDeleting, {0} - {1}", epubInfo.Title, string.Join("; ", epubInfo.Authors));
                                System.IO.File.Delete(epubInfo.Path);
                            }
                        }

                        any = true;
                    }

                    if (!any)
                    {
                        break;
                    }

                    page++;
                }
            }
        }

        private static IEnumerable<Uri> ProcessPage(int page)
        {
            Console.WriteLine("Processing page {0}", page);
            var pageUri = new Uri(string.Format(Uri, page));
            string html = null;
            using (var client = new System.Net.WebClient())
            {
                html = client.DownloadString(pageUri);
            }

            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(html);

            if (document.ParseErrors?.Any() == true)
            {
                // Handle any parse errors as required
            }

            if (document.DocumentNode != null)
            {
                // find all the links to the books
                var nodes = document.DocumentNode.SelectNodes("//body/main/article[@class='ebooks']/ol/li/a");
                if (nodes == null)
                {
                    yield break;
                }

                int count = -1;
                try
                {
                    count = nodes.Count;
                }
                catch (NullReferenceException)
                {
                    yield break;
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    // get the html attribute
                    var link = nodes[i].GetAttributeValue("href", string.Empty);
                    yield return new Uri(pageUri, link);
                }
            }
        }

        private static IEnumerable<Uri> ProcessBook(Uri uri)
        {
            Console.WriteLine("\tProcessing book {0}", uri.Segments.Last());
            string html = null;
            using (var client = new System.Net.WebClient())
            {
                html = client.DownloadString(uri);
            }

            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(html);

            if (document.ParseErrors?.Any() == true)
            {
                // Handle any parse errors as required
            }

            if (document.DocumentNode != null)
            {
                var nodes = document.DocumentNode.SelectNodes("//body/main/article[@class='ebook']/section[@id='download']/ul/li/p/span/a[@class='epub']");
                foreach (var node in nodes)
                {
                    var link = node.GetAttributeValue("href", string.Empty);
                    yield return new Uri(uri, link);
                }
            }
        }

        private static string DownloadEpub(Uri uri, string path)
        {
            // create the file name
            var fileName = uri.Segments.Last();
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(path, fileName));

            // get the last part of the URI
            if (System.IO.File.Exists(fileName))
            {
                return fullPath;
            }

            Console.WriteLine("\tDownloading book {0}", fileName);
            using (var client = new System.Net.WebClient())
            {
                client.DownloadFile(uri, fullPath);
            }

            return fullPath;
        }
    }
}
