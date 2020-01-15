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

        private const string SelectById = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book WHERE b.id = :id AND d.format = :extension";

        private const string SelectByIdentifier = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d INNER JOIN identifiers i ON b.id = i.book WHERE i.type = :type AND i.val = :identifier LIMIT 1";

        private const string SelectByIdentifierAndExtension = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book INNER JOIN identifiers i ON b.id = i.book WHERE i.type = :type AND i.val = :identifier AND d.format = :extension LIMIT 1";

        private const string UpdateById = "UPDATE books SET last_modified = :lastModified WHERE id = :id";

        private readonly ILogger logger;

        private readonly string calibreDbPath;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByIdAndExtensionCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByIdentifierCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByIdentifierAndExtensionCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand updateCommand;

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

            this.selectBookByIdAndExtensionCommand = this.connection.CreateCommand();
            this.selectBookByIdAndExtensionCommand.CommandText = SelectById;
            this.selectBookByIdAndExtensionCommand.Parameters.Add(":id", Microsoft.Data.Sqlite.SqliteType.Integer);
            this.selectBookByIdAndExtensionCommand.Parameters.Add(":extension", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByIdAndExtensionCommand.Prepare();

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

            this.updateCommand = this.connection.CreateCommand();
            this.updateCommand.CommandText = UpdateById;
            this.updateCommand.Parameters.Add(":lastModified", Microsoft.Data.Sqlite.SqliteType.Text);
            this.updateCommand.Parameters.AddWithValue(":id", Microsoft.Data.Sqlite.SqliteType.Integer);

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
#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                this.createTriggerCommand.CommandText = createTriggerCommandText;
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            }
        }

        /// <summary>
        /// Gets the path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the last-modified for a specified URL and format.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <param name="type">The type of identifier.</param>
        /// <param name="extension">The format to check.</param>
        /// <returns>The last modified date time.</returns>
        public async Task<DateTime?> GetDateTimeByIdentifierAndExtensionAsync(string identifier, string type, string extension)
        {
            if (identifier is null)
            {
                return null;
            }

            // get the date time of the format
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":type"].Value = type;
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":identifier"].Value = identifier;
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":extension"].Value = extension.TrimStart('.').ToUpperInvariant();
            (int id, string path, string name, string lastModified) book = default;

            using (var reader = await this.selectBookByIdentifierAndExtensionCommand.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    book = (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                }
                else
                {
                    return null;
                }
            }

            var fullPath = System.IO.Path.Combine(this.Path, book.path, $"{book.name}{extension}");

            if (System.IO.File.Exists(fullPath))
            {
                var fileInfo = new System.IO.FileInfo(fullPath);
                await this.UpdateLastModifiedAsync(book.id, book.name, fileInfo, book.lastModified).ConfigureAwait(false);
                return fileInfo.LastWriteTimeUtc;
            }

            return null;
        }

        /// <summary>
        /// Updates the EPUB if it exists in the calibre library.
        /// </summary>
        /// <param name="info">The EPUB info.</param>
        /// <returns><see langword="true"/> if the EPUB has been updated; otherwise <see langword="false" />.</returns>
        public async Task<bool> UpdateIfExistsAsync(EpubInfo info)
        {
            (int id, string path, string name, string lastModified) book = default;

            var identifier = info.Identifiers.First();
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":type"].Value = identifier.Key;
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":identifier"].Value = identifier.Value;
            this.selectBookByIdentifierAndExtensionCommand.Parameters[":extension"].Value = info.Extension.TrimStart('.').ToUpperInvariant();

            using (var reader = await this.selectBookByIdentifierAndExtensionCommand.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    book = (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                }
            }

            if (book.id == default || book.path is null || book.name is null)
            {
                // see if we need to add the book or add the format
                this.selectBookByIdentifierCommand.Parameters[":type"].Value = identifier.Key;
                this.selectBookByIdentifierCommand.Parameters[":identifier"].Value = identifier.Value;

                using (var reader = await this.selectBookByIdentifierCommand.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    if (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        book = (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                    }
                }

                if (book.id == 0)
                {
                    // add this book
                    var coverFile = default(string);
                    var args = $"--duplicates --languages eng \"{info.Path}\"";

                    // extract out the cover
                    using (var zipFile = System.IO.Compression.ZipFile.OpenRead(info.Path))
                    {
                        var coverEntry = zipFile.Entries.FirstOrDefault(entry => entry.Name == "cover.svg");
                        if (coverEntry != null)
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
                            .Distinct();
                        args += $" --tags=\"{string.Join(", ", sanitisedTags)}\"";
                    }

                    // we need to add this
                    this.ExecuteCalibreDbToLogger("add", args);
                    if (coverFile != default && System.IO.File.Exists(coverFile))
                    {
                        System.IO.File.Delete(coverFile);
                    }

                    using (var reader = await this.selectBookByIdentifierAndExtensionCommand.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            book = (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                        }
                    }

                    if (book.path != null && book.name != null && book.lastModified != null)
                    {
                        this.UpdateLastWriteTime(book.path, book.name, info);
                        await this.UpdateLastModifiedAsync(book.id, book.name, info.Path, book.lastModified).ConfigureAwait(false);
                    }
                }
                else if (book.path != null && book.name != null && book.lastModified != null)
                {
                    // add this format
                    this.ExecuteCalibreDbToLogger("add_format", "--dont-replace " + book.id + " \"" + info.Path + "\"");
                    this.UpdateLastWriteTime(book.path, book.name, info);
                    await this.UpdateLastModifiedAsync(book.id, book.name, info.Path, book.lastModified).ConfigureAwait(false);
                }

                return true;
            }
            else
            {
                var fullPath = System.IO.Path.Combine(this.Path, book.path, $"{book.name}{System.IO.Path.GetExtension(info.Path)}");

                if (System.IO.File.Exists(fullPath))
                {
                    // see if this has changed at all
                    if (!CheckFiles(info.Path, fullPath, this.logger))
                    {
                        // files are not the same. Copy in the new file
                        this.logger.LogInformation("Replacing {0} as files do not match", book.name);

                        // access the destination file first
                        var bytes = new byte[ushort.MaxValue];
                        using (var stream = System.IO.File.OpenRead(fullPath))
                        {
                            int length;
                            while ((length = stream.Read(bytes, 0, bytes.Length)) == bytes.Length)
                            {
                            }
                        }

                        System.IO.File.Copy(info.Path, fullPath, true);
                    }

                    // see if we need to update the last modified time
                    if (book.lastModified != null)
                    {
                        await this.UpdateLastModifiedAsync(book.id, book.name, info.Path, book.lastModified).ConfigureAwait(false);
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) below.
            this.Dispose(true);
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
                    this.selectBookByIdAndExtensionCommand?.Dispose();
                    this.selectBookByIdentifierCommand?.Dispose();
                    this.selectBookByIdentifierAndExtensionCommand?.Dispose();
                    this.updateCommand?.Dispose();
                    this.dropTriggerCommand?.Dispose();
                    this.createTriggerCommand?.Dispose();
                    this.connection?.Dispose();
                }

                this.disposedValue = true;
            }
        }

        private static bool CheckFiles(string source, string destination, ILogger logger)
        {
            var sourceFileInfo = new System.IO.FileInfo(source);
            var destinationFileInfo = new System.IO.FileInfo(destination);

            if (sourceFileInfo.LastWriteTime != destinationFileInfo.LastWriteTime)
            {
                logger.LogDebug("\tsource and destination have different modified dates");
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
        }

        private static byte[] GetFileHash(string fileName)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = System.IO.File.OpenRead(fileName);
            return sha.ComputeHash(stream);
        }

        private Task UpdateLastModifiedAsync(int id, string name, string path, string lastModified) => this.UpdateLastModifiedAsync(id, name, new System.IO.FileInfo(path), lastModified);

        private async Task UpdateLastModifiedAsync(int id, string name, System.IO.FileInfo sourceFileInfo, string lastModified)
        {
            var sourceLastWriteTime = sourceFileInfo.LastWriteTimeUtc;
            var sourceLastWriteTimeFormat = sourceLastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz", System.Globalization.CultureInfo.InvariantCulture);
            if (sourceLastWriteTimeFormat == lastModified)
            {
                return;
            }

            // check this as date time, to be within the same five minutes, and is the latest date/time
            var lastModifiedDateTime = DateTime.Parse(lastModified, System.Globalization.CultureInfo.InvariantCulture);
            var difference = sourceFileInfo.LastWriteTime - lastModifiedDateTime;
            if (Math.Abs(difference.TotalMinutes) > 5D || difference.TotalMinutes > 0)
            {
                // write this to the database
                this.logger.LogInformation("Updating last modified time for {0} in the database from {1} to {2}", name, lastModifiedDateTime.ToUniversalTime(), sourceLastWriteTime);
                this.updateCommand.Parameters[":lastModified"].Value = sourceLastWriteTimeFormat;
                this.updateCommand.Parameters[":id"].Value = id;

                if (this.dropTriggerCommand != null)
                {
                    await this.dropTriggerCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                await this.updateCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

                if (this.createTriggerCommand != null)
                {
                    await this.createTriggerCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        private void UpdateLastWriteTime(string path, string name, EpubInfo info)
        {
            var fullPath = System.IO.Path.Combine(this.Path, path, $"{name}{System.IO.Path.GetExtension(info.Path)}");
            if (System.IO.File.Exists(fullPath))
            {
                var fileSystemInfo = new System.IO.FileInfo(info.Path);
                var dateTime = fileSystemInfo.LastWriteTime;

                _ = new System.IO.FileInfo(fullPath) { LastWriteTime = dateTime };
            }
        }

        private void ExecuteCalibreDbToLogger(string command, string arguments = "") => this.ExecuteCalibreDb(command, arguments, (sender, args) =>
        {
            if (args?.Data is null)
            {
                return;
            }

            this.logger.LogInformation(0, args.Data);
        });

        private void ExecuteCalibreDb(string command, string arguments, System.Diagnostics.DataReceivedEventHandler outputDataReceived)
        {
            var fullArguments = command + " --library-path \"" + this.Path + "\" " + arguments;

            var processStartInfo = new System.Diagnostics.ProcessStartInfo(this.calibreDbPath, fullArguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = new System.Diagnostics.Process() { StartInfo = processStartInfo };

            process.OutputDataReceived += outputDataReceived;

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