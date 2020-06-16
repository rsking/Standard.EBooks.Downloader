// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Standard.EBooks
{
    using System;
    using System.CommandLine;
    using System.CommandLine.Builder;
    using System.CommandLine.Hosting;
    using System.CommandLine.Invocation;
    using System.CommandLine.Parsing;
    using System.Linq;
    using System.Threading.Tasks;
    using EBook.Downloader.Common;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Serilog;

    /// <summary>
    /// The main program class.
    /// </summary>
    internal class Program
    {
        private const int SentinelRetryCount = 30;

        private const int SentinelRetryWait = 100;

        private const int MaxTimeOffset = 180;

        private static readonly Uri Uri = new UriBuilder("https://standardebooks.org/opds/all").Uri;

        /// <summary>
        /// The main entry point.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The main application task.</returns>
        private static Task Main(string[] args)
        {
            var builder = new CommandLineBuilder(new RootCommand("Standard EBook Downloder") { Handler = CommandHandler.Create<IHost, System.IO.DirectoryInfo, System.IO.DirectoryInfo, bool, bool, int>(Process) })
                .AddArgument(new Argument<System.IO.DirectoryInfo>("CALIBRE-LIBRARY-PATH") { Description = "The path to the directory containing the calibre library", Arity = ArgumentArity.ExactlyOne }.ExistingOnly())
                .AddOption(new Option<System.IO.DirectoryInfo>(new[] { "-o", "--output-path" }, "The output path") { Argument = new Argument<System.IO.DirectoryInfo>("PATH", () => new System.IO.DirectoryInfo(Environment.CurrentDirectory)) { Arity = ArgumentArity.ExactlyOne } }.ExistingOnly())
                .AddOption(new Option<bool>(new[] { "-c", "--check-description" }, "Whether to check the description"))
                .AddOption(new Option<bool>(new[] { "-r", "--resync" }, "Forget the last saved state, perform a full sync"))
                .AddOption(new Option<int>(new[] { "-m", "--max-time-offset" }, () => MaxTimeOffset, "The maximum time offset"))
                .UseHost(
                    Host.CreateDefaultBuilder,
                    configureHost =>
                    {
                        configureHost
                            .UseSerilog((_, loggerConfiguration) => loggerConfiguration
                                .WriteTo
                                .Console(formatProvider: System.Globalization.CultureInfo.CurrentCulture, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] <{ThreadId:00}> {Message:lj}{NewLine}{Exception}")
                                .Filter.ByExcluding(Serilog.Filters.Matching.FromSource(typeof(System.Net.Http.HttpClient).FullName ?? string.Empty))
                                .Enrich.WithThreadId())
                            .ConfigureServices((_, services) => services
                                .AddHttpClient(string.Empty)
                                .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(30))
                                .Services
                                .AddHttpClient("header")
                                .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None }));
                    });

            return builder.Build().InvokeAsync(args.Select(arg => Environment.ExpandEnvironmentVariables(arg)).ToArray());
        }

        private static async Task Process(
            IHost host,
            System.IO.DirectoryInfo calibreLibraryPath,
            System.IO.DirectoryInfo outputPath,
            bool checkDescription = false,
            bool resync = false,
            int maxTimeOffset = MaxTimeOffset)
        {
            var programLogger = host.Services.GetRequiredService<ILogger<Program>>();
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                switch (e.ExceptionObject)
                {
                    case Exception exception:
                        programLogger.LogError(exception, exception.Message);
                        break;
                    case null:
                        programLogger.LogError("Unhandled Exception");
                        break;
                    default:
                        programLogger.LogError(e.ExceptionObject.ToString());
                        break;
                }
            };

            var atom = new System.ServiceModel.Syndication.Atom10FeedFormatter();
            using (var xml = System.Xml.XmlReader.Create(Uri.ToString()))
            {
                atom.ReadFrom(xml);
            }

            var sentinelPath = System.IO.Path.Combine(calibreLibraryPath.FullName, ".sentinel");

            if (!System.IO.File.Exists(sentinelPath))
            {
                System.IO.File.WriteAllBytes(sentinelPath, Array.Empty<byte>());
                System.IO.File.SetLastWriteTimeUtc(sentinelPath, DateTime.MinValue.ToUniversalTime());
            }

            var sentinelDateTime = resync
                ? DateTime.MinValue.ToUniversalTime()
                : System.IO.File.GetLastWriteTimeUtc(sentinelPath);

            var sentinelLock = new object();
            var httpClientFactory = host.Services.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            using var calibreLibrary = new CalibreLibrary(calibreLibraryPath.FullName, host.Services.GetRequiredService<ILogger<CalibreLibrary>>());
            foreach (var item in atom.Feed.Items
                .OrderBy(item => item.LastUpdatedTime)
                .Where(item => item.LastUpdatedTime > sentinelDateTime)
                .AsParallel())
            {
                // get the name, etc
                var name = string.Join(" & ", item.Authors.Select(author => author.Name));
                programLogger.LogInformation("Processing book {Title} - {Name}", item.Title.Text, name);
                using var bookScope = programLogger.BeginScope("{Title} - {Name}", item.Title.Text, name);
                foreach (var uri in item.Links
                    .Where(link => (link.MediaType == "application/epub+zip" && link.Uri.OriginalString.EndsWith("epub3", System.StringComparison.InvariantCultureIgnoreCase)) || link.MediaType == "application/kepub+zip")
                    .Select(link => link.Uri.IsAbsoluteUri ? link.Uri : new Uri(Uri, link.Uri.OriginalString)))
                {
                    // get the date time
                    var extension = uri.GetExtension();
                    var kepub = string.Equals(extension, ".kepub", StringComparison.InvariantCultureIgnoreCase);
                    var book = await calibreLibrary.GetBookByIdentifierAndExtensionAsync(item.Id, "url", extension).ConfigureAwait(false);
                    if (book.Id != default)
                    {
                        // see if we should update the date time
                        var filePath = book.GetFullPath(calibreLibrary.Path, extension);

                        if (System.IO.File.Exists(filePath))
                        {
                            var longDescription = checkDescription && !kepub
                                ? EpubInfo.Parse(filePath, true).LongDescription
                                : default;

                            var lastWriteTimeUtc = System.IO.File.GetLastWriteTimeUtc(filePath);
                            await calibreLibrary.UpdateLastModifiedAndDescriptionAsync(book, lastWriteTimeUtc, longDescription, maxTimeOffset).ConfigureAwait(false);
                            var shouldDownload = await uri.ShouldDownloadAsync(lastWriteTimeUtc, httpClientFactory).ConfigureAwait(false);
                            if (!shouldDownload.HasValue)
                            {
                                programLogger.LogError("Invalid response from {uri}", uri);
                                continue;
                            }
                            else if (!shouldDownload.Value)
                            {
                                continue;
                            }
                        }
                    }
                    else
                    {
                        programLogger.LogInformation("{Title} - {Name} does not exist in Calibre", item.Title.Text, name);
                    }

                    // download this
                    var path = await DownloadBookAsync(uri, outputPath.FullName, programLogger, httpClientFactory).ConfigureAwait(false);
                    if (path is null)
                    {
                        continue;
                    }

                    var epubInfo = EpubInfo.Parse(path, !kepub);
                    if (await calibreLibrary.AddOrUpdateAsync(epubInfo, maxTimeOffset).ConfigureAwait(false))
                    {
                        programLogger.LogDebug("Deleting, {0} - {1} - {2}", epubInfo.Title, string.Join("; ", epubInfo.Authors), epubInfo.Extension);
                        System.IO.File.Delete(epubInfo.Path);
                    }
                }

                lock (sentinelLock)
                {
                    // update the sentinel time
                    for (var i = 1; i <= SentinelRetryCount; i++)
                    {
                        try
                        {
                            System.IO.File.SetLastWriteTimeUtc(sentinelPath, item.LastUpdatedTime.UtcDateTime);
                            break;
                        }
                        catch (System.IO.IOException) when (i != SentinelRetryCount)
                        {
                        }

                        System.Threading.Thread.Sleep(SentinelRetryWait);
                    }
                }
            }
        }

        private static async Task<string?> DownloadBookAsync(Uri uri, string path, Microsoft.Extensions.Logging.ILogger logger, System.Net.Http.IHttpClientFactory httpClientFactory)
        {
            // create the file name
            var fileName = uri.GetFileName();
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(path, fileName));
            if (!System.IO.File.Exists(fullPath))
            {
                logger.LogInformation("Downloading book {0}", fileName);
                try
                {
                    await uri.DownloadAsFileAsync(fullPath, false, httpClientFactory).ConfigureAwait(false);
                }
                catch (System.Net.Http.HttpRequestException ex)
                {
                    logger.LogError(ex, ex.Message);
                    return default;
                }
            }

            return fullPath;
        }
    }
}
