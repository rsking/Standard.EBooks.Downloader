// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using AngleSharp;
using EBook.Downloader.Common;
using EBook.Downloader.Standard.EBooks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeoSmart.Caching.Sqlite;
using Serilog;

const int SentinelRetryCount = 30;

const int SentinelRetryWait = 100;

const int MaxTimeOffset = 180;

#pragma warning disable S1075 // URIs should not be hardcoded
const string AtomUrl = "https://standardebooks.org/feeds/atom/new-releases";
#pragma warning restore S1075 // URIs should not be hardcoded

var maxTimeOffsetOption = new Option<int>(new[] { "-m", "--max-time-offset" }, () => MaxTimeOffset, "The maximum time offset");
var forcedSeriesOption = new Option<FileInfo?>(new[] { "-f", "--forced-series" }, "A files containing the names of sets that should be series");
var forcedSetsOption = new Option<FileInfo?>(new[] { "-s", "--forced-sets" }, "A files containing the names of sets that should be sets");

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

downloadCommand.SetHandler(context =>
{
    var host = context.BindingContext.GetRequiredService<IHost>();
    var libraryPath = context.ParseResult.GetValueForArgument(EBook.Downloader.CommandLine.LibraryPathArgument);
    var outputPath = context.ParseResult.GetValueForOption(outputPathOption)!;
    var useContentServer = context.ParseResult.GetValueForOption(EBook.Downloader.CommandLine.UseContentServerOption);
    var resync = context.ParseResult.GetValueForOption(resyncOption);
    var after = context.ParseResult.GetValueForOption(afterOption);
    var maxTimeOffset = context.ParseResult.GetValueForOption(maxTimeOffsetOption);
    var forcedSeries = context.ParseResult.GetValueForOption(forcedSeriesOption);
    var forcesSets = context.ParseResult.GetValueForOption(forcedSetsOption);
    var cancellationToken = context.GetCancellationToken();
    return Download(host, libraryPath, outputPath, useContentServer, resync, after, maxTimeOffset, forcedSeries, forcesSets, cancellationToken);
});

var metadataCommand = new Command("metadata")
{
    EBook.Downloader.CommandLine.LibraryPathArgument,
};

metadataCommand.SetHandler(
    Metadata,
    EBook.Downloader.Bind.FromServiceProvider<IHost>(),
    EBook.Downloader.CommandLine.LibraryPathArgument,
    EBook.Downloader.CommandLine.UseContentServerOption,
    maxTimeOffsetOption,
    forcedSeriesOption,
    forcedSetsOption,
    EBook.Downloader.Bind.FromServiceProvider<CancellationToken>());

var updateCommand = new Command("update")
{
    EBook.Downloader.CommandLine.LibraryPathArgument,
    outputPathOption,
};

updateCommand.SetHandler(
    Update,
    EBook.Downloader.Bind.FromServiceProvider<IHost>(),
    EBook.Downloader.CommandLine.LibraryPathArgument,
    outputPathOption,
    EBook.Downloader.CommandLine.UseContentServerOption,
    maxTimeOffsetOption,
    forcedSeriesOption,
    forcedSetsOption,
    EBook.Downloader.Bind.FromServiceProvider<CancellationToken>());

var rootCommand = new RootCommand("Standard EBook Downloader")
{
    downloadCommand,
    metadataCommand,
    updateCommand,
};

rootCommand.AddGlobalOption(EBook.Downloader.CommandLine.UseContentServerOption);
rootCommand.AddGlobalOption(maxTimeOffsetOption);
rootCommand.AddGlobalOption(forcedSeriesOption);
rootCommand.AddGlobalOption(forcedSetsOption);

var builder = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .UseHost(
        Host.CreateDefaultBuilder,
        configureHost =>
        {
            _ = configureHost
                .UseSerilog((__, loggerConfiguration) =>
                {
                    _ = loggerConfiguration
                        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] <{ThreadId:00}> {Message:lj}{NewLine}{Exception}", formatProvider: System.Globalization.CultureInfo.CurrentCulture);
                    _ = loggerConfiguration
                        .WriteTo.Debug()
                        .Filter.ByExcluding(Serilog.Filters.Matching.FromSource(typeof(HttpClient).FullName ?? string.Empty))
                        .Enrich.WithThreadId();
                });
            _ = configureHost
                .ConfigureServices((__, services) =>
                {
                    _ = services
                        .AddSqliteCache(options => options.CachePath = Path.Combine(Path.GetTempPath(), "standard.ebook.cache.sqlite"));
                    _ = services
                        .AddHttpClient(string.Empty)
                        .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(30));
                    _ = services
                        .AddHttpClient("direct")
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                        .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(30));
                    _ = services
                        .AddHttpClient("header")
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None });

                    _ = services
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
        return parser.Parse(dateString)?.Start;
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
    FileInfo? forcedSets = default,
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

    var sentinelPath = Path.Combine(calibreLibraryPath.FullName, ".sentinel");

    if (!File.Exists(sentinelPath))
    {
        await File.WriteAllBytesAsync(sentinelPath, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
        File.SetLastWriteTimeUtc(sentinelPath, DateTime.UnixEpoch);
    }

    var sentinelDateTime = (after, resync) switch
    {
        (not null, _) => after.Value,
        (_, true) => DateTime.MinValue.ToUniversalTime(),
        _ => File.GetLastWriteTimeUtc(sentinelPath),
    };
    var sentinelLock = new object();
    var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
    var forcedSeriesEnumerable = GetRegexFromFile(forcedSeries);
    var forcedSetsEnumerable = GetRegexFromFile(forcedSets);

    var calibreDb = new EBook.Downloader.Calibre.CalibreDb(calibreLibraryPath.FullName, useContentServer, host.Services.GetRequiredService<ILogger<EBook.Downloader.Calibre.CalibreDb>>());
    var categories = await calibreDb.ShowCategoriesAsync(cancellationToken).ToArrayAsync(cancellationToken).ConfigureAwait(false);
    var series = categories.Where(category => category.CategoryType == EBook.Downloader.Calibre.CategoryType.Series).Select(category => category.TagName).ToArray();
    var sets = categories.Where(category => category.CategoryType == EBook.Downloader.Calibre.CategoryType.Sets).Select(category => category.TagName).ToArray();

    var atomUri = new Uri(AtomUrl);
    var atom = await GetAtomAsync(host.Services.GetRequiredService<IDistributedCache>(), httpClientFactory, atomUri, cancellationToken).ConfigureAwait(false);
    using var calibreLibrary = new CalibreLibrary(calibreDb, host.Services.GetRequiredService<ILogger<CalibreLibrary>>());
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

            var lastWriteTimeUtc = await GetLastWriteTimeFromItem(
                programLogger,
                item,
                calibreLibrary,
                name,
                extension,
                maxTimeOffset,
                cancellationToken).ConfigureAwait(false);

            await DownloadIfRequired(
                programLogger,
                calibreLibrary,
                httpClientFactory,
                uri,
                lastWriteTimeUtc,
                outputPath,
                extension,
                forcedSeriesEnumerable,
                forcedSetsEnumerable,
                series,
                sets,
                maxTimeOffset,
                cancellationToken).ConfigureAwait(false);
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
                    // this is below our retry count
                }

                Thread.Sleep(SentinelRetryWait);
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

    static async Task<System.ServiceModel.Syndication.Atom10FeedFormatter> GetAtomAsync(IDistributedCache cache, IHttpClientFactory httpClientFactory, Uri uri, CancellationToken cancellationToken)
    {
        var bytes = await cache.GetAsync("atom", cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            var client = httpClientFactory.CreateClient();
            bytes = await client.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);
            await cache.SetAsync("atom", bytes, new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1) }, cancellationToken).ConfigureAwait(false);
        }

        var atom = new System.ServiceModel.Syndication.Atom10FeedFormatter();
        using (var stream = new MemoryStream(bytes))
        {
            using var xml = System.Xml.XmlReader.Create(stream);
            atom.ReadFrom(xml);
        }

        return atom;
    }

    static async Task<DateTime> GetLastWriteTimeFromItem(
        Microsoft.Extensions.Logging.ILogger programLogger,
        System.ServiceModel.Syndication.SyndicationItem item,
        CalibreLibrary calibreLibrary,
        string name,
        string extension,
        int maxTimeOffset,
        CancellationToken cancellationToken)
    {
        var book = await calibreLibrary
            .GetBookByIdentifierAndExtensionAsync(item.Id, "url", extension, cancellationToken)
            .ConfigureAwait(false);

        DateTime lastWriteTimeUtc = default;
        if (book is not null)
        {
            lastWriteTimeUtc = await GetLastWriteTime(calibreLibrary, book, extension, maxTimeOffset, cancellationToken).ConfigureAwait(false);

            if (lastWriteTimeUtc == default)
            {
                // book should exist here!
                programLogger.LogError("Failed to find {Title} - {Name} - {Extension}", item.Title.Text, name, extension.TrimStart('.'));
            }
        }
        else
        {
            programLogger.LogInformation("{Title} - {Name} - {Extension} does not exist in Calibre", item.Title.Text, name, extension.TrimStart('.'));
        }

        return lastWriteTimeUtc;
    }
}

static async Task Metadata(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    bool useContentServer = false,
    int maxTimeOffset = MaxTimeOffset,
    FileInfo? forcedSeries = default,
    FileInfo? forcedSets = default,
    CancellationToken cancellationToken = default)
{
    var programLogger = host.Services.GetRequiredService<ILogger<EpubInfo>>();
    var calibreDb = new EBook.Downloader.Calibre.CalibreDb(calibreLibraryPath.FullName, useContentServer, host.Services.GetRequiredService<ILogger<EBook.Downloader.Calibre.CalibreDb>>());
    using var calibreLibrary = new CalibreLibrary(calibreDb, host.Services.GetRequiredService<ILogger<CalibreLibrary>>());
    var forcedSeriesEnumerable = GetRegexFromFile(forcedSeries);
    var forcedSetsEnumerable = GetRegexFromFile(forcedSets);

    // get the current sets and series
    var categories = await calibreDb.ShowCategoriesAsync(cancellationToken).ToArrayAsync(cancellationToken).ConfigureAwait(false);
    var sets = categories.Where(category => category.CategoryType == EBook.Downloader.Calibre.CategoryType.Sets).Select(category => category.TagName).ToArray();

    await foreach (var book in calibreLibrary.GetBooksByPublisherAsync("Standard Ebooks", cancellationToken).ConfigureAwait(false))
    {
        programLogger.LogInformation("Processing book {Title}", book.Name);
        var filePath = book.GetFullPath(calibreLibrary.Path, ".epub");
        if (File.Exists(filePath))
        {
            var epub = EpubInfo.Parse(filePath, parseDescription: true);
            epub = await UpdateEpubInfoAsync(epub, calibreLibrary, forcedSeriesEnumerable, forcedSetsEnumerable, sets, programLogger, cancellationToken).ConfigureAwait(false);
            await calibreLibrary.UpdateAsync(book, epub, maxTimeOffset, cancellationToken).ConfigureAwait(false);
        }
    }
}

static async Task<EpubInfo> UpdateEpubInfoAsync(
    EpubInfo epub,
    CalibreLibrary calibreLibrary,
    IEnumerable<System.Text.RegularExpressions.Regex> forcedSeries,
    IEnumerable<System.Text.RegularExpressions.Regex> forcedSets,
    IEnumerable<string> sets,
    Microsoft.Extensions.Logging.ILogger logger,
    CancellationToken cancellationToken)
{
    System.Xml.XmlElement? longDescription = default;
    if (epub.LongDescription is not null)
    {
        longDescription = await UpdateLongDescriptionAsync(epub.LongDescription, calibreLibrary, sets, logger, cancellationToken).ConfigureAwait(false);
    }

    // update the sets and series
    var collections = epub.Collections.Select(collection => collection switch
    {
        { Type: not EpubCollectionType.Series } c when c.IsSeries(forcedSeries, forcedSets) => c with { Type = EpubCollectionType.Series },
        { Type: not EpubCollectionType.Set } c when c.IsSet(forcedSeries, forcedSets) => c with { Type = EpubCollectionType.Set },
        _ => collection,
    });

    return epub with { LongDescription = longDescription, Collections = collections };

    async static Task<System.Xml.XmlElement> UpdateLongDescriptionAsync(System.Xml.XmlElement longDescription, CalibreLibrary calibreLibrary, IEnumerable<string> sets, Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken)
    {
        var bookRegex = new System.Text.RegularExpressions.Regex("https://standardebooks.org/ebooks/(?<author>[-a-z]+)/(?<book>[-a-z]+)", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        var authorRegex = new System.Text.RegularExpressions.Regex("https://standardebooks.org/ebooks/(?<author>[-a-z]+)", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        var collectionsRegex = new System.Text.RegularExpressions.Regex("https://standardebooks.org/collections/(?<collection>[-a-z]+)", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        // update the description with internal links
        var document = await Parser.ParseDocumentAsync(longDescription.OuterXml, cancellationToken).ConfigureAwait(false);

        var updated = false;
        foreach (var anchor in document.GetElementsByTagName("a").OfType<AngleSharp.Html.Dom.IHtmlAnchorElement>())
        {
            if (anchor.Href is string uri)
            {
                if (bookRegex.IsMatch(uri))
                {
                    logger.LogDebug("Looking up link to {Uri}", uri);
                    if (await calibreLibrary.GetCalibreBookAsync(new EBook.Downloader.Calibre.Identifier("url", uri), cancellationToken).ConfigureAwait(false) is CalibreBook book)
                    {
                        anchor.Href = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"calibre://show-book/_/{book.Id}");
                        updated = true;
                    }
                }
                else if (authorRegex.IsMatch(uri))
                {
                    logger.LogDebug("Found author URI");

                    // set this to seach for the author
                    var match = authorRegex.Match(uri);
                    var author = match.Groups["author"];
                    var search = $"author:\"={author.Value.Replace('-', ' ')}\"";
                    anchor.Href = string.Concat("calibre://search/_?eq=", ConvertStringToHex(search, System.Text.Encoding.UTF8));
                    updated = true;
                }
                else if (collectionsRegex.IsMatch(uri))
                {
                    logger.LogDebug("Found collections URI");

                    var match = collectionsRegex.Match(uri);
                    var collection = match.Groups["collection"];

                    var value = collection.Value.Replace('-', ' ');
                    var type = sets.Contains(value, StringComparer.OrdinalIgnoreCase)
                        ? "sets"
                        : "series";

                    var search = $"{type}:\"={value}\"";
                    anchor.Href = string.Concat("calibre://search/_?eq=", ConvertStringToHex(search, System.Text.Encoding.UTF8));
                    updated = true;
                }
                else
                {
                    logger.LogWarning("Unknown URI format: {Uri} - {Anchor}", uri, anchor.OuterHtml);
                }

                static string ConvertStringToHex(string input, System.Text.Encoding encoding)
                {
                    var stringBytes = encoding.GetBytes(input);
                    var characters = new char[stringBytes.Length * 2];
                    foreach (var (hex, i) in stringBytes.Select((b, i) => (Hex: b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture), i)))
                    {
                        var index = i * 2;
                        characters[index] = hex[0];
                        characters[index + 1] = hex[1];
                    }

                    return new string(characters);
                }
            }
        }

        if (updated && document.Body?.FirstChild?.ToHtml() is string html)
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(html);
            return doc.DocumentElement!;
        }

        return longDescription;
    }
}

static async Task Update(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    DirectoryInfo outputPath,
    bool useContentServer = false,
    int maxTimeOffset = MaxTimeOffset,
    FileInfo? forcedSeries = default,
    FileInfo? forcedSets = default,
    CancellationToken cancellationToken = default)
{
    var programLogger = host.Services.GetRequiredService<ILogger<EpubInfo>>();
    var calibreDb = new EBook.Downloader.Calibre.CalibreDb(calibreLibraryPath.FullName, useContentServer, host.Services.GetRequiredService<ILogger<EBook.Downloader.Calibre.CalibreDb>>());
    using var calibreLibrary = new CalibreLibrary(calibreDb, host.Services.GetRequiredService<ILogger<CalibreLibrary>>());
    var forcedSeriesEnumerable = GetRegexFromFile(forcedSeries);
    var forcedSetsEnumerable = GetRegexFromFile(forcedSets);
    var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
    var parser = new AngleSharp.Html.Parser.HtmlParser();

    var categories = await calibreDb.ShowCategoriesAsync(cancellationToken).ToArrayAsync(cancellationToken).ConfigureAwait(false);
    var sets = categories.Where(category => category.CategoryType == EBook.Downloader.Calibre.CategoryType.Sets).Select(category => category.TagName).ToArray();
    var series = categories.Where(category => category.CategoryType == EBook.Downloader.Calibre.CategoryType.Series).Select(category => category.TagName).ToArray();

    // get all the books from standard e-books
    await foreach (var book in calibreLibrary.GetBooksByPublisherAsync("Standard Ebooks", cancellationToken).ConfigureAwait(false))
    {
        if (book.Identifiers.TryGetValue("url", out var uriValue) && uriValue is Uri uri)
        {
            await foreach (var bookUri in GetBookUris(programLogger, parser, uri, httpClientFactory, cancellationToken).ConfigureAwait(false))
            {
                if (!IsValidEpub(bookUri))
                {
                    continue;
                }

                // get the extension
                var extension = GetExtension(bookUri);
                var last = await GetLastWriteTime(calibreLibrary, book, extension, maxTimeOffset, cancellationToken).ConfigureAwait(false);
                await DownloadIfRequired(programLogger, calibreLibrary, httpClientFactory, bookUri, last, outputPath, extension, forcedSeriesEnumerable, forcedSetsEnumerable, series, sets,  maxTimeOffset, cancellationToken).ConfigureAwait(false);
            }
        }

        static bool IsValidEpub(Uri uri)
        {
            return uri.PathAndQuery.EndsWith("_advanced.epub", StringComparison.OrdinalIgnoreCase) || uri.PathAndQuery.EndsWith(".kepub.epub", StringComparison.OrdinalIgnoreCase);
        }

        static string GetExtension(Uri uri)
        {
            return uri.PathAndQuery.EndsWith(".kepub.epub", StringComparison.OrdinalIgnoreCase) ? ".kepub" : ".epub";
        }
    }

    static async IAsyncEnumerable<Uri> GetBookUris(
        Microsoft.Extensions.Logging.ILogger logger,
        AngleSharp.Html.Parser.HtmlParser parser,
        Uri pageUri,
        IHttpClientFactory httpClientFactory,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();

        AngleSharp.Html.Dom.IHtmlDocument document;

        try
        {
            using var response = await client.GetAsync(pageUri, cancellationToken).ConfigureAwait(false);
            _ = response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                document = await parser.ParseDocumentAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download/parse {Uri}", pageUri);
            yield break;
        }

        foreach (var attributes in document
            .GetElementsByClassName("epub")
            .Concat(document.GetElementsByClassName("kobo"))
            .Select(element => element.Attributes))
        {
            if (attributes.GetNamedItem("property") is AngleSharp.Dom.IAttr propertyAttribute
                && propertyAttribute.Value is string propertyAttributeValue
                && string.Equals(propertyAttributeValue, "schema:contentUrl", StringComparison.Ordinal)
                && attributes["href"] is AngleSharp.Dom.IAttr hrefAttribute
                && Uri.TryCreate(hrefAttribute.Value, UriKind.RelativeOrAbsolute, out var uri))
            {
                yield return uri.IsAbsoluteUri
                    ? uri
                    : new Uri(pageUri, uri);
            }
        }
    }
}

static async Task DownloadIfRequired(
    Microsoft.Extensions.Logging.ILogger programLogger,
    CalibreLibrary calibreLibrary,
    IHttpClientFactory httpClientFactory,
    Uri uri,
    DateTime lastWriteTimeUtc,
    DirectoryInfo outputPath,
    string extension,
    IEnumerable<System.Text.RegularExpressions.Regex> forcedSeries,
    IEnumerable<System.Text.RegularExpressions.Regex> forcedSets,
    IEnumerable<string> series,
    IEnumerable<string> sets,
    int maxTimeOffset,
    CancellationToken cancellationToken)
{
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
        return;
    }

    // download this
    var path = await DownloadBookAsync(actualUri, outputPath.FullName, programLogger, httpClientFactory, cancellationToken).ConfigureAwait(false);
    if (path is null)
    {
        return;
    }

    var epubInfo = await UpdateEpubInfoAsync(EpubInfo.Parse(path, !IsKePub(extension)), calibreLibrary, forcedSeries, forcedSets, sets, programLogger, cancellationToken).ConfigureAwait(false);
    if (await calibreLibrary.AddOrUpdateAsync(epubInfo, maxTimeOffset, cancellationToken).ConfigureAwait(false))
    {
        programLogger.LogDebug("Deleting, {Title} - {Authors} - {Extension}", epubInfo.Title, string.Join("; ", epubInfo.Authors), epubInfo.Path.Extension.TrimStart('.'));
        epubInfo.Path.Delete();
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

static IEnumerable<System.Text.RegularExpressions.Regex> GetRegexFromFile(FileInfo? input)
{
    return input?.Exists == true
        ? ReadFromFile(input.FullName)
        : Array.Empty<System.Text.RegularExpressions.Regex>();

    IEnumerable<System.Text.RegularExpressions.Regex> ReadFromFile(string path)
    {
        return File
            .ReadLines(path)
            .Select(line => new System.Text.RegularExpressions.Regex(line, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            .ToArray();
    }
}

static async Task<DateTime> GetLastWriteTime(
    CalibreLibrary calibreLibrary,
    CalibreBook book,
    string extension,
    int maxTimeOffset,
    CancellationToken cancellationToken)
{
    DateTime lastWriteTimeUtc = default;
    var filePath = book.GetFullPath(calibreLibrary.Path, extension);
    if (File.Exists(filePath))
    {
        lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);

        // Only update the description/last modified from the EPUB
        if (!IsKePub(extension))
        {
            // see if we should update the date time
            await calibreLibrary.UpdateLastModifiedAsync(book, lastWriteTimeUtc, maxTimeOffset, cancellationToken).ConfigureAwait(false);
        }
    }

    return lastWriteTimeUtc;
}

static bool IsKePub(string extension)
{
    return string.Equals(extension, ".kepub", StringComparison.InvariantCultureIgnoreCase);
}

/// <content>
/// Members for the program.
/// </content>
internal sealed partial class Program
{
    private static readonly AngleSharp.Html.Parser.HtmlParser Parser = new();

    private Program()
    {
    }
}