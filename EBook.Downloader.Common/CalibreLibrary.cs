// -----------------------------------------------------------------------
// <copyright file="CalibreLibrary.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Represents a <see href="https://calibre-ebook.com/">calibre</see> library.
    /// </summary>
    public class CalibreLibrary : IDisposable
    {
        private const string TriggerName = "books_update_trg";

        private const string UpdateLastModifiedById = "UPDATE books SET last_modified = :lastModified WHERE id = :id";

        private readonly ILogger logger;

        private readonly Calibre.CalibreDb calibreDb;

        private readonly Microsoft.Data.Sqlite.SqliteCommand updateLastModifiedCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand? dropTriggerCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand? createTriggerCommand;

        private readonly Microsoft.Data.Sqlite.SqliteConnection connection;

        private bool disposedValue; // To detect redundant calls

        /// <summary>
        /// Initialises a new instance of the <see cref="CalibreLibrary" /> class.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="calibrePath">The path to the calibre binaries.</param>
        public CalibreLibrary(string path, ILogger logger, string calibrePath = Calibre.CalibreDb.DefaultCalibrePath)
        {
            this.Path = path;
            this.logger = logger;
            this.calibreDb = new Calibre.CalibreDb(path, logger, calibrePath);

            var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(path, "metadata.db"), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite };
            this.connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionStringBuilder.ConnectionString);
            this.connection.Open();

            this.updateLastModifiedCommand = this.connection.CreateCommand();
            this.updateLastModifiedCommand.CommandText = UpdateLastModifiedById;
            this.updateLastModifiedCommand.Parameters.Add(":lastModified", Microsoft.Data.Sqlite.SqliteType.Text);
            this.updateLastModifiedCommand.Parameters.Add(":id", Microsoft.Data.Sqlite.SqliteType.Integer);

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
        /// <returns>The book.</returns>
        public CalibreBook? GetBookByIdentifierAndExtension(string identifier, string type, string extension)
        {
            if (identifier is null)
            {
                return default;
            }

            // get the date time of the format
            return this.GetCalibreBook(new Calibre.Identifier(type, identifier), extension?.TrimStart('.') ?? "EPUB");
        }

        /// <summary>
        /// Adds a new book, or updates the book if it exists in the calibre library.
        /// </summary>
        /// <param name="info">The EPUB info.</param>
        /// <param name="maxTimeOffset">The maximum time offset.</param>
        /// <returns><see langword="true"/> if the EPUB has been added/updated; otherwise <see langword="false" />.</returns>
        public Task<bool> AddOrUpdateAsync(EpubInfo info, int maxTimeOffset)
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
                CalibreBook? book = this.GetCalibreBook(new Calibre.Identifier(identifier.Key, identifier.Value), info.Path.Extension.TrimStart('.'));

                if (book is null)
                {
                    // see if we need to add the book or add the format
                    book = this.GetCalibreBook(new Calibre.Identifier(identifier.Key, identifier.Value));

                    if (book is null)
                    {
                        book = await AddBookAsync(info).ConfigureAwait(false);
                    }
                    else if (info.Path is not null)
                    {
                        // add this format
                        this.calibreDb.AddFormat(book.Id, info.Path, dontReplace: true);
                    }

                    if (book is not null && book.Path is not null && book.Name is not null && info.Path is not null)
                    {
                        UpdateLastWriteTime(book.Path, book.Name, info);
                        this.UpdateDescription(book.Id, info.LongDescription);
                        await this.UpdateLastModifiedAsync(book.Id, book.Name, info.Path.LastWriteTimeUtc, book.LastModified, maxTimeOffset).ConfigureAwait(false);
                    }

                    return true;
                }

                var fullPath = book.GetFullPath(this.Path, info.Path.Extension);
                if (System.IO.File.Exists(fullPath))
                {
                    // see if this has changed at all
                    if (!CheckFiles(info.Path, fullPath, this.logger))
                    {
                        // files are not the same. Copy in the new file
                        this.logger.LogInformation("Replacing {0} as files do not match", book.Name);

                        // access the destination file first
                        var bytes = new byte[ushort.MaxValue];
                        using (var stream = System.IO.File.OpenRead(fullPath))
                        {
                            int length;
                            while ((length = await stream.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false)) == bytes.Length)
                            {
                            }
                        }

                        info.Path.CopyTo(fullPath, overwrite: true);
                    }

                    this.UpdateDescription(book.Id, info.LongDescription);
                    this.UpdateSeries(book.Id, info.SeriesName, info.SeriesIndex);
                    await this.UpdateLastModifiedAsync(book.Id, book.Name, info.Path.LastWriteTimeUtc, book.LastModified, maxTimeOffset).ConfigureAwait(false);
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
                            coverFile = System.IO.Path.GetTempFileName();
                            using var zipStream = coverEntry.Open();
                            using var fileStream = System.IO.File.OpenWrite(coverFile);
                            await zipStream.CopyToAsync(fileStream).ConfigureAwait(false);
                        }
                    }

                    // sanitise the tags
                    var tags = default(string);
                    if (info.Tags.Any())
                    {
                        var sanitisedTags = info.Tags
                            .SelectMany(tag => tag.Split(new[] { "--" }, StringSplitOptions.RemoveEmptyEntries))
                            .Select(tag => tag.Trim().Replace(",", ";"))
                            .Distinct(StringComparer.Ordinal);
                        tags = string.Join(", ", sanitisedTags);
                    }

                    // we need to add this
                    var bookId = this.calibreDb.Add(info.Path, duplicates: true, languages: "eng", cover: coverFile, tags: tags);
                    if (coverFile != default && System.IO.File.Exists(coverFile))
                    {
                        System.IO.File.Delete(coverFile);
                    }

                    return this.GetCalibreBook(bookId);
                }

                static bool CheckFiles(System.IO.FileInfo source, string destination, ILogger logger)
                {
                    var destinationFileInfo = new System.IO.FileInfo(destination);
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
                        using var stream = System.IO.File.OpenRead(fileName);
                        return sha.ComputeHash(stream);
                    }
                }

                void UpdateLastWriteTime(string path, string name, EpubInfo info)
                {
                    var fullPath = System.IO.Path.Combine(this.Path, path, $"{name}{info.Path.Extension}");
                    if (System.IO.File.Exists(fullPath))
                    {
                        _ = new System.IO.FileInfo(fullPath) { LastWriteTime = info.Path.LastWriteTime };
                    }
                }
            }
        }

        /// <summary>
        /// Updates the last modified date, description, and series.
        /// </summary>
        /// <param name="book">The book.</param>
        /// <param name="lastModified">The last modified date.</param>
        /// <param name="longDescription">The long description.</param>
        /// <param name="seriesName">The series name.</param>
        /// <param name="seriesIndex">The series index.</param>
        /// <param name="maxTimeOffset">The maximum time offset.</param>
        /// <returns>The task associated with this function.</returns>
        public Task UpdateLastModifiedDescriptionAndSeriesAsync(CalibreBook book, DateTime lastModified, System.Xml.XmlElement? longDescription, string? seriesName, float seriesIndex, int maxTimeOffset)
        {
            if (book is null)
            {
                throw new ArgumentNullException(nameof(book));
            }

            System.Diagnostics.Contracts.Contract.EndContractBlock();

            return UpdateLastModifiedDescriptionAndSeriesInternalAsync(book, lastModified, longDescription, seriesName, seriesIndex, maxTimeOffset);

            async Task UpdateLastModifiedDescriptionAndSeriesInternalAsync(CalibreBook book, DateTime lastModified, System.Xml.XmlElement? longDescription, string? seriesName, float seriesIndex, int maxTimeOffset)
            {
                if ((this.UpdateDescription(book.Id, longDescription)
                    || this.UpdateSeries(book.Id, seriesName, seriesIndex)) && this.GetCalibreBook(book.Id) is CalibreBook processed)
                {
                    // refresh the book with the last data
                    book = processed;
                }

                await this.UpdateLastModifiedAsync(book.Id, book.Name, lastModified, book.LastModified, maxTimeOffset).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates the last modified date, description, and series.
        /// </summary>
        /// <param name="book">The book.</param>
        /// <param name="lastModified">The last modified date.</param>
        /// <param name="maxTimeOffset">The maximum time offset.</param>
        /// <returns>The task associated with this function.</returns>
        public Task UpdateLastModified(CalibreBook book, DateTime lastModified, int maxTimeOffset) => book is null
            ? throw new ArgumentNullException(nameof(book))
            : this.UpdateLastModifiedAsync(book.Id, book.Name, lastModified, book.LastModified, maxTimeOffset);

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
                    this.dropTriggerCommand?.Dispose();
                    this.createTriggerCommand?.Dispose();
                    this.connection?.Dispose();
                }

                this.disposedValue = true;
            }
        }

        private static CalibreBook? GetCalibreBook(System.Text.Json.JsonDocument document) => document
            .RootElement
            .EnumerateArray()
            .Select(json => new CalibreBook(json))
            .SingleOrDefault();

        private bool UpdateDescription(int id, System.Xml.XmlElement? longDescription)
        {
            if (longDescription is null)
            {
                return false;
            }

            var json = this.calibreDb.List(new[] { "comments" }, searchPattern: FormattableString.Invariant($"id:\"={id}\"")).RootElement.EnumerateArray().First();
            var currentDescription = SanitiseHtml(json.GetProperty("comments").GetString());
            var newDescription = SanitiseHtml(longDescription.OuterXml);
            if (string.Equals(currentDescription, newDescription, StringComparison.Ordinal))
            {
                return false;
            }

            // execute calibredb to update the description
            this.logger.LogInformation("Updating description to the long description");
            this.calibreDb.SetMetadata(id, "comments", newDescription);
            return true;

            static string? SanitiseHtml(string? html)
            {
                if (html is null)
                {
                    return null;
                }

                var htmlDocument = new HtmlAgilityPack.HtmlDocument();
                htmlDocument.LoadHtml(html);
                return htmlDocument.DocumentNode.OuterHtml;
            }
        }

        private bool UpdateSeries(int id, string? seriesName, float seriesIndex)
        {
            var (currentSeriesName, currentSeriesIndex) = GetCurrentSeries();
            if (string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && (seriesName is null || currentSeriesIndex == seriesIndex))
            {
                // neither have a series, or the indexes match in the same series.
                return false;
            }

            (string Field, object? Value)[]? fields = default;
            if (!string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && seriesName is null)
            {
                // execute calibredb to clear the series
                this.logger.LogInformation("Clearing series");
                fields = new (string, object?)[] { ("series", seriesName) };
            }
            else if (!string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && currentSeriesIndex != seriesIndex)
            {
                // execute calibredb to update the series index
                this.logger.LogInformation("Updating series and index to {Series}:{SeriesIndex}", seriesName, seriesIndex);
                fields = new (string, object?)[] { ("series", seriesName), ("series_index", seriesIndex) };
            }
            else if (!string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && currentSeriesIndex == seriesIndex)
            {
                // execute calibredb to update the series
                this.logger.LogInformation("Updating series to {Series}:{SeriesIndex}", seriesName, seriesIndex);
                fields = new (string, object?)[] { ("series", seriesName) };
            }
            else if (string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && currentSeriesIndex != seriesIndex)
            {
                // execute calibredb to update the series index
                this.logger.LogInformation("Updating series index to {Series}:{SeriesIndex}", seriesName, seriesIndex);
                fields = new (string, object?)[] { ("series_index", seriesIndex) };
            }

            if (fields is not null)
            {
                this.calibreDb.SetMetadata(id, fields.ToLookup(_ => _.Field, _ => _.Value, StringComparer.OrdinalIgnoreCase));
            }

            return true;

            (string?, float) GetCurrentSeries()
            {
                var json = this.calibreDb.List(new[] { "series", "series_index" }, searchPattern: FormattableString.Invariant($"id:\"={id}\"")).RootElement.EnumerateArray().First();
                return (json.TryGetProperty("series", out var seriesProperty) ? seriesProperty.GetString() : default, json.GetProperty("series_index").GetSingle());
            }
        }

        private async Task UpdateLastModifiedAsync(int id, string name, DateTime fileLastModified, DateTime lastModified, int maxTimeOffset)
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
                this.logger.LogInformation("Updating last modified time for {0} in the database from {1} to {2}", name, lastModified.ToUniversalTime(), fileLastModified);
                this.updateLastModifiedCommand.Parameters[":lastModified"].Value = fileLastModified.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz", System.Globalization.CultureInfo.InvariantCulture);
                this.updateLastModifiedCommand.Parameters[":id"].Value = id;

                if (this.dropTriggerCommand is not null)
                {
                    await this.dropTriggerCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                await this.updateLastModifiedCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

                if (this.createTriggerCommand is not null)
                {
                    await this.createTriggerCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        private CalibreBook? GetCalibreBook(int id) => this.GetCalibreBook(FormattableString.Invariant($"id:\"={id}\""));

        private CalibreBook? GetCalibreBook(Calibre.Identifier identifier) => this.GetCalibreBook($"identifier:\"={identifier}\"");

        private CalibreBook? GetCalibreBook(Calibre.Identifier identifier, string format) => this.GetCalibreBook($"identifier:\"={identifier}\" and formats:\"{format}\"");

        private CalibreBook? GetCalibreBook(string searchPattern) => GetCalibreBook(this.calibreDb.List(new[] { "id", "formats", "last_modified" }, searchPattern: searchPattern));
    }
}