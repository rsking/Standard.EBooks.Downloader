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
    using System.Reactive.Linq;
    using System.Threading.Tasks;
    using EBook.Downloader.Common;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The main program class.
    /// </summary>
    internal static class Program
    {
        private const string Uri = "https://standardebooks.org/ebooks/?page={0}";

        private static readonly string FilterName = typeof(System.Net.Http.HttpClient).FullName;

        /// <summary>
        /// The main entry point.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The main application task.</returns>
        private static async Task Main(string[] args)
        {
            IServiceCollection services = new ServiceCollection();
            services
                .AddLogging(c => c.AddConsole().AddFilter((string category, LogLevel logLevel) => logLevel > LogLevel.Debug && !category.StartsWith(FilterName)))
                .AddHttpClient(string.Empty)
                .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(30))
                .Services
                .AddHttpClient("header")
                .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None });

            var serviceProvider = services.BuildServiceProvider();

            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            var programLogger = loggerFactory.CreateLogger(nameof(Program));
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                {
                    programLogger.LogError(exception, exception.Message);
                }
                else if (e.ExceptionObject != null)
                {
                    programLogger.LogError(e.ExceptionObject.ToString());
                }
            };

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.SystemDefault | System.Net.SecurityProtocolType.Tls12;
            var calibreLibraryPath = Environment.ExpandEnvironmentVariables(args[0]);
            var outputPath = args.Length > 1 ? Environment.ExpandEnvironmentVariables(args[1]) : ("." + System.IO.Path.DirectorySeparatorChar);
            var page = args.Length > 2 ? int.Parse(args[2]) : 1;
            var endPage = args.Length > 3 ? int.Parse(args[3]) : int.MaxValue;
            var httpClientFactory = serviceProvider.GetService<System.Net.Http.IHttpClientFactory>();

            using (var calibreLibrary = new CalibreLibrary(calibreLibraryPath, loggerFactory.CreateLogger<CalibreLibrary>()))
            {
                do
                {
                    var any = false;
                    foreach (var value in ProcessPage(page, programLogger, httpClientFactory).ToEnumerable())
                    {
                        foreach (var epub in ProcessBook(value, programLogger))
                        {
                            // get the date time
                            var dateTime = await calibreLibrary.GetDateTimeAsync(value, epub.GetExtension()).ConfigureAwait(false);

                            if (dateTime.HasValue && !(await epub.ShouldDownloadAsync(dateTime.Value, httpClientFactory).ConfigureAwait(false)))
                            {
                                continue;
                            }

                            // download this
                            var path = await DownloadBookAsync(epub, outputPath, programLogger, httpClientFactory).ConfigureAwait(false);

                            // parse the format this
                            if (path != null)
                            {
                                var epubInfo = EpubInfo.Parse(path);

                                if (await calibreLibrary.UpdateIfExistsAsync(epubInfo).ConfigureAwait(false))
                                {
                                    programLogger.LogInformation("\tDeleting, {0} - {1} - {2}", epubInfo.Title, string.Join("; ", epubInfo.Authors), epubInfo.Extension);
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
                }
                while (page < endPage);
            }
        }

        private static IObservable<Uri> ProcessPage(int page, ILogger logger, System.Net.Http.IHttpClientFactory httpClientFactory)
        {
            return Observable.Create<Uri>(async obs =>
            {
                logger.LogInformation("Processing page {0}", page);
                var pageUri = new Uri(string.Format(Uri, page));

                var document = new HtmlAgilityPack.HtmlDocument();
                document.LoadHtml(await pageUri.DownloadAsStringAsync(httpClientFactory).ConfigureAwait(false));

                if (document.ParseErrors?.Any() == true)
                {
                    // Handle any parse errors as required
                }

                if (document.DocumentNode != null)
                {
                    // find all the links to the books
                    var nodes = document.DocumentNode.SelectNodes("//body/main[@class='ebooks']/ol/li/p/a");
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
                        if (nodes[i].ParentNode.HasClass("author"))
                        {
                            continue;
                        }

                        var link = nodes[i].GetAttributeValue("href", string.Empty);
                        obs.OnNext(new Uri(pageUri, link));
                    }
                }
            });
        }

        private static IEnumerable<Uri> ProcessBook(Uri uri, ILogger logger)
        {
            logger.LogInformation("\tProcessing book {0}{1}", uri.Segments[2], uri.Segments[3]);
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

        private static async Task<string> DownloadBookAsync(Uri uri, string path, ILogger logger, System.Net.Http.IHttpClientFactory httpClientFactory)
        {
            // create the file name
            var fileName = uri.GetFileName();

            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(path, fileName));

            // get the last part of the URI
            if (System.IO.File.Exists(fullPath))
            {
                return fullPath;
            }

            logger.LogInformation("\tDownloading book {0}", fileName);
            await uri.DownloadAsFileAsync(fullPath, false, httpClientFactory).ConfigureAwait(false);
            return fullPath;
        }
    }
}
