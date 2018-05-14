// -----------------------------------------------------------------------
// <copyright file="CalibreLibrary.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Represents a <see href="https://calibre-ebook.com/">calibre</see> library.
    /// </summary>
    public class CalibreLibrary : IDisposable
    {
        private const string DefaultCalibrePath = "C:\\Program Files\\Calibre2";

        private const string TriggerName = "books_update_trg";

        private readonly ILogger logger;

        private readonly string calibreDbPath;

        private Microsoft.Data.Sqlite.SqliteConnection connection;

        private Microsoft.Data.Sqlite.SqliteCommand selectBookWithFormatCommand;

        private Microsoft.Data.Sqlite.SqliteCommand selectBookWithoutFormatCommand;

        private Microsoft.Data.Sqlite.SqliteCommand updateCommand;

        private Microsoft.Data.Sqlite.SqliteCommand dropTriggerCommand;

        private Microsoft.Data.Sqlite.SqliteCommand createTriggerCommand;

        private bool disposedValue = false; // To detect redundant calls

        /// <summary>
        /// Initializes a new instance of the <see cref="CalibreLibrary" /> class.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="calibrePath">The path to the calibre binaries.</param>
        public CalibreLibrary(string path, ILogger logger, string calibrePath = DefaultCalibrePath)
        {
            this.Path = path;
            this.logger = logger;
            this.calibreDbPath = System.IO.Path.Combine(calibrePath, "calibredb.exe");

            var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(path, "metadata.db"), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite };
            this.connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionStringBuilder.ConnectionString);
            this.connection.Open();

            this.selectBookWithoutFormatCommand = this.connection.CreateCommand();
            this.selectBookWithoutFormatCommand.CommandText = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book INNER JOIN books_publishers_link bpl ON b.id = bpl.book INNER JOIN publishers p ON bpl.publisher = p.id INNER JOIN books_authors_link bal ON b.id = bal.book INNER JOIN authors a ON bal.author = a.id WHERE a.name = :author AND b.title = :title AND p.name = :publisher";
            this.selectBookWithoutFormatCommand.Parameters.Add(":author", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookWithoutFormatCommand.Parameters.Add(":title", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookWithoutFormatCommand.Parameters.Add(":publisher", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookWithoutFormatCommand.Prepare();

            this.selectBookWithFormatCommand = this.connection.CreateCommand();
            this.selectBookWithFormatCommand.CommandText = this.selectBookWithoutFormatCommand.CommandText + " AND d.format = :extension";
            foreach (Microsoft.Data.Sqlite.SqliteParameter parameter in this.selectBookWithoutFormatCommand.Parameters)
            {
                this.selectBookWithFormatCommand.Parameters.Add(parameter.ParameterName, parameter.SqliteType);
            }

            this.selectBookWithFormatCommand.Parameters.Add(":extension", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectBookWithFormatCommand.Prepare();

            this.updateCommand = this.connection.CreateCommand();
            this.updateCommand.CommandText = "UPDATE books SET last_modified = :lastModified WHERE id = :id";
            this.updateCommand.Parameters.Add(":lastModified", Microsoft.Data.Sqlite.SqliteType.Text);
            this.updateCommand.Parameters.AddWithValue(":id", Microsoft.Data.Sqlite.SqliteType.Integer);

            string createTriggerCommandText = default;
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
        public string Path { get; private set; }

        /// <summary>
        /// Updates the EPUB if it exists in the calibre library.
        /// </summary>
        /// <param name="info">The EPUB info.</param>
        /// <returns><see langword="true"/> if the EPUB has been updated; otherwise <see langword="false" /></returns>
        public async Task<bool> UpdateIfExistsAsync(EpubInfo info)
        {
            int id = default;
            string path = default;
            string name = default;
            string lastModified = default;
            
            this.selectBookWithFormatCommand.Parameters[":author"].Value = info.Authors.First().Replace(',', '|');
            this.selectBookWithFormatCommand.Parameters[":title"].Value = info.Title;
            this.selectBookWithFormatCommand.Parameters[":publisher"].Value = info.Publisher;
            this.selectBookWithFormatCommand.Parameters[":extension"].Value = info.Extension.ToUpperInvariant();

            using (var reader = await this.selectBookWithFormatCommand.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    id = reader.GetInt32(0);
                    path = reader.GetString(1);
                    name = reader.GetString(2);
                    lastModified = reader.GetString(3);
                }
            }

            if (id == 0 || path == null || name == null)
            {
                // see if we need to add the book or add the format
                this.selectBookWithoutFormatCommand.Parameters[":author"].Value = info.Authors.First().Replace(',', '|');
                this.selectBookWithoutFormatCommand.Parameters[":title"].Value = info.Title;
                this.selectBookWithoutFormatCommand.Parameters[":publisher"].Value = info.Publisher;

                using (var reader = await this.selectBookWithoutFormatCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        id = reader.GetInt32(0);
                        path = reader.GetString(1);
                        name = reader.GetString(2);
                        lastModified = reader.GetString(3);
                    }
                }

                if (id == 0)
                {
                    // we need to add this
                    this.ExecuteCalibreDb("add", "--duplicates --languages eng \"" + info.Path + "\"");
                    using (var reader = await this.selectBookWithoutFormatCommand.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            id = reader.GetInt32(0);
                            path = reader.GetString(1);
                            name = reader.GetString(2);
                            lastModified = reader.GetString(3);
                        }
                    }

                    this.UpdateLastWriteTime(path, name, info);
                    await this.UpdateLastModifiedAsync(id, name, info.Path, lastModified);
                }
                else
                {
                    // add this format
                    this.ExecuteCalibreDb("add_format", "--dont-replace " + id + " \"" + info.Path + "\"");
                    this.UpdateLastWriteTime(path, name, info);
                    await this.UpdateLastModifiedAsync(id, name, info.Path, lastModified);
                }

                return true;
            }
            else
            {
                var fullPath = System.IO.Path.Combine(this.Path, path, string.Format("{0}{1}", name, System.IO.Path.GetExtension(info.Path)));

                if (System.IO.File.Exists(fullPath))
                {
                    // see if this has changed at all
                    if (!CheckFiles(info.Path, fullPath, this.logger))
                    {
                        // files are not the same. Copy in the new file
                        this.logger.LogInformation("\tReplacing {0} as files do not match", name);
                        System.IO.File.Copy(info.Path, fullPath, true);
                    }

                    // see if we need to update the last modified time
                    await this.UpdateLastModifiedAsync(id, name, info.Path, lastModified);

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

            // TODO: uncomment the following line if the finalizer is overridden below.
            //// GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the unmanaged resource, and optionally managed resources, for this instance.
        /// </summary>
        /// <param name="disposing">Set to <see landword="true"/> to dispose managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    if (this.connection != null)
                    {
                        this.connection.Dispose();
                    }

                    this.connection = null;
                }

                this.disposedValue = true;
            }
        }

        //// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        //// ~CalibreLibrary() {
        ////   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        ////   Dispose(false);
        //// }

        private static bool CheckFiles(string source, string destination, ILogger logger)
        {
            var sourceFileInfo = new System.IO.FileInfo(source);
            var destinationFileInfo = new System.IO.FileInfo(destination);

            if (sourceFileInfo.LastWriteTime != destinationFileInfo.LastWriteTime)
            {
                logger.LogInformation("\tsource and destination have different modified dates");
                return false;
            }

            if (sourceFileInfo.Length != destinationFileInfo.Length)
            {
                logger.LogInformation("\tsource and destination are different lengths");
                return false;
            }

            var sourceHash = GetFileHash(sourceFileInfo.FullName);
            var destinationHash = GetFileHash(destinationFileInfo.FullName);

            if (sourceHash.Length != destinationHash.Length)
            {
                logger.LogInformation("\tsource and destination hashes are different lengths");
                return false;
            }

            for (var i = 0; i < sourceHash.Length; i++)
            {
                if (sourceHash[i] != destinationHash[i])
                {
                    logger.LogInformation("\tsource and destination hashes do not match");
                    return false;
                }
            }

            return true;
        }

        private static byte[] GetFileHash(string fileName)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                using (var stream = System.IO.File.OpenRead(fileName))
                {
                    return md5.ComputeHash(stream);
                }
            }
        }

        private int? GetAuthorId(string author)
        {
            using (var command = this.connection.CreateCommand())
            {
                command.CommandText = "SELECT id FROM authors WHERE name = :name";
                var parameter = command.Parameters.AddWithValue(":name", author);
                parameter.DbType = System.Data.DbType.String;

                var value = command.ExecuteScalar();
                if (value == DBNull.Value)
                {
                    return null;
                }

                return (int)value;
            }
        }

        private async Task UpdateLastModifiedAsync(int id, string name, string path, string lastModified)
        {
            var sourceFileInfo = new System.IO.FileInfo(path);
            var sourceLastWriteTime = sourceFileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz");
            if (sourceLastWriteTime != lastModified)
            {
                // check this as date time, to be within the same minute, and is the latest date/time
                var lastModifiedDateTime = DateTime.Parse(lastModified);
                var difference = sourceFileInfo.LastWriteTime - lastModifiedDateTime;
                if (Math.Abs(difference.TotalMinutes) > 1D || difference.TotalMinutes > 0)
                {
                    // write this to the database
                    this.logger.LogInformation("\tUpdating last modified time for {0} in the database to {1}", name, sourceLastWriteTime);
                    this.updateCommand.Parameters[":lastModified"].Value = sourceLastWriteTime;
                    this.updateCommand.Parameters[":id"].Value = id;

                    if (this.dropTriggerCommand != null)
                    {
                        await this.dropTriggerCommand.ExecuteNonQueryAsync();
                    }

                    await this.updateCommand.ExecuteNonQueryAsync();

                    if (this.createTriggerCommand != null)
                    {
                        await this.createTriggerCommand.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        private void UpdateLastWriteTime(string path, string name, EpubInfo info)
        {
            var fullPath = System.IO.Path.Combine(this.Path, path, string.Format("{0}{1}", name, System.IO.Path.GetExtension(info.Path)));
            if (System.IO.File.Exists(fullPath))
            {
                var fileSystemInfo = new System.IO.FileInfo(info.Path);
                var dateTime = fileSystemInfo.LastWriteTime;

                fileSystemInfo = new System.IO.FileInfo(fullPath);
                fileSystemInfo.LastWriteTime = dateTime;
            }
        }

        private void ExecuteCalibreDb(string command, string arguments = "")
        {
            try
            {
                var fullArguments = command + " --library-path \"" + this.Path + "\" " + arguments;

                var processStartInfo = new System.Diagnostics.ProcessStartInfo(this.calibreDbPath, fullArguments);
                processStartInfo.UseShellExecute = false;
                processStartInfo.RedirectStandardOutput = true;
                processStartInfo.RedirectStandardError = true;

                var process = new System.Diagnostics.Process();
                process.StartInfo = processStartInfo;
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args?.Data != null)
                    {
                        this.logger.LogInformation(0, args.Data);
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args?.Data != null)
                    {
                        this.logger.LogError(0, args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();
            }
            catch (System.Exception ex)
            {
                this.logger.LogError(0, ex, "Failed to run calibredb.exe");
            }
        }
    }
}