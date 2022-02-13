// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using EBook.Downloader.Calibre;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var coverOption = new Option<bool>(new[] { "-c", "--cover" }, "Download covers");

var rootCommand = new RootCommand("Syncfusion EBook Updater")
{
    EBook.Downloader.CommandLine.LibraryPathArgument,
    EBook.Downloader.CommandLine.UseContentServerOption,
    coverOption,
};

rootCommand.SetHandler<IHost, DirectoryInfo, bool, bool, CancellationToken>(Process, EBook.Downloader.CommandLine.LibraryPathArgument, EBook.Downloader.CommandLine.UseContentServerOption, coverOption);

var builder = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .UseHost(
        Host.CreateDefaultBuilder,
        configureHost =>
        {
            configureHost
                .UseSerilog((_, loggerConfiguration) => loggerConfiguration
                    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.CurrentCulture, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] <{ThreadId:00}> {Message:lj}{NewLine}{Exception}")
                    .Filter.ByExcluding(Serilog.Filters.Matching.FromSource(typeof(HttpClient).FullName ?? string.Empty))
                    .Enrich.WithThreadId());
            configureHost
                .ConfigureServices((_, services) =>
                {
                    services.AddHttpClient(string.Empty);
                    services.Configure<InvocationLifetimeOptions>(options => options.SuppressStatusMessages = true);
                });
        });

return await builder
    .CancelOnProcessTermination()
    .Build()
    .InvokeAsync(args.Select(System.Environment.ExpandEnvironmentVariables).ToArray())
    .ConfigureAwait(false);

static async Task Process(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    bool cover = false,
    bool useContentServer = false,
    CancellationToken cancellationToken = default)
{
    var logger = host.Services.GetRequiredService<ILogger<CalibreDb>>();
    var calibreDb = new CalibreDb(calibreLibraryPath.FullName, useContentServer, logger);
    var list = await calibreDb.ListAsync(fields: new[] { "id", "title", "identifiers" }, searchPattern: "series:\"=Succinctly\"", cancellationToken: cancellationToken).ConfigureAwait(false);
    if (list is null)
    {
        return;
    }

    var books = list.RootElement.EnumerateArray().Select(element =>
    {
        var id = element.GetProperty("id").GetInt32();
        var title = element.GetProperty("title").GetString();
        Uri? uri = default;
        string? isbn = default;
        string? gitHub = default;
        if (element.TryGetProperty("identifiers", out var identifiersElement))
        {
            if (identifiersElement.TryGetProperty("uri", out var uriElement) && uriElement.GetString() is string uriString)
            {
                uri = new Uri(uriString);
            }

            if (identifiersElement.TryGetProperty("isbn", out var isbnElement) && isbnElement.GetString() is string isbnString)
            {
                isbn = isbnString;
            }

            if (identifiersElement.TryGetProperty("github", out var gitHubElement) && gitHubElement.GetString() is string gitHubString)
            {
                gitHub = gitHubString;
            }
        }

        return (Id: id, Title: title, Uri: uri, Isbn: isbn, GitHub: gitHub);
    }).ToArray();

    var clientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
    foreach (var (id, title, uri, isbn, gitHub) in books)
    {
        if (uri is null)
        {
            logger.LogError("{Title} does not have a URI", title);
            continue;
        }

        // download the web page
        var client = clientFactory.CreateClient();
        var response = await client
            .GetAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Could not retreive {URI} because of {StatusCode}", uri, response.StatusCode);
            continue;
        }

        var requestUri = response.RequestMessage?.RequestUri;

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var document = new HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(html);

        var detailsSections = document.DocumentNode.Descendants("div").Where(d => d.HasClass("details-section"));
        string? actualIsbn = default;
        if (detailsSections is not null)
        {
            foreach (var detailsSection in detailsSections.Where(node => node.HasChildNodes))
            {
                string? header = default;
                foreach (var detail in detailsSection.ChildNodes)
                {
                    if (string.Equals(detail.Name, "h5", StringComparison.OrdinalIgnoreCase))
                    {
                        header = detail.InnerText;
                        continue;
                    }

                    if (string.Equals(detail.Name, "p", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(header, "isbn", StringComparison.OrdinalIgnoreCase))
                        {
                            actualIsbn = detail.InnerText.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
                        }

                        header = default;
                    }
                }
            }
        }

        string? actualGithub = default;
        var sourceCodeSections = document.DocumentNode.Descendants("div").Where(d => d.HasClass("source-code"));
        if (sourceCodeSections is not null)
        {
            foreach (var sourceCodeSection in sourceCodeSections.Where(node => node.HasChildNodes))
            {
                var anchor = sourceCodeSection.ChildNodes.Single(n => string.Equals(n.Name,  "a", StringComparison.Ordinal));
                var href = anchor.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(href))
                {
                    actualGithub = href.Replace("https://github.com/", string.Empty, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        var fields = new List<(StandardField Field, object? Value)>();
        string? imagePath = default;
        if (IsbnHasChanged(isbn, actualIsbn)
            || UrlHasChanged(uri, requestUri)
            || GitHubHasChanged(gitHub, actualGithub))
        {
            if (!cover)
            {
                imagePath = await GetImageAsync(client, uri, document, cancellationToken).ConfigureAwait(false);
            }

            fields.Add((StandardField.Identifiers, new Identifier("isbn", actualIsbn ?? isbn)));
            fields.Add((StandardField.Identifiers, new Identifier("uri", requestUri ?? uri)));
            if (actualGithub is not null)
            {
                fields.Add((StandardField.Identifiers, new Identifier("github", actualGithub ?? gitHub)));
            }
        }

        if (cover)
        {
            imagePath = await GetImageAsync(client, uri, document, cancellationToken).ConfigureAwait(false);
        }

        if (imagePath is not null)
        {
            fields.Add((StandardField.Cover, imagePath));
        }

        if (fields.Count > 0)
        {
            await calibreDb
                .SetMetadataAsync(id, fields.ToLookup(field => field.Field, field => field.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        static bool IsbnHasChanged(string isbn, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? actualIsbn)
        {
            return actualIsbn is not null && !string.Equals(isbn, actualIsbn, StringComparison.Ordinal);
        }

        static bool UrlHasChanged(Uri uri, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Uri? requestUri)
        {
            return requestUri is not null && uri != requestUri;
        }

        static bool GitHubHasChanged(string github, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? actualGitHub)
        {
            return !string.Equals(github, actualGitHub, StringComparison.Ordinal);
        }

        static async ValueTask<string?> GetImageAsync(HttpClient client, Uri uri, HtmlAgilityPack.HtmlDocument document, CancellationToken cancellationToken)
        {
            // get the read online link
            var readOnlineButton = document.DocumentNode
                .Descendants("button")
                .FirstOrDefault(d => d.HasClass("eBook_View"));
            if (readOnlineButton is null)
            {
                return default;
            }

            var onClick = readOnlineButton.GetAttributeValue("onclick", default(string));
            if (onClick is null)
            {
                return default;
            }

            // parse this out
            var readOnlineUri = new Uri(uri, onClick
                .Replace("location.href=", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim('\''));

            var html = await client
                .GetStringAsync(readOnlineUri, cancellationToken)
                .ConfigureAwait(false);

            document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(html);

            var imageNode = document.DocumentNode
                .Descendants("img")
                .FirstOrDefault(d => d.HasClass("img-responsive"));

            if (imageNode is null)
            {
                return default;
            }

            var src = imageNode.GetAttributeValue("src", def: null);
            if (src is null)
            {
                return default;
            }

            var imageUri = new Uri(uri, src);

            var fileName = Path.GetRandomFileName();
            var fileStream = File.OpenWrite(fileName);
            await using (fileStream.ConfigureAwait(false))
            {
                var stream = await client
                    .GetStreamAsync(imageUri, cancellationToken)
                    .ConfigureAwait(false);

                await using (stream.ConfigureAwait(false))
                {
                    await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }
            }

            return fileName;
        }
    }
}