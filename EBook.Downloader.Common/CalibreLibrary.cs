// -----------------------------------------------------------------------
// <copyright file="CalibreLibrary.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common;

using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

/// <summary>
/// Represents a <see href="https://calibre-ebook.com/">calibre</see> library.
/// </summary>
public class CalibreLibrary : IDisposable
{
    private const string TriggerName = "books_update_trg";

    private const string UpdateLastModifiedById = "UPDATE books SET last_modified = :lastModified WHERE id = :id";

    private const string SelectLastModifiedById = "SELECT last_modified FROM books WHERE id = :id";

    private static readonly AngleSharp.Html.Parser.HtmlParser Parser = new();

    private readonly ILogger logger;

    private readonly Calibre.CalibreDb calibreDb;

    private readonly Microsoft.Data.Sqlite.SqliteCommand updateLastModifiedCommand;

    private readonly Microsoft.Data.Sqlite.SqliteCommand selectLastModifiedCommand;

    private readonly Microsoft.Data.Sqlite.SqliteCommand? dropTriggerCommand;

    private readonly Microsoft.Data.Sqlite.SqliteCommand? createTriggerCommand;

    private readonly Microsoft.Data.Sqlite.SqliteConnection connection;

    private bool disposedValue; // To detect redundant calls

    /// <summary>
    /// Initialises a new instance of the <see cref="CalibreLibrary" /> class.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="useContentServer">Set to <see langword="true" /> to use the content server.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="calibrePath">The path to the calibre binaries.</param>
    public CalibreLibrary(string path, bool useContentServer, ILogger logger, string calibrePath = Calibre.CalibreDb.DefaultCalibrePath)
    {
        this.Path = path;
        this.logger = logger;
        this.calibreDb = new Calibre.CalibreDb(path, useContentServer, logger, calibrePath);

        var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(path, "metadata.db"), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite };
        this.connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionStringBuilder.ConnectionString);
        this.connection.Open();

        this.updateLastModifiedCommand = this.connection.CreateCommand();
        this.updateLastModifiedCommand.CommandText = UpdateLastModifiedById;
        _ = this.updateLastModifiedCommand.Parameters.Add(":lastModified", Microsoft.Data.Sqlite.SqliteType.Text);
        _ = this.updateLastModifiedCommand.Parameters.Add(":id", Microsoft.Data.Sqlite.SqliteType.Integer);

        this.selectLastModifiedCommand = this.connection.CreateCommand();
        this.selectLastModifiedCommand.CommandText = SelectLastModifiedById;
        _ = this.selectLastModifiedCommand.Parameters.Add(":id", Microsoft.Data.Sqlite.SqliteType.Integer);

        var createTriggerCommandText = default(string);
        using (var command = this.connection.CreateCommand())
        {
            command.CommandText = $"SELECT sql FROM sqlite_master WHERE type = 'trigger' AND name = '{TriggerName}'";
            createTriggerCommandText = command.ExecuteScalar() as string;
        }

        if (!string.IsNullOrEmpty(createTriggerCommandText))
        {
            this.dropTriggerCommand = this.connection.CreateCommand();
            this.dropTriggerCommand.CommandText = $"DROP TRIGGER IF EXISTS {TriggerName}";
            this.createTriggerCommand = this.connection.CreateCommand();
            this.createTriggerCommand.CommandText = createTriggerCommandText;
        }
    }

    /// <summary>
    /// Gets the path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the book for a specified identifier and extension.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <param name="type">The type of identifier.</param>
    /// <param name="extension">The extension to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The book.</returns>
    public Task<CalibreBook?> GetBookByIdentifierAndExtensionAsync(string identifier, string type, string extension, CancellationToken cancellationToken = default)
    {
        if (identifier is null)
        {
            return Task.FromResult<CalibreBook?>(default);
        }

        // get the date time of the format
        return this.GetCalibreBookAsync(new Calibre.Identifier(type, identifier), extension?.TrimStart('.') ?? "EPUB", cancellationToken);
    }

    /// <summary>
    /// Gets the books by publisher.
    /// </summary>
    /// <param name="publisher">The publisher.</param>
    /// <param name="cancellationToken">The cancellation tokens.</param>
    /// <returns>The books.</returns>
    public async IAsyncEnumerable<CalibreBook> GetBooksByPublisherAsync(string publisher, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var searchPattern = $"publisher:\"{publisher}\"";
        var books = await this.calibreDb.ListAsync(new[] { "id", "authors", "title", "last_modified", "identifiers", "publisher" }, searchPattern: searchPattern, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var book in GetCalibreBooks(books, element => element.TryGetProperty("publisher", out var publisherElement) && string.Equals(publisherElement.GetString(), publisher, StringComparison.Ordinal)))
        {
            yield return book;
        }
    }

    /// <summary>
    /// Adds a new book, or updates the book if it exists in the calibre library.
    /// </summary>
    /// <param name="info">The EPUB info.</param>
    /// <param name="maxTimeOffset">The maximum time offset.</param>
    /// <param name="forcedSeries">The forced series.</param>
    /// <param name="forcedSets">The forced sets.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the EPUB has been added/updated; otherwise <see langword="false" />.</returns>
    public Task<bool> AddOrUpdateAsync(
        EpubInfo info,
        int maxTimeOffset,
        IEnumerable<System.Text.RegularExpressions.Regex>? forcedSeries = default,
        IEnumerable<System.Text.RegularExpressions.Regex>? forcedSets = default,
        CancellationToken cancellationToken = default)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        System.Diagnostics.Contracts.Contract.EndContractBlock();

        return AddOrUpdateInternalAsync(info, maxTimeOffset);

        async Task<bool> AddOrUpdateInternalAsync(EpubInfo info, int maxTimeOffset)
        {
            var identifier = info.Identifiers.First();
            var book = await this.GetCalibreBookAsync(new Calibre.Identifier(identifier.Key, identifier.Value), info.Path.Extension.TrimStart('.'), cancellationToken).ConfigureAwait(false);

            if (book is null)
            {
                // see if we need to add the book or add the format
                book = await this.GetCalibreBookAsync(new Calibre.Identifier(identifier.Key, identifier.Value), cancellationToken).ConfigureAwait(false);

                if (book is null)
                {
                    book = await AddBookAsync(info).ConfigureAwait(false);
                }
                else if (info.Path is not null)
                {
                    // add this format
                    await this.calibreDb.AddFormatAsync(book.Id, info.Path, dontReplace: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (book?.Path is not null && book.Name is not null)
                {
                    UpdateLastWriteTime(book.Path, book.Name, info);
                    await UpdateMetadata(book, forcedSeries ?? Enumerable.Empty<System.Text.RegularExpressions.Regex>(), forcedSets ?? Enumerable.Empty<System.Text.RegularExpressions.Regex>()).ConfigureAwait(false);
                }

                return true;
            }

            var fullPath = book.GetFullPath(this.Path, info.Path.Extension);
            if (File.Exists(fullPath))
            {
                // see if this has changed at all
                if (!CheckFiles(info.Path, fullPath, this.logger))
                {
                    // files are not the same. Copy in the new file
                    this.logger.LogInformation("Replacing {Name} as files do not match", book.Name);

                    // access the destination file first
                    var bytes = new byte[ushort.MaxValue];
                    using (var stream = File.OpenRead(fullPath))
                    {
                        while (await stream.ReadAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false) == bytes.Length)
                        {
                            // just loop through
                        }
                    }

                    _ = info.Path.CopyTo(fullPath, overwrite: true);
                }

                await UpdateMetadata(book, forcedSeries ?? Enumerable.Empty<System.Text.RegularExpressions.Regex>(), forcedSets ?? Enumerable.Empty<System.Text.RegularExpressions.Regex>()).ConfigureAwait(false);

                return true;
            }

            return false;

            async Task<CalibreBook?> AddBookAsync(EpubInfo info)
            {
                // extract out the cover
                var coverFile = default(string);
                using (var zipFile = System.IO.Compression.ZipFile.OpenRead(info.Path.FullName))
                {
                    var coverEntry = zipFile.Entries.FirstOrDefault(entry => string.Equals(entry.Name, "cover.svg", StringComparison.Ordinal));
                    if (coverEntry is not null)
                    {
                        coverFile = System.IO.Path.GetRandomFileName();
                        using var zipStream = coverEntry.Open();
                        using var fileStream = File.OpenWrite(coverFile);
                        await zipStream.CopyToAsync(fileStream).ConfigureAwait(false);
                    }
                }

                // sanitise the tags
                var tags = info.Tags.Any()
                    ? string.Join(",", SanitiseTags(info.Tags))
                    : default;

                // we need to add this
                var bookId = await this.calibreDb.AddAsync(info.Path, duplicates: true, tags: tags, cover: coverFile, languages: "eng", cancellationToken: cancellationToken).ConfigureAwait(false);
                if (coverFile != default && File.Exists(coverFile))
                {
                    File.Delete(coverFile);
                }

                return bookId == -1
                    ? default
                    : await this.GetCalibreBookAsync(bookId, cancellationToken).ConfigureAwait(false);
            }

            static bool CheckFiles(FileInfo source, string destination, ILogger logger)
            {
                var destinationFileInfo = new FileInfo(destination);
                if (source.LastWriteTime != destinationFileInfo.LastWriteTime)
                {
                    logger.LogDebug("source and destination have different modified dates");
                    return false;
                }

                if (source.Length != destinationFileInfo.Length)
                {
                    logger.LogDebug("source and destination are different lengths");
                    return false;
                }

                var sourceHash = GetFileHash(source.FullName);
                var destinationHash = GetFileHash(destinationFileInfo.FullName);

                if (sourceHash.Length != destinationHash.Length)
                {
                    logger.LogDebug("source and destination hashes are different lengths");
                    return false;
                }

                for (var i = 0; i < sourceHash.Length; i++)
                {
                    if (sourceHash[i] != destinationHash[i])
                    {
                        logger.LogDebug("source and destination hashes do not match");
                        return false;
                    }
                }

                return true;

                static byte[] GetFileHash(string fileName)
                {
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    using var stream = File.OpenRead(fileName);
                    return sha.ComputeHash(stream);
                }
            }

            async Task UpdateMetadata(CalibreBook book, IEnumerable<System.Text.RegularExpressions.Regex> forcedSeries, IEnumerable<System.Text.RegularExpressions.Regex> forcedSets)
            {
                await this.UpdateAsync(book, info, forcedSeries, forcedSets, maxTimeOffset, cancellationToken).ConfigureAwait(false);
            }

            void UpdateLastWriteTime(string path, string name, EpubInfo info)
            {
                var fullPath = System.IO.Path.Combine(this.Path, path, $"{name}{info.Path.Extension}");
                if (File.Exists(fullPath))
                {
                    _ = new FileInfo(fullPath) { LastWriteTime = info.Path.LastWriteTime };
                }
            }
        }
    }

    /// <summary>
    /// Updates the information from the EPUB.
    /// </summary>
    /// <param name="book">The calibre book.</param>
    /// <param name="epub">The EPUB info.</param>
    /// <param name="forcedSeries">The collections that should be forced to a series.</param>
    /// <param name="forcedSets">The collections that should be forced to a set.</param>
    /// <param name="maxTimeOffset">The maximum time offset.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task associated with this function.</returns>
    public Task UpdateAsync(CalibreBook book, EpubInfo epub, IEnumerable<System.Text.RegularExpressions.Regex> forcedSeries, IEnumerable<System.Text.RegularExpressions.Regex> forcedSets, int maxTimeOffset, CancellationToken cancellationToken = default)
    {
        if (book is null)
        {
            throw new ArgumentNullException(nameof(book));
        }

        System.Diagnostics.Contracts.Contract.EndContractBlock();

        return UpdateInternalAsync(book, epub, forcedSeries, forcedSets, maxTimeOffset, cancellationToken);

        async Task UpdateInternalAsync(CalibreBook book, EpubInfo epub, IEnumerable<System.Text.RegularExpressions.Regex> forcedSeries, IEnumerable<System.Text.RegularExpressions.Regex> forcedSets, int maxTimeOffset, CancellationToken cancellationToken = default)
        {
            var series = epub.Collections
                .FirstOrDefault(collection => collection.IsSeries(forcedSeries, forcedSets));
            var sets = epub.Collections
                .Where(collection => collection.IsSet(forcedSeries, forcedSets))
                .Select(collection => collection.Name);

            (var currentLongDescription, var currentSeriesName, var currentSeriesIndex, var currentSets, var currentTags) = await this.GetCurrentAsync(book.Id, cancellationToken).ConfigureAwait(false);

            var longDescription = epub.LongDescription;
            var seriesName = series?.Name;
            var seriesIndex = series?.Position ?? 0;

            var fields = (await this.UpdateDescriptionAsync(longDescription, currentLongDescription, cancellationToken).ConfigureAwait(false))
                .Concat(this.UpdateSeries(seriesName, seriesIndex, currentSeriesName, currentSeriesIndex))
                .Concat(this.UpdateSets(sets, currentSets))
                .Concat(this.UpdateTags(SanitiseTags(epub.Tags), currentTags));
            if (await this.SetMetadataAsync(book.Id, fields, cancellationToken).ConfigureAwait(false)

                // refresh the book with the last data
                && await this.GetCalibreBookAsync(book.Id, cancellationToken).ConfigureAwait(false) is CalibreBook processed)
            {
                book = processed;
            }

            await this.UpdateLastModifiedAsync(book, epub.Path.LastWriteTimeUtc, maxTimeOffset, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Updates the last modified date, description, and series.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="lastModified">The last modified date.</param>
    /// <param name="maxTimeOffset">The maximum time offset.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task associated with this function.</returns>
    public Task UpdateLastModifiedAsync(CalibreBook book, DateTime lastModified, int maxTimeOffset, CancellationToken cancellationToken = default)
    {
        return book is null ? throw new ArgumentNullException(nameof(book)) : UpdateLastModifiedAsyncCore();

        async Task UpdateLastModifiedAsyncCore()
        {
            var bookLastModified = await this.GetDateTimeFromDatabaseAsync(book.Id, cancellationToken).ConfigureAwait(false);
            await this.UpdateLastModifiedAsync(
                book.Id,
                book.Name,
                lastModified,
                bookLastModified,
                maxTimeOffset,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) below.
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the unmanaged resource, and optionally managed resources, for this instance.
    /// </summary>
    /// <param name="disposing">Set to <see landword="true"/> to dispose managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposedValue)
        {
            if (disposing)
            {
                this.updateLastModifiedCommand?.Dispose();
                this.selectLastModifiedCommand?.Dispose();
                this.dropTriggerCommand?.Dispose();
                this.createTriggerCommand?.Dispose();
                this.connection?.Dispose();
            }

            this.disposedValue = true;
        }
    }

    private static IEnumerable<CalibreBook> GetCalibreBooks(System.Text.Json.JsonDocument? document, Func<System.Text.Json.JsonElement, bool> predicate) => document is null || document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array
            ? Enumerable.Empty<CalibreBook>()
            : document
          .RootElement
          .EnumerateArray()
          .Where(predicate)
          .Select(GetCalibreBook);

    private static CalibreBook GetCalibreBook(System.Text.Json.JsonElement json) => new(json);

    private static CalibreBook? GetCalibreBook(System.Text.Json.JsonDocument? document, Func<System.Text.Json.JsonElement, bool> predicate) => GetCalibreBooks(document, predicate).SingleOrDefault();

    private static bool CheckIdentifier(System.Text.Json.JsonElement element, Calibre.Identifier identifier) => element.GetProperty("identifiers").GetProperty(identifier.Name).ToString()?.Equals(identifier.Value.ToString(), StringComparison.Ordinal) == true;

    private static string? SanitiseHtml(string? html) => html is null ? null : Parser.ParseDocument(html).Body?.FirstChild?.Minify();

    private static string? GetCurrentLongDescription(System.Text.Json.JsonElement json) => SanitiseHtml(json.GetProperty("comments").GetString());

    private static (string? Name, float Index) GetCurrentSeries(System.Text.Json.JsonElement json)
    {
        return (GetSeries(json), json.GetProperty("series_index").GetSingle());

        static string? GetSeries(System.Text.Json.JsonElement json)
        {
            return json.TryGetProperty("series", out var seriesProperty)
                ? seriesProperty.GetString()
                : default;
        }
    }

    private static string? GetCurrentSets(System.Text.Json.JsonElement json)
    {
        var sets = GetSets(json);
        return string.Join(",", sets);

        static IEnumerable<string> GetSets(System.Text.Json.JsonElement json)
        {
            if (json.TryGetProperty("*sets", out var setsProperty))
            {
                if (setsProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var element in setsProperty.EnumerateArray())
                    {
                        if (element.GetString() is string set)
                        {
                            yield return set;
                        }
                    }
                }
                else
                {
                    if (setsProperty.GetString() is string set)
                    {
                        yield return set;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetCurrentTags(System.Text.Json.JsonElement json)
    {
        if (json.TryGetProperty("tags", out var tagsProperty))
        {
            if (tagsProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in tagsProperty.EnumerateArray())
                {
                    if (element.GetString() is string tag)
                    {
                        yield return tag;
                    }
                }
            }
            else
            {
                if (tagsProperty.GetString() is string tag)
                {
                    yield return tag;
                }
            }
        }
    }

    private static IEnumerable<string> SanitiseTags(IEnumerable<string> tags)
    {
        return tags.SelectMany(SplitByDashes)
            .Select(tag => tag.Trim())
            .Select(CaseCorrectly)
            .Select(ReplaceCommas)
            .Distinct(StringComparer.Ordinal);

        static IEnumerable<string> SplitByDashes(string value)
        {
            return value.Split(new string[] { "--" }, StringSplitOptions.RemoveEmptyEntries);
        }

        static string ReplaceCommas(string tag)
        {
            return tag.Replace(',', '‚');
        }

        static string CaseCorrectly(string value)
        {
            var (name, character) = Split(value);

            return Join(ToProperCase(name)!.Replace(" and ", " & "), ToProperCase(character));

            static (string Name, string? Character) Split(string value)
            {
                return value.IndexOf('(') switch
                {
                    -1 => (value, default(string)),
                    (int index) => (value.Substring(0, index - 1), value.Substring(index)),
                };
            }

            static string Join(string name, string? character)
            {
                return character is null ? name : $"{name} {character}";
            }

            static string? ToProperCase(string? strX)
            {
                return strX is null ? null : ToProperCaseImpl(strX, ' ', " ", System.Globalization.CultureInfo.CurrentCulture.TextInfo);

                static string ToProperCaseImpl(string strX, char separator, string join, System.Globalization.TextInfo textInfo)
                {
                    var words = new List<string>();
                    var split = strX.Trim().Split(separator);
                    const int first = 0;
                    var last = split.Length - 1;

                    foreach (var (word, index) in split.Select((w, i) => (word: w.Trim(), index: i)))
                    {
                        if (index != first
                            && index != last
                            && Words.LowerCase.Contains(word, StringComparer.OrdinalIgnoreCase))
                        {
                            words.Add(textInfo.ToLower(word));
                            continue;
                        }

                        if (word.Contains('-'))
                        {
                            words.Add(ToProperCaseImpl(word, '-', "-", textInfo));
                        }
                        else
                        {
                            words.Add(ProcessWord(word, textInfo));
                        }
                    }

                    return string.Join(join, words);

                    static string ProcessWord(string word, System.Globalization.TextInfo textInfo)
                    {
                        var count = 0;
                        var currentWord = new System.Text.StringBuilder();
                        foreach (var letter in word)
                        {
                            _ = count == 0 ? currentWord.Append(textInfo.ToUpper(letter)) : currentWord.Append(letter);

                            count++;
                        }

                        return currentWord.ToString();
                    }
                }
            }
        }
    }

    private async Task<DateTime> GetDateTimeFromDatabaseAsync(int id, CancellationToken cancellationToken)
    {
        this.selectLastModifiedCommand.Parameters[":id"].Value = id;
        var dateTimeObject = await this.selectLastModifiedCommand
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return dateTimeObject is string dataTimeString
            ? DateTime.Parse(dataTimeString, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)
            : throw new InvalidOperationException("Failed to get last modified time");
    }

    private Task<IEnumerable<FieldToUpdate>> UpdateDescriptionAsync(System.Xml.XmlElement? longDescription, string? currentLongDescription, CancellationToken cancellationToken) => longDescription is null
        ? Task.FromResult(Enumerable.Empty<FieldToUpdate>())
        : this.UpdateDescriptionAsync(SanitiseHtml(longDescription.OuterXml), currentLongDescription, cancellationToken);

    private async Task<IEnumerable<FieldToUpdate>> UpdateDescriptionAsync(string? longDescription, string? currentLongDescription, CancellationToken cancellationToken)
    {
        if (longDescription is not null)
        {
            var bookRegex = new System.Text.RegularExpressions.Regex("https://standardebooks.org/ebooks/(?<author>[-a-z]+)/(?<book>[-a-z]+)", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
            var authorRegex = new System.Text.RegularExpressions.Regex("https://standardebooks.org/ebooks/(?<author>[-a-z]+)", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
            var collectionsRegex = new System.Text.RegularExpressions.Regex("https://standardebooks.org/collections/(?<collection>[-a-z]+)", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

            // update the description with internal links
            var document = await Parser.ParseDocumentAsync(longDescription, cancellationToken).ConfigureAwait(false);

            foreach (var anchor in document.GetElementsByTagName("a").OfType<AngleSharp.Html.Dom.IHtmlAnchorElement>())
            {
                if (anchor.Href is string uri)
                {
                    if (bookRegex.IsMatch(uri))
                    {
                        this.logger.LogDebug("Looking up link to {Uri}", uri);
                        if (await this.GetCalibreBookAsync(new Calibre.Identifier("url", uri), cancellationToken).ConfigureAwait(false) is CalibreBook book)
                        {
                            anchor.Href = FormattableString.Invariant($"calibre://show-book/_/{book.Id}");
                        }
                    }
                    else if (authorRegex.IsMatch(uri))
                    {
                        this.logger.LogDebug("Found author URI");
                    }
                    else if (collectionsRegex.IsMatch(uri))
                    {
                        this.logger.LogDebug("Found collections URI");
                    }
                    else
                    {
                        this.logger.LogWarning("Unknown URI format: {Uri} - {Anchor}", uri, anchor.OuterHtml);
                    }
                }
            }

            longDescription = document.Minify();
        }

        return DoUpdateDescription(longDescription, currentLongDescription);

        IEnumerable<FieldToUpdate> DoUpdateDescription(string? longDescription, string? currentLongDescription)
        {
            if (string.Equals(currentLongDescription, longDescription, StringComparison.Ordinal))
            {
                yield break;
            }

            // execute calibredb to update the description
            this.logger.LogInformation("Updating description to the long description");
            yield return new FieldToUpdate("comments", longDescription);
        }
    }

    private IEnumerable<FieldToUpdate> UpdateSeries(string? name, float index, string? currentName, float currentIndex)
    {
        if (string.Equals(currentName, name, StringComparison.Ordinal) && (name is null || currentIndex == index))
        {
            // neither have a series, or the indexes match in the same series.
            yield break;
        }

        if (!string.Equals(currentName, name, StringComparison.Ordinal) && name is null)
        {
            // execute calibredb to clear the series
            this.logger.LogInformation("Clearing series");
            yield return new FieldToUpdate("series", name);
            yield return new FieldToUpdate("series_index", 0);
        }
        else if (!string.Equals(currentName, name, StringComparison.Ordinal) && currentIndex != index)
        {
            // execute calibredb to update the series index
            this.logger.LogInformation("Updating series and index to {Series}:{SeriesIndex}", name, index);
            yield return new FieldToUpdate("series", name);
            yield return new FieldToUpdate("series_index", index);
        }
        else if (!string.Equals(currentName, name, StringComparison.Ordinal) && currentIndex == index)
        {
            // execute calibredb to update the series
            this.logger.LogInformation("Updating series to {Series}:{SeriesIndex}", name, index);
            yield return new FieldToUpdate("series", name);
        }
        else if (string.Equals(currentName, name, StringComparison.Ordinal) && currentIndex != index)
        {
            // execute calibredb to update the series index
            this.logger.LogInformation("Updating series index to {Series}:{SeriesIndex}", name, index);
            yield return new FieldToUpdate("series_index", index);
        }
    }

    private IEnumerable<FieldToUpdate> UpdateSets(IEnumerable<string> sets, string? currentSets)
    {
        return UpdateSetsInternal(string.Join(",", sets.OrderBy(_ => _)), currentSets);

        IEnumerable<FieldToUpdate> UpdateSetsInternal(string? sets, string? currentSets)
        {
            if (string.Equals(currentSets, sets, StringComparison.Ordinal))
            {
                // neither have a bookshelf.
                yield break;
            }

            if (string.IsNullOrEmpty(sets))
            {
                this.logger.LogInformation("Clearing sets");
            }
            else
            {
                this.logger.LogInformation("Updating sets to {Sets}", sets);
            }

            yield return new FieldToUpdate("#sets", sets);
        }
    }

    private IEnumerable<FieldToUpdate> UpdateTags(IEnumerable<string> tags, IEnumerable<string> currentTags)
    {
        return UpdatetTagsInternal(tags, currentTags);

        IEnumerable<FieldToUpdate> UpdatetTagsInternal(IEnumerable<string> tags, IEnumerable<string> currentTags)
        {
            var tagsString = string.Join(",", tags.OrderBy(_ => _));
            var currentTagsString = string.Join(",", currentTags.OrderBy(_ => _));

            if (string.Equals(currentTagsString, tagsString, StringComparison.Ordinal))
            {
                // neither have tags.
                yield break;
            }

            if (string.IsNullOrEmpty(tagsString))
            {
                this.logger.LogInformation("Clearing tags");
            }
            else
            {
                this.logger.LogInformation("Updating tags to {Tags}", tagsString);
            }

            yield return new FieldToUpdate("tags", tagsString);
        }
    }

    private async Task<(string? LongDescription, string? SeriesName, float SeriesIndex, string? Sets, IEnumerable<string> Tags)> GetCurrentAsync(int id, CancellationToken cancellationToken)
    {
        var document = await this.calibreDb
            .ListAsync(new[] { "comments", "tags", "series", "series_index", "*sets" }, searchPattern: FormattableString.Invariant($"id:\"={id}\""), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return default;
        }

        var json = document.RootElement.EnumerateArray().First();

        var longDescription = GetCurrentLongDescription(json);
        (var seriesName, var seriesIndex) = GetCurrentSeries(json);
        var sets = GetCurrentSets(json);
        var tags = GetCurrentTags(json);
        return (longDescription, seriesName, seriesIndex, sets, tags);
    }

    private async Task<bool> SetMetadataAsync(int id, IEnumerable<FieldToUpdate> fields, CancellationToken cancellationToken = default)
    {
        var fieldsArray = fields.ToArray();

        if (fieldsArray.Length > 0)
        {
            await this.calibreDb.SetMetadataAsync(id, fieldsArray.ToLookup(_ => _.Field, _ => _.Value, StringComparer.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task UpdateLastModifiedAsync(int id, string name, DateTime fileLastModified, DateTime lastModified, int maxTimeOffset, CancellationToken cancellationToken = default)
    {
        if (fileLastModified == lastModified)
        {
            return;
        }

        // check this as date time, to be within the same five minutes, and is the latest date/time
        var difference = fileLastModified - lastModified;
        if (Math.Abs(difference.TotalMinutes) > maxTimeOffset || difference.TotalMinutes > 0)
        {
            // write this to the database
            this.logger.LogInformation("Updating last modified time for {Name} in the database from {Previous} to {New}", name, lastModified.ToUniversalTime(), fileLastModified);
            this.updateLastModifiedCommand.Parameters[":lastModified"].Value = fileLastModified.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz", System.Globalization.CultureInfo.InvariantCulture);
            this.updateLastModifiedCommand.Parameters[":id"].Value = id;

            if (this.dropTriggerCommand is not null)
            {
                _ = await this.dropTriggerCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _ = await this.updateLastModifiedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (this.createTriggerCommand is not null)
            {
                _ = await this.createTriggerCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task<CalibreBook?> GetCalibreBookAsync(int id, CancellationToken cancellationToken) => this.GetCalibreBookAsync(FormattableString.Invariant($"id:\"={id}\""), _ => true, cancellationToken);

    private Task<CalibreBook?> GetCalibreBookAsync(Calibre.Identifier identifier, CancellationToken cancellationToken) => this.GetCalibreBookAsync($"identifier:\"={identifier}\"", element => CheckIdentifier(element, identifier), cancellationToken);

    private Task<CalibreBook?> GetCalibreBookAsync(Calibre.Identifier identifier, string format, CancellationToken cancellationToken) => this.GetCalibreBookAsync($"identifier:\"={identifier}\" and formats:\"{format}\"", element => CheckIdentifier(element, identifier), cancellationToken);

    private async Task<CalibreBook?> GetCalibreBookAsync(string searchPattern, Func<System.Text.Json.JsonElement, bool> predicate, CancellationToken cancellationToken) => GetCalibreBook(await this.calibreDb.ListAsync(new[] { "id", "authors", "title", "last_modified", "identifiers" }, searchPattern: searchPattern, cancellationToken: cancellationToken).ConfigureAwait(false), predicate);

    private sealed record class FieldToUpdate(string Field, object? Value);

    private static class Words
    {
        public static readonly IEnumerable<string> LowerCase = new[]
        {
            "a",
            "for",
            "of",
            "on",
            "and",
            "in",
            "the",
            "it",
            "it",
            "it's",
            "as",
            "to",
            "ca.",
            "into",
        };
    }
}