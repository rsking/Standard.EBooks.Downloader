// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using EBook.Downloader.Common;
using EBook.Downloader.Standard.EBooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

#pragma warning disable MA0047, SA1516

const int SentinelRetryCount = 30;

const int SentinelRetryWait = 100;

const int MaxTimeOffset = 180;

const string AtomUrl = "https://standardebooks.org/opds/all";

var maxTimeOffsetOption = new Option<int>(new[] { "-m", "--max-time-offset" }, () => MaxTimeOffset, "The maximum time offset");
var forcedSeriesOption = new Option<FileInfo?>(new[] { "-f", "--forced-series" }, "A files containing the names of sets that should be series");

var outputPathOption = new Option<DirectoryInfo>(new[] { "-o", "--output-path" }, () => new DirectoryInfo(Environment.CurrentDirectory), "The output path") { ArgumentHelpName = "PATH", Arity = ArgumentArity.ExactlyOne }.ExistingOnly();
var resyncOption = new Option<bool>(new[] { "-r", "--resync" }, "Forget the last saved state, perform a full sync");
var afterOption = new Option<DateTime?>(new[] { "-a", "--after" }, ParseArgument, isDefault: false, "The time to download books after");

var downloadCommand = new Command("download")
{
    EBook.Downloader.CommandLine.LibraryPathArgument,
    outputPathOption,
    resyncOption,
    afterOption,
};

downloadCommand.SetHandler<IHost, DirectoryInfo, DirectoryInfo, bool, bool, DateTime?, int, FileInfo?, CancellationToken>(Download, EBook.Downloader.CommandLine.LibraryPathArgument, outputPathOption, EBook.Downloader.CommandLine.UseContentServerOption, resyncOption, afterOption, maxTimeOffsetOption, forcedSeriesOption);

var metadataCommand = new Command("metadata")
{
    EBook.Downloader.CommandLine.LibraryPathArgument,
};

metadataCommand.SetHandler<IHost, System.IO.DirectoryInfo, bool, int, System.IO.FileInfo?, System.Threading.CancellationToken>(Metadata, EBook.Downloader.CommandLine.LibraryPathArgument, EBook.Downloader.CommandLine.UseContentServerOption, maxTimeOffsetOption, forcedSeriesOption);

var rootCommand = new RootCommand("Standard EBook Downloader")
{
    downloadCommand,
    metadataCommand,
};

rootCommand.AddGlobalOption(EBook.Downloader.CommandLine.UseContentServerOption);
rootCommand.AddGlobalOption(maxTimeOffsetOption);
rootCommand.AddGlobalOption(forcedSeriesOption);

var builder = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .UseHost(
        Host.CreateDefaultBuilder,
        configureHost =>
        {
            configureHost
                .UseSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.CurrentCulture, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] <{ThreadId:00}> {Message:lj}{NewLine}{Exception}");
                    loggerConfiguration
                        .WriteTo.Debug()
                        .Filter.ByExcluding(Serilog.Filters.Matching.FromSource(typeof(HttpClient).FullName ?? string.Empty))
                        .Enrich.WithThreadId();
                });
            configureHost
                .ConfigureServices((_, services) =>
                {
                    services
                        .AddHttpClient(string.Empty)
                        .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(30));
                    services
                        .AddHttpClient("header")
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None });

                    services
                        .Configure<InvocationLifetimeOptions>(options => options.SuppressStatusMessages = true);
                });
        })
    .CancelOnProcessTermination();

return await builder
    .Build()
    .InvokeAsync(args.Select(Environment.ExpandEnvironmentVariables).ToArray())
    .ConfigureAwait(false);

static DateTime? ParseArgument(ArgumentResult result)
{
    var dateString = result.Tokens[0];
    return Parse(dateString.Value);

    static DateTime? Parse(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
        {
            return null;
        }

        var parser = new ChronicNetCore.Parser();
        var parsedSpan = parser.Parse(dateString);
        if (parsedSpan is null)
        {
            return default;
        }

        return parsedSpan.Start;
    }
}

static async Task Download(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    DirectoryInfo outputPath,
    bool useContentServer = false,
    bool resync = false,
    DateTime? after = default,
    int maxTimeOffset = MaxTimeOffset,
    FileInfo? forcedSeries = default,
    CancellationToken cancellationToken = default)
{
    var programLogger = host.Services.GetRequiredService<ILogger<EpubInfo>>();
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
        switch (e.ExceptionObject)
        {
            case Exception exception:
                programLogger.LogError(exception, "{Message}", exception.Message);
                break;
            case null:
                programLogger.LogError("Unhandled Exception");
                break;
            default:
                programLogger.LogError("{Message}", e.ExceptionObject.ToString());
                break;
        }
    };

    var atom = new System.ServiceModel.Syndication.Atom10FeedFormatter();
    using (var xml = System.Xml.XmlReader.Create(AtomUrl))
    {
        atom.ReadFrom(xml);
    }

    var sentinelPath = Path.Combine(calibreLibraryPath.FullName, ".sentinel");

    if (!File.Exists(sentinelPath))
    {
        await File.WriteAllBytesAsync(sentinelPath, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(sentinelPath, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    DateTime sentinelDateTime;
    if (after is not null)
    {
        sentinelDateTime = after.Value;
    }
    else if (resync)
    {
        sentinelDateTime = DateTime.MinValue.ToUniversalTime();
    }
    else
    {
        sentinelDateTime = File.GetLastWriteTimeUtc(sentinelPath);
    }

    var sentinelLock = new object();
    var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
    var forcedSeriesList = forcedSeries?.Exists == true
        ? await File.ReadAllLinesAsync(forcedSeries.FullName, cancellationToken).ConfigureAwait(false)
        : Array.Empty<string>();

    var atomUri = new Uri(AtomUrl);
    using var calibreLibrary = new CalibreLibrary(calibreLibraryPath.FullName, useContentServer, host.Services.GetRequiredService<ILogger<CalibreLibrary>>());
    foreach (var item in atom.Feed.Items
        .Where(item => item.LastUpdatedTime > sentinelDateTime)
        .OrderBy(item => item.LastUpdatedTime)
        .AsParallel())
    {
        // get the name, etc
        var name = string.Join(" & ", item.Authors.Select(author => author.Name));
        programLogger.LogInformation("Processing book {Title} - {Name} for {Date}", item.Title.Text, name, item.LastUpdatedTime);
        using var bookScope = programLogger.BeginScope("{Title} - {Name} - {Date}", item.Title.Text, name, item.LastUpdatedTime);
        foreach (var uri in item.Links
            .Where(IsValidEBook)
            .Select(AbsoluteUri))
        {
            // get the date time
            var extension = uri.GetExtension();
            var kepub = string.Equals(extension, ".kepub", StringComparison.InvariantCultureIgnoreCase);
            var book = await calibreLibrary
                .GetBookByIdentifierAndExtensionAsync(item.Id, "url", extension, cancellationToken)
                .ConfigureAwait(false);
            var lastWriteTimeUtc = DateTime.MinValue;
            if (book is not null)
            {
                var filePath = book.GetFullPath(calibreLibrary.Path, extension);
                if (File.Exists(filePath))
                {
                    lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);

                    // Only update the description/last modified from the EPUB
                    if (!kepub)
                    {
                        // see if we should update the date time
                        await calibreLibrary.UpdateLastModifiedAsync(book, lastWriteTimeUtc, maxTimeOffset, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    // book should exist here!
                    programLogger.LogError("Failed to find {Title} - {Name} - {Extension}", item.Title.Text, name, extension.TrimStart('.'));
                }
            }
            else
            {
                programLogger.LogInformation("{Title} - {Name} - {Extension} does not exist in Calibre", item.Title.Text, name, extension.TrimStart('.'));
            }

            var actualUri = await uri.ShouldDownloadAsync(
                lastWriteTimeUtc,
                httpClientFactory,
                url =>
                {
                    var uriString = url.ToString();
                    var baseUri = uriString[..(uriString.LastIndexOf("/", StringComparison.OrdinalIgnoreCase) + 1)];

                    var split = GetFileNameWithoutExtension(uriString).Split('_');
                    var number = Math.Max(split.Length - 1, 2);
                    split = split[..number];
                    var fileName = string.Join('_', split.Take(number));
                    return new Uri(baseUri + fileName + GetExtension(uriString));

                    static string GetFileNameWithoutExtension(string path)
                    {
                        var fileName = GetFileName(path);
                        return fileName[..fileName.IndexOf('.', StringComparison.OrdinalIgnoreCase)];
                    }

                    static string GetExtension(string path)
                    {
                        var fileName = GetFileName(path);
                        return fileName[fileName.IndexOf('.', StringComparison.OrdinalIgnoreCase)..];
                    }

                    static string GetFileName(string path)
                    {
                        return path[(path.LastIndexOf('/') + 1)..];
                    }
                },
                cancellationToken).ConfigureAwait(false);

            if (actualUri is null)
            {
                continue;
            }

            // download this
            var path = await DownloadBookAsync(actualUri, outputPath.FullName, programLogger, httpClientFactory, cancellationToken).ConfigureAwait(false);
            if (path is null)
            {
                continue;
            }

            var epubInfo = EpubInfo.Parse(path, !kepub);
            if (await calibreLibrary.AddOrUpdateAsync(epubInfo, maxTimeOffset, forcedSeriesList, cancellationToken).ConfigureAwait(false))
            {
                programLogger.LogDebug("Deleting, {Title} - {Authors} - {Extension}", epubInfo.Title, string.Join("; ", epubInfo.Authors), epubInfo.Path.Extension.TrimStart('.'));
                epubInfo.Path.Delete();
            }
        }

        lock (sentinelLock)
        {
            // update the sentinel time
            var i = 0;
            while (true)
            {
                i++;
                try
                {
                    File.SetLastWriteTimeUtc(sentinelPath, item.LastUpdatedTime.UtcDateTime);
                    break;
                }
                catch (IOException) when (i != SentinelRetryCount)
                {
                }

                System.Threading.Thread.Sleep(SentinelRetryWait);
            }
        }
    }

    static bool IsValidEBook(System.ServiceModel.Syndication.SyndicationLink link)
    {
        return IsEPub() || IsKobo();

        bool IsEPub()
        {
            return string.Equals(link.MediaType, "application/epub+zip", StringComparison.Ordinal)
                && (link.Uri.OriginalString.EndsWith("epub3", StringComparison.InvariantCultureIgnoreCase) || Path.GetFileNameWithoutExtension(link.Uri.OriginalString).EndsWith("_advanced", StringComparison.InvariantCultureIgnoreCase));
        }

        bool IsKobo()
        {
            return string.Equals(link.MediaType, "application/kepub+zip", StringComparison.Ordinal);
        }
    }

    Uri AbsoluteUri(System.ServiceModel.Syndication.SyndicationLink link)
    {
        return link.Uri.IsAbsoluteUri ? link.Uri : new Uri(atomUri, link.Uri.OriginalString);
    }

    static async Task<string?> DownloadBookAsync(Uri uri, string path, Microsoft.Extensions.Logging.ILogger logger, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
    {
        // create the file name
        var fileName = uri.GetFileName();
        var fullPath = Path.GetFullPath(Path.Combine(path, GetHashedName(fileName)));
        if (!File.Exists(fullPath))
        {
            logger.LogInformation("Downloading book {FileName}", fileName);
            try
            {
                await uri.DownloadAsFileAsync(fullPath, overwrite: false, httpClientFactory, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "{Message}", ex.Message);
                return default;
            }
        }

        return fullPath;

        static string GetHashedName(string fileName)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:X}", StringComparer.Ordinal.GetHashCode(fileName)) + Path.GetExtension(fileName);
        }
    }
}

static async Task Metadata(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    bool useContentServer = false,
    int maxTimeOffset = MaxTimeOffset,
    FileInfo? forcedSeries = default,
    CancellationToken cancellationToken = default)
{
    var programLogger = host.Services.GetRequiredService<ILogger<EpubInfo>>();
    using var calibreLibrary = new CalibreLibrary(calibreLibraryPath.FullName, useContentServer, host.Services.GetRequiredService<ILogger<CalibreLibrary>>());
    var forcedSeriesList = forcedSeries?.Exists == true
        ? await File.ReadAllLinesAsync(forcedSeries.FullName, cancellationToken).ConfigureAwait(false)
        : Array.Empty<string>();

    await foreach (var book in calibreLibrary.GetBooksByPublisherAsync("Standard Ebooks", cancellationToken).ConfigureAwait(false))
    {
        programLogger.LogInformation("Processing book {Title}", book.Name);
        var filePath = book.GetFullPath(calibreLibrary.Path, ".epub");
        if (File.Exists(filePath))
        {
            await calibreLibrary.UpdateAsync(book, EpubInfo.Parse(filePath, parseDescription: true), forcedSeriesList, maxTimeOffset, cancellationToken).ConfigureAwait(false);
        }
    }
}

#pragma warning restore MA0047, SA1516