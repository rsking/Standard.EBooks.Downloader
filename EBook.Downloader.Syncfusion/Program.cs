// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using EBook.Downloader.Calibre;
using EBook.Downloader.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = new CommandLineBuilder(new RootCommand("Syncfusion EBook Updater") { Handler = CommandHandler.Create<IHost, System.IO.DirectoryInfo, bool, bool, System.Threading.CancellationToken>(Process) })
    .AddArgument(new Argument<System.IO.DirectoryInfo>("CALIBRE-LIBRARY-PATH") { Description = "The path to the directory containing the calibre library", Arity = ArgumentArity.ExactlyOne }.ExistingOnly())
    .AddOption(new Option<bool>(new[] { "-s", "--use-content-server" }, "Whether to use the content server or not"))
    .AddOption(new Option<bool>(new[] { "-c", "--cover" }, "Download covers"))
    .UseDefaults()
    .UseHost(
        Host.CreateDefaultBuilder,
        configureHost =>
        {
            configureHost
                .UseSerilog((_, loggerConfiguration) => loggerConfiguration
                    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.CurrentCulture, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] <{ThreadId:00}> {Message:lj}{NewLine}{Exception}")
                    .Filter.ByExcluding(Serilog.Filters.Matching.FromSource(typeof(System.Net.Http.HttpClient).FullName ?? string.Empty))
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
    System.IO.DirectoryInfo calibreLibraryPath,
    bool cover = false,
    bool useContentServer = false,
    System.Threading.CancellationToken cancellationToken = default)
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
        System.Uri? uri = default;
        string? isbn = default;
        if (element.TryGetProperty("identifiers", out var identifiersElement))
        {
            if (identifiersElement.TryGetProperty("uri", out var uriElement) && uriElement.GetString() is string uriString)
            {
                uri = new System.Uri(uriString);
            }

            if (identifiersElement.TryGetProperty("isbn", out var isbnElement) && isbnElement.GetString() is string isbnString)
            {
                isbn = isbnString;
            }
        }

        return (Id: id, Title: title, Uri: uri, Isbn: isbn);
    }).ToArray();

    var clientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
    foreach (var (id, title, uri, isbn) in books)
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
        System.DateTime? publishedOn = default;
        if (detailsSections is not null)
        {
            foreach (var detailsSection in detailsSections.Where(node => node.HasChildNodes))
            {
                string? header = default;
                foreach (var detail in detailsSection.ChildNodes)
                {
                    if (string.Equals(detail.Name, "h5", System.StringComparison.OrdinalIgnoreCase))
                    {
                        header = detail.InnerText;
                        continue;
                    }

                    if (string.Equals(detail.Name, "p", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(header, "isbn", System.StringComparison.OrdinalIgnoreCase))
                        {
                            actualIsbn = detail.InnerText.Replace("-", string.Empty, System.StringComparison.OrdinalIgnoreCase);
                        }
                        else if (string.Equals(header, "published on", System.StringComparison.OrdinalIgnoreCase))
                        {
                            var text = detail.InnerText.Replace("Published on: ", string.Empty, System.StringComparison.OrdinalIgnoreCase);

                            if (System.DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                            {
                                publishedOn = parsed;
                            }
                            else
                            {
                                publishedOn = System.DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
                            }
                        }

                        header = default;
                    }
                }
            }
        }

        var fields = new System.Collections.Generic.List<(StandardField Field, object? Value)>();
        string? imagePath = default;
        if (IsbnHasChanged(isbn, actualIsbn)
            || UrlHasChanged(uri, requestUri))
        {
            if (!cover)
            {
                imagePath = await GetImageAsync(client, uri, document, cancellationToken).ConfigureAwait(false);
            }

            fields.Add((StandardField.Identifiers, new Identifier("isbn", actualIsbn ?? isbn)));
            fields.Add((StandardField.Identifiers, new Identifier("uri", requestUri ?? uri)));
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
            return actualIsbn is not null && !string.Equals(isbn, actualIsbn, System.StringComparison.Ordinal);
        }

        static bool UrlHasChanged(System.Uri uri, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] System.Uri? requestUri)
        {
            return requestUri is not null && uri != requestUri;
        }

        static async ValueTask<string?> GetImageAsync(HttpClient client, System.Uri uri, HtmlAgilityPack.HtmlDocument document, System.Threading.CancellationToken cancellationToken)
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
            var readOnlineUri = new System.Uri(uri, onClick
                .Replace("location.href=", string.Empty, System.StringComparison.OrdinalIgnoreCase)
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

            var imageUri = new System.Uri(uri, src);

            var fileName = System.IO.Path.GetTempFileName();
            var fileStream = System.IO.File.OpenWrite(fileName);
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
