﻿// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Standard.EBooks.Downloader
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive;
    using System.Reactive.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The main program class.
    /// </summary>
    internal class Program
    {
        private static readonly string Uri = "https://standardebooks.org/ebooks/?page={0}";

        private static readonly ILoggerFactory LoggerFactory;

        private static readonly ILogger ProgramLogger;

        static Program()
        {
            LoggerFactory = new LoggerFactory().AddConsole();
            ProgramLogger = LoggerFactory.CreateLogger<Program>();
        }

        /// <summary>
        /// The main entry point.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The main application task.</returns>
        private static async Task Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.SystemDefault | System.Net.SecurityProtocolType.Tls12;

            using (var calibreLibrary = new CalibreLibrary(args[0], LoggerFactory.CreateLogger<CalibreLibrary>()))
            {
                var page = 1;
                while (true)
                {
                    var any = false;
                    foreach (var value in ProcessPage(page).ToEnumerable())
                    {
                        foreach (var epub in ProcessBook(value))
                        {
                            // download this
                            var epubInfo = EpubInfo.Parse(await DownloadEpub(epub, ".\\"));

                            if (calibreLibrary.UpdateIfExists(epubInfo))
                            {
                                ProgramLogger.LogInformation("\tDeleting, {0} - {1}", epubInfo.Title, string.Join("; ", epubInfo.Authors));
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

        private static IObservable<Uri> ProcessPage(int page)
        {
            return System.Reactive.Linq.Observable.Create<Uri>(async obs =>
            {
                ProgramLogger.LogInformation("Processing page {0}", page);
                var pageUri = new Uri(string.Format(Uri, page));

                var document = new HtmlAgilityPack.HtmlDocument();
                document.LoadHtml(await pageUri.DownloadAsStringAsync());

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
                        return;
                    }

                    int count = -1;
                    try
                    {
                        count = nodes.Count;
                    }
                    catch (NullReferenceException)
                    {
                        return;
                    }

                    for (int i = 0; i < nodes.Count; i++)
                    {
                        // get the html attribute
                        var link = nodes[i].GetAttributeValue("href", string.Empty);
                        obs.OnNext(new Uri(pageUri, link));
                    }
                }
            });
        }

        private static IEnumerable<Uri> ProcessBook(Uri uri)
        {
            ProgramLogger.LogInformation("\tProcessing book {0}", uri.Segments.Last());
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

        private static async Task<string> DownloadEpub(Uri uri, string path)
        {
            // create the file name
            var fileName = uri.Segments.Last();
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(path, fileName));

            // get the last part of the URI
            if (System.IO.File.Exists(fileName))
            {
                return fullPath;
            }

            ProgramLogger.LogInformation("\tDownloading book {0}", fileName);
            await uri.DownloadAsFileAsync(fileName, false);
            return fullPath;
        }
    }
}
