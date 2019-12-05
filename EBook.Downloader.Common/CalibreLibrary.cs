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

        private const string SelectByInfo = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book INNER JOIN books_publishers_link bpl ON b.id = bpl.book INNER JOIN publishers p ON bpl.publisher = p.id INNER JOIN books_authors_link bal ON b.id = bal.book INNER JOIN authors a ON bal.author = a.id WHERE a.name = :author AND b.title = :title AND p.name = :publisher";

        private const string SelectByInfoAndFormat = SelectByInfo + " AND d.format = :extension";

        private const string SelectById = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book WHERE b.id = :id AND d.format = :extension";

        private const string SelectByUrl = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book INNER JOIN identifiers i ON b.id = i.book WHERE i.type = 'url' AND i.val = :uri LIMIT 1";

        private const string UpdateById = "UPDATE books SET last_modified = :lastModified WHERE id = :id";

        private readonly ILogger logger;

        private readonly string calibreDbPath;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByInfoCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByInfoAndFormatCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByIdCommand;

        private readonly Microsoft.Data.Sqlite.SqliteCommand selectBookByUrlCommand;

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

            this.selectBookByInfoCommand = this.connection.CreateCommand();
            this.selectBookByInfoCommand.CommandText = SelectByInfo;
            this.selectBookByInfoCommand.Parameters.Add(":author", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByInfoCommand.Parameters.Add(":title", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByInfoCommand.Parameters.Add(":publisher", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByInfoCommand.Prepare();

            this.selectBookByInfoAndFormatCommand = this.connection.CreateCommand();
            this.selectBookByInfoAndFormatCommand.CommandText = SelectByInfoAndFormat;
            foreach (Microsoft.Data.Sqlite.SqliteParameter parameter in this.selectBookByInfoCommand.Parameters)
            {
                this.selectBookByInfoAndFormatCommand.Parameters.Add(parameter.ParameterName, parameter.SqliteType);
            }

            this.selectBookByInfoAndFormatCommand.Parameters.Add(":extension", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByInfoAndFormatCommand.Prepare();

            this.selectBookByIdCommand = this.connection.CreateCommand();
            this.selectBookByIdCommand.CommandText = SelectById;
            this.selectBookByIdCommand.Parameters.Add(":id", Microsoft.Data.Sqlite.SqliteType.Integer);
            this.selectBookByIdCommand.Parameters.Add(":extension", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByIdCommand.Prepare();

            this.selectBookByUrlCommand = this.connection.CreateCommand();
            this.selectBookByUrlCommand.CommandText = SelectByUrl;
            this.selectBookByUrlCommand.Parameters.Add(":uri", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookByUrlCommand.Prepare();

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
        /// Gets the last-modified for a specified URL.
        /// </summary>
        /// <param name="uri">The URL identifier.</param>
        /// <returns>The last modified date time.</returns>
        public async Task<DateTime?> GetDateTimeAsync(Uri uri)
        {
            if (uri is null)
            {
                return null;
            }

            // get the date time of the format
            this.selectBookByUrlCommand.Parameters[":uri"].Value = uri.ToString();

            using var reader = await this.selectBookByUrlCommand.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return DateTime.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        /// <summary>
        /// Gets the last-modified for a specified URL and format.
        /// </summary>
        /// <param name="uri">The URL identifier.</param>
        /// <param name="extension">The format to check.</param>
        /// <returns>The last modified date time.</returns>
        public async Task<DateTime?> GetDateTimeAsync(Uri uri, string extension)
        {
            if (uri is null)
            {
                return null;
            }

            // get the date time of the format
            this.selectBookByUrlCommand.Parameters[":uri"].Value = uri.ToString();
            (int id, string path, string name, string lastModified) values = default;

            using (var reader = await this.selectBookByUrlCommand.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    values = (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                }
                else
                {
                    return null;
                }
            }

            var fullPath = System.IO.Path.Combine(this.Path, values.path, $"{values.name}{extension}");

            if (System.IO.File.Exists(fullPath))
            {
                var fileInfo = new System.IO.FileInfo(fullPath);
                await this.UpdateLastModifiedAsync(values.id, values.name, fileInfo, values.lastModified).ConfigureAwait(false);
                return fileInfo.LastWriteTimeUtc;
            }

            return null;
        }

        /// <summary>
        /// Updates the EPUB if it exists in the calibre library.
        /// </summary>
        /// <param name="info">The EPUB info.</param>
        /// <param name="found">Set to <see langword="true"/> if the book was already found.</param>
        /// <returns><see langword="true"/> if the EPUB has been updated; otherwise <see langword="false" />.</returns>
        public async Task<bool> UpdateIfExistsAsync(EpubInfo info, bool found)
        {
            int id = default;
            string? path = default;
            string? name = default;
            string? lastModified = default;
            var author = info.Authors.First().Replace(',', '|');
            var publisher = info.Publishers.First().Replace(',', '|');

            this.selectBookByInfoAndFormatCommand.Parameters[":author"].Value = author;
            this.selectBookByInfoAndFormatCommand.Parameters[":title"].Value = info.Title;
            this.selectBookByInfoAndFormatCommand.Parameters[":publisher"].Value = publisher;
            this.selectBookByInfoAndFormatCommand.Parameters[":extension"].Value = info.Extension.ToUpperInvariant();

            using (var reader = await this.selectBookByInfoAndFormatCommand.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    id = reader.GetInt32(0);
                    path = reader.GetString(1);
                    name = reader.GetString(2);
                    lastModified = reader.GetString(3);
                }
            }

            if (id == 0 || path is null || name is null)
            {
                // see if we need to add the book or add the format
                this.selectBookByInfoCommand.Parameters[":author"].Value = author;
                this.selectBookByInfoCommand.Parameters[":title"].Value = info.Title;
                this.selectBookByInfoCommand.Parameters[":publisher"].Value = publisher;

                using (var reader = await this.selectBookByInfoCommand.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    if (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        id = reader.GetInt32(0);
                        path = reader.GetString(1);
                        name = reader.GetString(2);
                        lastModified = reader.GetString(3);
                    }
                }

                if (id == 0)
                {
                    if (found)
                    {
                        // this was previously found
                        this.logger.LogError("Failed to find {Book} by {Author} by data, but found by URI, check data", info.Title, info.Authors.First().Replace(',', '|'));
                        return false;
                    }

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

                    using (var reader = await this.selectBookByInfoCommand.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            id = reader.GetInt32(0);
                            path = reader.GetString(1);
                            name = reader.GetString(2);
                            lastModified = reader.GetString(3);
                        }
                    }

                    if (path != null && name != null && lastModified != null)
                    {
                        this.UpdateLastWriteTime(path, name, info);
                        await this.UpdateLastModifiedAsync(id, name, info.Path, lastModified).ConfigureAwait(false);
                    }
                }
                else if (path != null && name != null && lastModified != null)
                {
                    // add this format
                    this.ExecuteCalibreDbToLogger("add_format", "--dont-replace " + id + " \"" + info.Path + "\"");
                    this.UpdateLastWriteTime(path, name, info);
                    await this.UpdateLastModifiedAsync(id, name, info.Path, lastModified).ConfigureAwait(false);
                }

                return true;
            }
            else
            {
                if (!found)
                {
                    // this was previously found
                    this.logger.LogError("Fount {Book} by {Author} by data, but this was not found by URI, check data", info.Title, info.Authors.First().Replace(',', '|'));
                    return false;
                }

                var fullPath = System.IO.Path.Combine(this.Path, path, $"{name}{System.IO.Path.GetExtension(info.Path)}");

                if (System.IO.File.Exists(fullPath))
                {
                    // see if this has changed at all
                    if (!CheckFiles(info.Path, fullPath, this.logger))
                    {
                        // files are not the same. Copy in the new file
                        this.logger.LogInformation("Replacing {0} as files do not match", name);

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
                    if (lastModified != null)
                    {
                        await this.UpdateLastModifiedAsync(id, name, info.Path, lastModified).ConfigureAwait(false);
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
                    this.selectBookByInfoCommand?.Dispose();
                    this.selectBookByInfoAndFormatCommand?.Dispose();
                    this.selectBookByIdCommand?.Dispose();
                    this.selectBookByUrlCommand?.Dispose();
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