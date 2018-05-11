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
        private const string TriggerName = "books_update_trg";

        private readonly ILogger logger;

        private Microsoft.Data.Sqlite.SqliteConnection connection;

        private Microsoft.Data.Sqlite.SqliteCommand selectCommand;

        private Microsoft.Data.Sqlite.SqliteCommand updateCommand;

        private Microsoft.Data.Sqlite.SqliteCommand dropTriggerCommand;

        private Microsoft.Data.Sqlite.SqliteCommand createTriggerCommand;

        private bool disposedValue = false; // To detect redundant calls

        /// <summary>
        /// Initializes a new instance of the <see cref="CalibreLibrary" /> class.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="logger">The logger.static</param>
        public CalibreLibrary(string path, ILogger logger)
        {
            this.Path = path;
            this.logger = logger;

            var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(path, "metadata.db") };
            this.connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionStringBuilder.ConnectionString);
            this.connection.Open();

            this.selectCommand = this.connection.CreateCommand();
            this.selectCommand.CommandText = "SELECT b.id, b.path, d.name, b.last_modified FROM books b INNER JOIN data d ON b.id = d.book INNER JOIN books_publishers_link bpl ON b.id = bpl.book INNER JOIN publishers p ON bpl.publisher = p.id INNER JOIN books_authors_link bal ON b.id = bal.book INNER JOIN authors a ON bal.author = a.id WHERE d.format = :extension AND a.name = :author AND b.title = :title AND p.name = :publisher";
            this.selectCommand.Parameters.Add(":extension", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectCommand.Parameters.Add(":author", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectCommand.Parameters.Add(":title", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectCommand.Parameters.Add(":publisher", Microsoft.Data.Sqlite.SqliteType.Text);
            this.selectCommand.Prepare();

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
            this.selectCommand.Parameters[":extension"].Value = info.Extension.ToUpperInvariant();
            this.selectCommand.Parameters[":author"].Value = info.Authors.First().Replace(',', '|');
            this.selectCommand.Parameters[":title"].Value = info.Title;
            this.selectCommand.Parameters[":publisher"].Value = "Standard EBooks";

            using (var reader = await this.selectCommand.ExecuteReaderAsync())
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
                return false;
            }

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
                var sourceFileInfo = new System.IO.FileInfo(info.Path);
                var sourceLastWriteTime = sourceFileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz");
                if (sourceLastWriteTime != lastModified)
                {
                    // check this as date time, to be within the same minute, and is the latest date/time
                    var lastModifiedDateTime = DateTime.Parse(lastModified);
                    var difference = lastModifiedDateTime - sourceFileInfo.LastWriteTime;
                    if (Math.Abs(difference.TotalMinutes) > 1D || difference.TotalMinutes > 0)
                    {
                        // write this to the database
                        this.logger.LogInformation("\tUpdating last modified time for {0} in the database ", name);
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

                return true;
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
    }
}