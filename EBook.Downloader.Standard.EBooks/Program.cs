// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Standard.EBooks
{
    using System;
    using System.Collections.Generic;
    using System.CommandLine;
    using System.CommandLine.Builder;
    using System.CommandLine.Invocation;
    using System.Linq;
    using System.Threading.Tasks;
    using EBook.Downloader.Common;
    using Humanizer;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Serilog;

    /// <summary>
    /// The main program class.
    /// </summary>
    internal class Program
    {
        private const string Uri = "https://standardebooks.org/ebooks/?page={0}";

        private static readonly string DefaultOutputPath = "." + System.IO.Path.DirectorySeparatorChar;

        private static readonly string FilterName = typeof(System.Net.Http.HttpClient).FullName;

        /// <summary>
        /// The main entry point.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The main application task.</returns>
        private static Task Main(string[] args)
        {
            var rootCommand = new RootCommand("Standard EBook Downloder");
            rootCommand.AddArgument(new Argument<System.IO.DirectoryInfo>("CALIBRE-LIBRARY-PATH") { Description = "The path to the directory containing the calibre library", Arity = ArgumentArity.ExactlyOne });
            rootCommand.AddOption(new Option(new[] { "--output-path", "-o" }, "The output path") { Argument = new Argument<System.IO.DirectoryInfo>("PATH", new System.IO.DirectoryInfo(DefaultOutputPath)) { Arity = ArgumentArity.ExactlyOne } });
            rootCommand.AddOption(new Option(new[] { "--start-page", "-s" }, "The start page") { Argument = new Argument<int>("PAGE") { Arity = ArgumentArity.ExactlyOne } });
            rootCommand.AddOption(new Option(new[] { "--end-page", "-e" }, "The end page") { Argument = new Argument<int>("PAGE") { Arity = ArgumentArity.ExactlyOne } });

            rootCommand.Handler = CommandHandler.Create<System.IO.DirectoryInfo, System.IO.DirectoryInfo, int, int>(Process);

            if (args != null)
            {
                for (var i = 0; i < args.Length; i++)
                {
                    args[i] = Environment.ExpandEnvironmentVariables(args[i]);
                }
            }

            return rootCommand.InvokeAsync(args);
        }

        private static async Task Process(System.IO.DirectoryInfo calibreLibraryPath, System.IO.DirectoryInfo outputPath, int startPage = 1, int endPage = int.MaxValue)
        {
            var host = new Microsoft.Extensions.Hosting.HostBuilder()
                .UseSerilog((hostingContext, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.CurrentCulture)
                        .Filter.ByExcluding(log => (log.Level < Serilog.Events.LogEventLevel.Debug)
                            || (log.Properties["SourceContext"] is Serilog.Events.ScalarValue scalarValue
                            && scalarValue.Value is string stringValue
                            && stringValue.StartsWith(FilterName, StringComparison.Ordinal)));
                })
                .ConfigureServices((_, services) =>
                {
                    services.AddHttpClient(string.Empty)
                        .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(30));
                    services.AddHttpClient("header")
                        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None });
                })
                .Build();

            var programLogger = host.Services.GetRequiredService<ILogger<Program>>();
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
            var httpClientFactory = host.Services.GetRequiredService<System.Net.Http.IHttpClientFactory>();

            using var calibreLibrary = new CalibreLibrary(calibreLibraryPath.FullName, host.Services.GetRequiredService<ILogger<CalibreLibrary>>());
            do
            {
                var any = false;
                programLogger.LogDebug("Processing page {Page}", startPage);
                using var pageScope = programLogger.BeginScope(startPage);
                await foreach (var value in ProcessPage(startPage, httpClientFactory))
                {
                    var names = value.Segments[2].Trim('/').Split("_").Select(name => name.Replace("-", " ", StringComparison.OrdinalIgnoreCase).Transform(To.TitleCase, ToName.Instance));
                    var name = string.Join(" & ", names);
                    var title = value.Segments[3].Trim('/').Replace("-", " ", StringComparison.OrdinalIgnoreCase).Transform(To.TitleCase);
                    programLogger.LogInformation("Processing book {Name} - {Title}", name, title);
                    using var bookScope = programLogger.BeginScope("{Name} - {Title}", name, title);
                    foreach (var epub in ProcessBook(value))
                    {
                        // get the date time
                        var dateTime = await calibreLibrary.GetDateTimeAsync(value, epub.GetExtension()).ConfigureAwait(false);

                        if (dateTime.HasValue && !(await epub.ShouldDownloadAsync(dateTime.Value, httpClientFactory).ConfigureAwait(false)))
                        {
                            continue;
                        }

                        // download this
                        var path = await DownloadBookAsync(epub, outputPath.FullName, programLogger, httpClientFactory).ConfigureAwait(false);

                        // parse the format this
                        if (path != null)
                        {
                            var epubInfo = EpubInfo.Parse(path);

                            if (await calibreLibrary.UpdateIfExistsAsync(epubInfo, dateTime.HasValue).ConfigureAwait(false))
                            {
                                programLogger.LogDebug("Deleting, {0} - {1} - {2}", epubInfo.Title, string.Join("; ", epubInfo.Authors), epubInfo.Extension);
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

                startPage++;
            }
            while (startPage <= endPage);
        }

        private static async IAsyncEnumerable<Uri> ProcessPage(int page, System.Net.Http.IHttpClientFactory httpClientFactory)
        {
            var pageUri = new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, Uri, page));

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
                    yield break;
                }

                int count;
                try
                {
                    count = nodes.Count;
                }
                catch (NullReferenceException)
                {
                    yield break;
                }

                for (var i = 0; i < count; i++)
                {
                    // get the html attribute
                    if (nodes[i].ParentNode.HasClass("author"))
                    {
                        continue;
                    }

                    var link = nodes[i].GetAttributeValue("href", string.Empty);
                    yield return new Uri(pageUri, link);
                }
            }
        }

        private static IEnumerable<Uri> ProcessBook(Uri uri)
        {
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
                    if (bookUri.Segments.Last().EndsWith("epub3", StringComparison.Ordinal))
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

        private static async Task<string> DownloadBookAsync(Uri uri, string path, Microsoft.Extensions.Logging.ILogger logger, System.Net.Http.IHttpClientFactory httpClientFactory)
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

        private class ToName : IStringTransformer
        {
            public static readonly IStringTransformer Instance = new ToName();

            public string Transform(string input)
            {
                var result = input;
                var matches = System.Text.RegularExpressions.Regex.Matches(input, @"(\w|[^\u0000-\u007F])+'?\w*");
                var offset = 0;
                foreach (System.Text.RegularExpressions.Match word in matches)
                {
                    if (word.Length == 1)
                    {
                        result = AddPeriod(word, result, offset);
                        offset += word.Length;
                    }
                }

                return result;
            }

            private static string AddPeriod(System.Text.RegularExpressions.Match word, string source, int offset) => source.Substring(0, offset + word.Index + word.Length) + "." + source.Substring(offset + word.Index + word.Length);
        }
    }
}
