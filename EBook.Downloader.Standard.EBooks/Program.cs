// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Standard.EBooks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive;
    using System.Reactive.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using EBook.Downloader.Common;
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
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is System.Exception exception)
                {
                    ProgramLogger.LogError(exception, exception.Message);
                }
                else if (e.ExceptionObject != null)
                {
                    ProgramLogger.LogError(e.ExceptionObject.ToString());
                }
            };

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.SystemDefault | System.Net.SecurityProtocolType.Tls12;
            var calibreLibraryPath = System.Environment.ExpandEnvironmentVariables(args[0]);
            var outputPath = args.Length > 1 ? System.Environment.ExpandEnvironmentVariables(args[1]) : ("." + System.IO.Path.DirectorySeparatorChar);
            var page = args.Length > 2 ? int.Parse(args[2]) : 1;
            var endPage = args.Length > 3 ? int.Parse(args[3]) : int.MaxValue;

            using (var calibreLibrary = new CalibreLibrary(calibreLibraryPath, LoggerFactory.CreateLogger<CalibreLibrary>()))
            {
                while (true)
                {
                    var any = false;
                    foreach (var value in ProcessPage(page).ToEnumerable())
                    {
                        foreach (var epub in ProcessBook(value))
                        {
                            // get the date time
                            var dateTime = await calibreLibrary.GetDateTime(value, epub.GetExtension());

                            if (dateTime.HasValue && !(await epub.ShouldDownload(dateTime.Value)))
                            {
                                continue;
                            }

                            // download this
                            var path = await DownloadBook(epub, outputPath);

                            // parse the format this
                            if (path != null)
                            {
                                var epubInfo = EpubInfo.Parse(path);

                                if (await calibreLibrary.UpdateIfExistsAsync(epubInfo))
                                {
                                    ProgramLogger.LogInformation("\tDeleting, {0} - {1} - {2}", epubInfo.Title, string.Join("; ", epubInfo.Authors), epubInfo.Extension);
                                    System.IO.File.Delete(epubInfo.Path);
                                }
                            }
                        }

                        any = true;
                    }

                    if (!any)
                    {
                        break;
                    }

                    page++;
                    if (page >= endPage)
                    {
                        break;
                    }
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
                    var bookUri = new Uri(uri, link);
                    if (bookUri.Segments.Last().EndsWith("epub3"))
                    {
                        yield return bookUri;
                    }
                }

                nodes = document.DocumentNode.SelectNodes("//body/main/article[@class='ebook']/section[@id='download']/ul/li/p/span/a[@class='kobo']");
                foreach (var node in nodes)
                {
                    var link = node.GetAttributeValue("href", string.Empty);
                    yield return new Uri(uri, link);
                }
            }
        }

        private static async Task<string> DownloadBook(Uri uri, string path)
        {
            // create the file name
            var fileName = uri.GetFileName();

            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(path, fileName));

            // get the last part of the URI
            if (System.IO.File.Exists(fullPath))
            {
                return fullPath;
            }

            ProgramLogger.LogInformation("\tDownloading book {0}", fileName);
            await uri.DownloadAsFileAsync(fullPath, false);
            return fullPath;
        }
    }
}
