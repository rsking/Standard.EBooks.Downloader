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
        private const string DefaultCalibrePath = "%PROGRAMFILES%\\Calibre2";

        private const string TriggerName = "books_update_trg";

        private const string SelectById = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book WHERE b.id = :id";

        private const string SelectByIdentifier = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book INNER JOIN identifiers i ON b.id = i.book WHERE i.type = :type AND i.val = :identifier LIMIT 1";

        private const string SelectByIdentifierAndExtension = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book INNER JOIN identifiers i ON b.id = i.book WHERE i.type = :type AND i.val = :identifier AND d.format = :extension LIMIT 1";

        private const string SelectDescriptionById = "SELECT text from comments where book = :id";

        private const string SelectSeriesById = "SELECT s.name as series_name, b.series_index FROM books_series_link AS bsl INNER JOIN series AS S ON bsl.series = s.id INNER JOIN books as b ON bsl.book = b.id WHERE b.id = :id";

        private const string UpdateLastModifiedById = "UPDATE books SET last_modified = :lastModified WHERE id = :id";

        private readonly ILogger logger;

        private readonly string calibreDbPath;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByIdCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByIdentifierCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByIdentifierAndExtensionCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectDescriptionCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectSeriesCommand;

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
        public CalibreLibrary(string path, ILogger logger, string calibrePath = DefaultCalibrePath)
        {
            this.Path = path;
            this.logger = logger;
            this.calibreDbPath = Environment.ExpandEnvironmentVariables(System.IO.Path.Combine(calibrePath, "calibredb.exe"));

            var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(path, "metadata.db"), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite };
            this.connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionStringBuilder.ConnectionString);
            this.connection.Open();

            this.selectBookByIdCommand = this.connection.CreateCommand();
            this.selectBookByIdCommand.CommandText = SelectById;
            this.selectBookByIdCommand.Parameters.Add(":id", Microsoft.Data.Sqlite.SqliteType.Integer);
            this.selectBookByIdCommand.Prepare();

            this.selectBookByIdentifierCommand = this.connection.CreateCommand();
            this.selectBookByIdentifierCommand.CommandText = SelectByIdentifier;
            this.selectBookByIdentifierCommand.Parameters.Add(":type", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByIdentifierCommand.Parameters.Add(":identifier", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByIdentifierCommand.Prepare();

            this.selectBookByIdentifierAndExtensionCommand = this.connection.CreateCommand();
            this.selectBookByIdentifierAndExtensionCommand.CommandText = SelectByIdentifierAndExtension;
            this.selectBookByIdentifierAndExtensionCommand.Parameters.Add(":type", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByIdentifierAndExtensionCommand.Parameters.Add(":identifier", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByIdentifierAndExtensionCommand.Parameters.Add(":extension", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByIdentifierAndExtensionCommand.Prepare();

            this.selectDescriptionCommand = this.connection.CreateCommand();
            this.selectDescriptionCommand.CommandText = SelectDescriptionById;
            this.selectDescriptionCommand.Parameters.Add(":id", Microsoft.Data.Sqlite.SqliteType.Integer);

            this.selectSeriesCommand = this.connection.CreateCommand();
            this.selectSeriesCommand.CommandText = SelectSeriesById;
            this.selectSeriesCommand.Parameters.Add(":id", Microsoft.Data.Sqlite.SqliteType.Integer);

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
        public async Task<CalibreBook?> GetBookByIdentifierAndExtensionAsync(string identifier, string type, string extension)
        {
            if (identifier is null)
            {
                return default;
            }

            // get the date time of the format
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":type"].Value = type;
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":identifier"].Value = identifier;
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":extension"].Value = extension?.TrimStart('.').ToUpperInvariant() ?? "EPUB";

            using var reader = await this.selectBookByIdentifierAndExtensionCommand.ExecuteReaderAsync().ConfigureAwait(false);
            return await reader.ReadAsync().ConfigureAwait(false)
                ? new CalibreBook(reader.GetInt32(0), reader.GetString(2), reader.GetString(1), reader.GetString(3))
                : default;
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
                CalibreBook? book = default;

                var identifier = info.Identifiers.First();
                this.selectBookByIdentifierAndExtensionCommand.Parameters[":type"].Value = identifier.Key;
                this.selectBookByIdentifierAndExtensionCommand.Parameters[":identifier"].Value = identifier.Value;
                this.selectBookByIdentifierAndExtensionCommand.Parameters[":extension"].Value = info.Extension.TrimStart('.').ToUpperInvariant();

                using (var reader = await this.selectBookByIdentifierAndExtensionCommand.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    if (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        book = new CalibreBook(reader.GetInt32(0), reader.GetString(2), reader.GetString(1), reader.GetString(3));
                    }
                }

                if (book is null)
                {
                    // see if we need to add the book or add the format
                    this.selectBookByIdentifierCommand.Parameters[":type"].Value = identifier.Key;
                    this.selectBookByIdentifierCommand.Parameters[":identifier"].Value = identifier.Value;

                    using (var reader = await this.selectBookByIdentifierCommand.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            book = new CalibreBook(reader.GetInt32(0), reader.GetString(2), reader.GetString(1), reader.GetString(3));
                        }
                    }

                    if (book is null)
                    {
                        book = await AddBookAsync(info).ConfigureAwait(false);
                    }
                    else if (info.Path is not null)
                    {
                        // add this format
                        this.ExecuteCalibreDb("add_format", FormattableString.Invariant($"--dont-replace {book.Id} \"{info.Path}\""));
                    }

                    if (book is not null && book.Path is not null && book.Name is not null && info.Path is not null)
                    {
                        UpdateLastWriteTime(book.Path, book.Name, info);
                        await this.UpdateDescriptionAsync(book.Id, info.LongDescription).ConfigureAwait(false);
                        await UpdateLastModifiedAsync(book.Id, book.Name, info.Path, book.LastModified, maxTimeOffset).ConfigureAwait(false);
                    }

                    return true;
                }

                var fullPath = book.GetFullPath(this.Path, System.IO.Path.GetExtension(info.Path));
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

                        System.IO.File.Copy(info.Path, fullPath, overwrite: true);
                    }

                    await this.UpdateDescriptionAsync(book.Id, info.LongDescription).ConfigureAwait(false);
                    await this.UpdateSeriesAsync(book.Id, info.SeriesName, info.SeriesIndex).ConfigureAwait(false);
                    await UpdateLastModifiedAsync(book.Id, book.Name, info.Path, book.LastModified, maxTimeOffset).ConfigureAwait(false);
                    return true;
                }

                return false;

                async Task<CalibreBook?> AddBookAsync(EpubInfo info)
                {
                    // add this book
                    var coverFile = default(string);
                    var args = $"--duplicates --languages eng \"{info.Path}\"";

                    // extract out the cover
                    using (var zipFile = System.IO.Compression.ZipFile.OpenRead(info.Path))
                    {
                        var coverEntry = zipFile.Entries.FirstOrDefault(entry => string.Equals(entry.Name, "cover.svg", StringComparison.Ordinal));
                        if (coverEntry is not null)
                        {
                            coverFile = System.IO.Path.GetTempFileName();
                            using var zipStream = coverEntry.Open();
                            using var fileStream = System.IO.File.OpenWrite(coverFile);
                            await zipStream.CopyToAsync(fileStream).ConfigureAwait(false);
                            args += $" --cover=\"{coverFile}\"";
                        }
                    }

                    if (info.Tags.Any())
                    {
                        // sanitise the tags
                        var sanitisedTags = info.Tags
                            .SelectMany(tag => tag.Split(new[] { "--" }, StringSplitOptions.RemoveEmptyEntries))
                            .Select(tag => tag.Trim().Replace(",", ";"))
                            .Distinct(StringComparer.Ordinal);
                        args += $" --tags=\"{string.Join(", ", sanitisedTags)}\"";
                    }

                    // we need to add this
                    this.ExecuteCalibreDb("add", args);
                    if (coverFile != default && System.IO.File.Exists(coverFile))
                    {
                        System.IO.File.Delete(coverFile);
                    }

                    using var reader = await this.selectBookByIdentifierAndExtensionCommand.ExecuteReaderAsync().ConfigureAwait(false);
                    if (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        return new CalibreBook(reader.GetInt32(0), reader.GetString(2), reader.GetString(1), reader.GetString(3));
                    }

                    return default;
                }

                static bool CheckFiles(string source, string destination, ILogger logger)
                {
                    var sourceFileInfo = new System.IO.FileInfo(source);
                    var destinationFileInfo = new System.IO.FileInfo(destination);

                    if (sourceFileInfo.LastWriteTime != destinationFileInfo.LastWriteTime)
                    {
                        logger.LogDebug("source and destination have different modified dates");
                        return false;
                    }

                    if (sourceFileInfo.Length != destinationFileInfo.Length)
                    {
                        logger.LogDebug("source and destination are different lengths");
                        return false;
                    }

                    var sourceHash = GetFileHash(sourceFileInfo.FullName);
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

                Task UpdateLastModifiedAsync(int id, string name, string path, DateTime lastModified, int maxTimeOffset)
                {
                    return this.UpdateLastModifiedAsync(id, name, new System.IO.FileInfo(path).LastWriteTimeUtc, lastModified, maxTimeOffset);
                }

                void UpdateLastWriteTime(string path, string name, EpubInfo info)
                {
                    var fullPath = System.IO.Path.Combine(this.Path, path, $"{name}{System.IO.Path.GetExtension(info.Path)}");
                    if (System.IO.File.Exists(fullPath))
                    {
                        var fileSystemInfo = new System.IO.FileInfo(info.Path);
                        var dateTime = fileSystemInfo.LastWriteTime;

                        _ = new System.IO.FileInfo(fullPath) { LastWriteTime = dateTime };
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
                if (await this.UpdateDescriptionAsync(book.Id, longDescription).ConfigureAwait(false)
                    || await this.UpdateSeriesAsync(book.Id, seriesName, seriesIndex).ConfigureAwait(false))
                {
                    // refresh the book with the last data
                    this.selectBookByIdCommand.Parameters[":id"].Value = book.Id;
                    using var reader = await this.selectBookByIdCommand.ExecuteReaderAsync().ConfigureAwait(false);
                    if (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        book = new CalibreBook(reader.GetInt32(0), reader.GetString(2), reader.GetString(1), reader.GetString(3));
                    }
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
                    this.selectBookByIdCommand?.Dispose();
                    this.selectBookByIdentifierCommand?.Dispose();
                    this.selectBookByIdentifierAndExtensionCommand?.Dispose();
                    this.selectDescriptionCommand?.Dispose();
                    this.selectSeriesCommand?.Dispose();
                    this.updateLastModifiedCommand?.Dispose();
                    this.dropTriggerCommand?.Dispose();
                    this.createTriggerCommand?.Dispose();
                    this.connection?.Dispose();
                }

                this.disposedValue = true;
            }
        }

        private async Task<bool> UpdateDescriptionAsync(int id, System.Xml.XmlElement? longDescription)
        {
            if (longDescription is null)
            {
                return false;
            }

            this.selectDescriptionCommand.Parameters[":id"].Value = id;
            var currentDescription = SanitiseHtml(await this.selectDescriptionCommand.ExecuteScalarAsync().ConfigureAwait(false) as string);
            var newDescription = SanitiseHtml(longDescription.OuterXml);
            if (string.Equals(currentDescription, newDescription, StringComparison.Ordinal))
            {
                return false;
            }

            // execute calibredb to update the description
            this.logger.LogInformation("Updating description to the long description");
            this.ExecuteCalibreDb("set_metadata", FormattableString.Invariant($"{id} --field comments:\"{newDescription}\""));
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

        private async Task<bool> UpdateSeriesAsync(int id, string? seriesName, float seriesIndex)
        {
            var (currentSeriesName, currentSeriesIndex) = await GetCurrentSeries().ConfigureAwait(false);
            if (string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && (seriesName is null || currentSeriesIndex == seriesIndex))
            {
                // neither have a series, or the indexes match in the same series.
                return false;
            }

            if (!string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && seriesName is null)
            {
                // execute calibredb to clear the series
                this.logger.LogInformation("Clearing series");
                this.ExecuteCalibreDb("set_metadata", FormattableString.Invariant($"{id} --field series:\"{seriesName}\" --field series_index:\"{1}\""));
            }
            else if (!string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && currentSeriesIndex != seriesIndex)
            {
                // execute calibredb to update the series index
                this.logger.LogInformation("Updating series and index to {Series}:{SeriesIndex}", seriesName, seriesIndex);
                this.ExecuteCalibreDb("set_metadata", FormattableString.Invariant($"{id} --field series:\"{seriesName}\" --field series_index:\"{seriesIndex}\""));
            }
            else if (!string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && currentSeriesIndex == seriesIndex)
            {
                // execute calibredb to update the series
                this.logger.LogInformation("Updating series to {Series}:{Series}", seriesName, seriesIndex);
                this.ExecuteCalibreDb("set_metadata", FormattableString.Invariant($"{id} --field series:\"{seriesName}\""));
            }
            else if (string.Equals(currentSeriesName, seriesName, StringComparison.Ordinal) && currentSeriesIndex != seriesIndex)
            {
                // execute calibredb to update the series index
                this.logger.LogInformation("Updating series index to {Series}:{SeriesIndex}", seriesName, seriesIndex);
                this.ExecuteCalibreDb("set_metadata", FormattableString.Invariant($"{id} --field series_index:\"{seriesIndex}\""));
            }

            return true;

            async Task<(string?, float)> GetCurrentSeries()
            {
                this.selectSeriesCommand.Parameters[":id"].Value = id;
                using var reader = await this.selectSeriesCommand.ExecuteReaderAsync().ConfigureAwait(false);
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    return (reader.GetString(0), reader.GetFloat(1));
                }

                return (default, 1F);
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

        private void ExecuteCalibreDb(string command, string arguments, System.Diagnostics.DataReceivedEventHandler? outputDataReceived = default)
        {
            var fullArguments = command + " --library-path \"" + this.Path + "\" " + arguments;
            this.logger.LogDebug(fullArguments);

            var processStartInfo = new System.Diagnostics.ProcessStartInfo(this.calibreDbPath, fullArguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = new System.Diagnostics.Process { StartInfo = processStartInfo };

            void DefaultOutputDataReceived(object sender, System.Diagnostics.DataReceivedEventArgs args)
            {
                if (args?.Data is null)
                {
                    return;
                }

                this.logger.LogInformation(0, args.Data);
            }

            process.OutputDataReceived += outputDataReceived ?? DefaultOutputDataReceived;

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args?.Data is null)
                {
                    return;
                }

                this.logger.LogError(0, args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();
        }
    }
}