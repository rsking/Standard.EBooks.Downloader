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

    private static readonly string[] Separator = ["--"];

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
        : this(new Calibre.CalibreDb(path, useContentServer, logger, calibrePath), logger)
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="CalibreLibrary" /> class.
    /// </summary>
    /// <param name="calibreDb">The calibre DB instance.</param>
    /// <param name="logger">The logger.</param>
    public CalibreLibrary(Calibre.CalibreDb calibreDb, ILogger logger)
    {
        this.logger = logger;
        this.calibreDb = calibreDb;
        var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(this.Path, "metadata.db"), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite };
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
    public string Path => this.calibreDb.Path;

    /// <summary>
    /// Gets the book for a specified identifier and extension.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <param name="type">The type of identifier.</param>
    /// <param name="extension">The extension to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The book.</returns>
    public Task<CalibreBook?> GetBookByIdentifierAndExtensionAsync(string? identifier, string type, string? extension, CancellationToken cancellationToken = default)
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
        var books = await this.calibreDb.ListAsync(["id", "authors", "title", "last_modified", "identifiers", "publisher"], searchPattern: searchPattern, cancellationToken: cancellationToken).ConfigureAwait(false);

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
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the EPUB has been added/updated; otherwise <see langword="false" />.</returns>
    public Task<bool> AddOrUpdateAsync(
        EpubInfo info,
        int maxTimeOffset,
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

                if (book is { Id: { } id } && info is { Path: { } infoPath })
                {
                    // add this format
                    await this.calibreDb.AddFormatAsync(id, infoPath, dontReplace: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                book ??= await AddBookAsync(info).ConfigureAwait(false);

                if (book is { Path: { } bookPath, Name: { } name })
                {
                    UpdateLastWriteTime(bookPath, name, info);
                    await UpdateMetadata(book).ConfigureAwait(false);
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

                await UpdateMetadata(book).ConfigureAwait(false);

                return true;
            }

            this.logger.LogError("Failed to find file {Name}, tried {Path}", book.Name, fullPath);
            return false;

            async Task<CalibreBook?> AddBookAsync(EpubInfo info)
            {
                // extract out the cover
                var coverFile = default(string);
                using (var zipFile = System.IO.Compression.ZipFile.OpenRead(info.Path.FullName))
                {
                    if (zipFile.Entries.FirstOrDefault(entry => string.Equals(entry.Name, "cover.svg", StringComparison.Ordinal)) is { } coverEntry)
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

            async Task UpdateMetadata(CalibreBook book)
            {
                await this.UpdateAsync(book, info, maxTimeOffset, cancellationToken).ConfigureAwait(false);
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
    /// <param name="maxTimeOffset">The maximum time offset.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task associated with this function.</returns>
    public Task UpdateAsync(CalibreBook book, EpubInfo epub, int maxTimeOffset, CancellationToken cancellationToken = default)
    {
        if (book is null)
        {
            throw new ArgumentNullException(nameof(book));
        }

        System.Diagnostics.Contracts.Contract.EndContractBlock();

        return UpdateInternalAsync(book, epub, maxTimeOffset, cancellationToken);

        async Task UpdateInternalAsync(CalibreBook book, EpubInfo epub, int maxTimeOffset, CancellationToken cancellationToken = default)
        {
            var series = epub.Collections
                .FirstOrDefault(collection => collection.Type == EpubCollectionType.Series);
            var sets = epub.Collections
                .Where(collection => collection.Type == EpubCollectionType.Set)
                .Select(collection => collection.Name);

            var (currentTitle, currentSubtitle, currentLongDescription, currentSeriesName, currentSeriesIndex, currentSets, currentTags, currentDate) = await GetCurrentAsync(book.Id, cancellationToken).ConfigureAwait(false);

            var longDescription = epub.LongDescription;
            var seriesName = series?.Name;
            var seriesIndex = series?.Position ?? 0;

            var fields = UpdateTitle(epub.Title, currentTitle)
                .Concat(UpdateSubtitle(epub.Subtitle, currentSubtitle))
                .Concat(UpdateDescription(longDescription, currentLongDescription))
                .Concat(UpdateSeries(seriesName, seriesIndex, currentSeriesName, currentSeriesIndex))
                .Concat(UpdateSets(sets, currentSets))
                .Concat(UpdateTags(SanitiseTags(epub.Tags), currentTags))
                .Concat(UpdateDate(epub.Date, currentDate));
            if (await SetMetadataAsync(book.Id, fields, cancellationToken).ConfigureAwait(false)
                && await this.GetCalibreBookAsync(book.Id, cancellationToken).ConfigureAwait(false) is { } processed)
            {
                book = processed;
            }

            // refresh the book with the last data
            await this.UpdateLastModifiedAsync(book, epub.Path.LastWriteTimeUtc, maxTimeOffset, cancellationToken).ConfigureAwait(false);

            async Task<(string Title, string? Subtitle, string? LongDescription, string? SeriesName, float SeriesIndex, string? Sets, IEnumerable<string> Tags, DateTimeOffset? Date)> GetCurrentAsync(int id, CancellationToken cancellationToken)
            {
                var document = await this.calibreDb
                    .ListAsync(["title", "*subtitle", "comments", "tags", "series", "series_index", "*sets", "pubdate"], searchPattern: FormattableString.Invariant($"id:\"={id}\""), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (document is null)
                {
                    return default;
                }

                var json = document.RootElement.EnumerateArray().First();

                var title = GetCurrentTitle(json);
                var subtitle = GetCurrentSubtitle(json);
                var longDescription = GetCurrentLongDescription(json);
                (var seriesName, var seriesIndex) = GetCurrentSeries(json);
                var sets = GetCurrentSets(json);
                var tags = GetCurrentTags(json);
                var date = GetCurrentDate(json);
                return (title, subtitle, longDescription, seriesName, seriesIndex, sets, tags, date);

                static string GetCurrentTitle(System.Text.Json.JsonElement json)
                {
                    return json.GetProperty("title").GetString();
                }

                static string? GetCurrentSubtitle(System.Text.Json.JsonElement json)
                {
                    return json.TryGetProperty("*subtitle", out var subtitle) ? subtitle.GetString() : default;
                }

                static string? GetCurrentLongDescription(System.Text.Json.JsonElement json)
                {
                    return SanitiseHtml(json.GetProperty("comments").GetString());
                }

                static DateTimeOffset? GetCurrentDate(System.Text.Json.JsonElement json)
                {
                    return json.GetProperty("pubdate").GetDateTimeOffset();
                }

                static (string? Name, float Index) GetCurrentSeries(System.Text.Json.JsonElement json)
                {
                    return (GetSeries(json), json.GetProperty("series_index").GetSingle());

                    static string? GetSeries(System.Text.Json.JsonElement json)
                    {
                        return json.TryGetProperty("series", out var seriesProperty)
                            ? seriesProperty.GetString()
                            : default;
                    }
                }

                static string? GetCurrentSets(System.Text.Json.JsonElement json)
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

                static IEnumerable<string> GetCurrentTags(System.Text.Json.JsonElement json)
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
            }

            IEnumerable<FieldToUpdate> UpdateTitle(string title, string currentTitle)
            {
                if (string.Equals(title, currentTitle, StringComparison.Ordinal))
                {
                    yield break;
                }

                // execute calibredb to update the description
                this.logger.LogInformation("Updating title");
                yield return new FieldToUpdate("title", title);
            }

            IEnumerable<FieldToUpdate> UpdateSubtitle(string? subtitle, string? currentSubtitle)
            {
                if ((subtitle is null && currentSubtitle is null)
                    || string.Equals(subtitle, currentSubtitle, StringComparison.Ordinal))
                {
                    yield break;
                }

                // execute calibredb to update the description
                this.logger.LogInformation("Updating subtitle");
                yield return new FieldToUpdate("#subtitle", subtitle);
            }

            IEnumerable<FieldToUpdate> UpdateDescription(System.Xml.XmlElement? longDescription, string? currentLongDescription)
            {
                return longDescription is { OuterXml: var outerXml }
                    ? UpdateDescription(SanitiseHtml(outerXml), currentLongDescription)
                    : [];

                IEnumerable<FieldToUpdate> UpdateDescription(string? longDescription, string? currentLongDescription)
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

            IEnumerable<FieldToUpdate> UpdateDate(DateTimeOffset date, DateTimeOffset? currentDate)
            {
                if (currentDate.HasValue && date == currentDate)
                {
                    yield break;
                }

                this.logger.LogInformation("Updating published date");
                yield return new FieldToUpdate("pubdate", date);
            }

            IEnumerable<FieldToUpdate> UpdateSeries(string? name, float index, string? currentName, float currentIndex)
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

            IEnumerable<FieldToUpdate> UpdateSets(IEnumerable<string> sets, string? currentSets)
            {
                return UpdateSetsInternal(string.Join(",", sets.OrderBy(set => set, StringComparer.Ordinal)), currentSets);

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

            IEnumerable<FieldToUpdate> UpdateTags(IEnumerable<string> tags, IEnumerable<string> currentTags)
            {
                return UpdatetTagsInternal(tags, currentTags);

                IEnumerable<FieldToUpdate> UpdatetTagsInternal(IEnumerable<string> tags, IEnumerable<string> currentTags)
                {
                    var tagsString = string.Join(",", tags.OrderBy(tag => tag, StringComparer.Ordinal));
                    var currentTagsString = string.Join(",", currentTags.OrderBy(tag => tag, StringComparer.Ordinal));

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

            async Task<bool> SetMetadataAsync(int id, IEnumerable<FieldToUpdate> fields, CancellationToken cancellationToken = default)
            {
                var fieldsArray = fields.ToArray();

                if (fieldsArray.Length > 0)
                {
                    await this.calibreDb.SetMetadataAsync(id, fieldsArray.ToLookup(_ => _.Field, _ => _.Value, StringComparer.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);
                    return true;
                }

                return false;
            }
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
        return book is null
            ? throw new ArgumentNullException(nameof(book))
            : UpdateLastModifiedAsyncCore();

        async Task UpdateLastModifiedAsyncCore()
        {
            var bookLastModified = await GetDateTimeFromDatabaseAsync(book.Id, cancellationToken).ConfigureAwait(false);
            await UpdateLastModifiedAsync(
                book.Id,
                book.Name,
                lastModified,
                bookLastModified,
                maxTimeOffset,
                cancellationToken).ConfigureAwait(false);

            async Task<DateTime> GetDateTimeFromDatabaseAsync(int id, CancellationToken cancellationToken)
            {
                this.selectLastModifiedCommand.Parameters[":id"].Value = id;
                var dateTimeObject = await this.selectLastModifiedCommand
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false);
                return dateTimeObject is string dataTimeString
                    ? DateTime.Parse(dataTimeString, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)
                    : throw new InvalidOperationException("Failed to get last modified time");
            }

            async Task UpdateLastModifiedAsync(int id, string name, DateTime fileLastModified, DateTime lastModified, int maxTimeOffset, CancellationToken cancellationToken = default)
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
        }
    }

    /// <summary>
    /// Gets the book by the identifier.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The book if found; otherwise <see langword="null"/>.</returns>
    public Task<CalibreBook?> GetCalibreBookAsync(Calibre.Identifier identifier, CancellationToken cancellationToken) => this.GetCalibreBookAsync($"identifier:\"={identifier}\"", element => CheckIdentifier(element, identifier), cancellationToken);

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

    private static IEnumerable<CalibreBook> GetCalibreBooks(System.Text.Json.JsonDocument? document, Func<System.Text.Json.JsonElement, bool> predicate)
    {
        return document is { RootElement: { ValueKind: System.Text.Json.JsonValueKind.Array } rootElement }
            ? ProcessDocument(rootElement, predicate)
            : [];

        static IEnumerable<CalibreBook> ProcessDocument(System.Text.Json.JsonElement rootElement, Func<System.Text.Json.JsonElement, bool> predicate)
        {
            return rootElement
                .EnumerateArray()
                .Where(predicate)
                .Select(GetCalibreBook);

            static CalibreBook GetCalibreBook(System.Text.Json.JsonElement json)
            {
                return new(json);
            }
        }
    }

    private static bool CheckIdentifier(System.Text.Json.JsonElement element, Calibre.Identifier identifier) => element.GetProperty("identifiers").GetProperty(identifier.Name).ToString()?.Equals(identifier.Value.ToString(), StringComparison.Ordinal) == true;

    private static string? SanitiseHtml(string? html) => html is null ? null : Parser.ParseDocument(html).Body?.FirstChild?.Minify();

    private static IEnumerable<string> SanitiseTags(IEnumerable<string> tags)
    {
        return JoinIfRequired(
                tags.SelectMany(SplitByDashes)
                .Select(tag => tag.Trim()))
            .Select(CaseCorrectly)
            .Select(ReplaceCommas)
            .Select(RemoveQuotes)
            .Distinct(StringComparer.Ordinal);

        static IEnumerable<string> SplitByDashes(string value)
        {
            return value.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        }

        static IEnumerable<string> JoinIfRequired(IEnumerable<string> values)
        {
            var enumerator = values.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                yield break;
            }

            var last = enumerator.Current;
            while (enumerator.MoveNext())
            {
                var item = enumerator.Current;

                if (item.StartsWith("(", StringComparison.Ordinal))
                {
                    if (last is not null)
                    {
                        yield return last + " " + item;
                        last = default;
                    }
                    else
                    {
                        yield return item;
                    }

                    continue;
                }

                if (last is not null)
                {
                    yield return last;
                }

                last = item;
            }

            if (last is not null)
            {
                yield return last;
            }
        }

        static string ReplaceCommas(string tag)
        {
            return tag.Replace(',', '�');
        }

        static string RemoveQuotes(string tag)
        {
            return tag.Replace("\"", string.Empty);
        }

        static string CaseCorrectly(string value)
        {
            var (name, character) = Split(value);

            return Join(ToProperCase(name)!.Replace(" and ", " & "), ToProperCase(character));

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2589:Boolean expressions should not be gratuitous", Justification = "False positive")]
            static (string Name, string? Character) Split(string value)
            {
                return value.IndexOf('(') switch
                {
                    <= 0 => (value, default(string)),
                    int index => (value.Substring(0, index - 1), value.Substring(index)),
                };
            }

            static string Join(string name, string? character)
            {
                return character is null
                    ? name
                    : $"{name} {character}";
            }

            static string? ToProperCase(string? strX)
            {
                return strX is null
                    ? null
                    : ToProperCaseImpl(strX, ' ', " ", System.Globalization.CultureInfo.CurrentCulture.TextInfo);

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
                            _ = count == 0
                                ? currentWord.Append(textInfo.ToUpper(letter))
                                : currentWord.Append(letter);

                            count++;
                        }

                        return currentWord.ToString();
                    }
                }
            }
        }
    }

    private Task<CalibreBook?> GetCalibreBookAsync(int id, CancellationToken cancellationToken) => this.GetCalibreBookAsync(FormattableString.Invariant($"id:\"={id}\""), _ => true, cancellationToken);

    private Task<CalibreBook?> GetCalibreBookAsync(Calibre.Identifier identifier, string format, CancellationToken cancellationToken) => this.GetCalibreBookAsync($"identifier:\"={identifier}\" and formats:\"{format}\"", element => CheckIdentifier(element, identifier), cancellationToken);

    private async Task<CalibreBook?> GetCalibreBookAsync(string searchPattern, Func<System.Text.Json.JsonElement, bool> predicate, CancellationToken cancellationToken)
    {
        return GetCalibreBook(await this.calibreDb.ListAsync(["id", "authors", "title", "last_modified", "identifiers"], searchPattern: searchPattern, cancellationToken: cancellationToken).ConfigureAwait(false), predicate);

        static CalibreBook? GetCalibreBook(System.Text.Json.JsonDocument? document, Func<System.Text.Json.JsonElement, bool> predicate)
        {
            return GetCalibreBooks(document, predicate).SingleOrDefault();
        }
    }

    private sealed record class FieldToUpdate(string Field, object? Value);

    private static class Words
    {
        public static readonly IEnumerable<string> LowerCase =
        [
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
        ];
    }
}