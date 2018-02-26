// -----------------------------------------------------------------------
// <copyright file="CalibreLibrary.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Standard.EBooks.Downloader
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Represents a <see href="https://calibre-ebook.com/">calibre</see> library.
    /// </summary>
    public class CalibreLibrary : IDisposable
    {
        private Microsoft.Data.Sqlite.SqliteConnection connection;

        private bool disposedValue = false; // To detect redundant calls

        /// <summary>
        /// Initializes a new instance of the <see cref="CalibreLibrary" /> class.
        /// </summary>
        /// <param name="path">The path.</param>
        public CalibreLibrary(string path)
        {
            this.Path = path;

            var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(path, "metadata.db") };
            this.connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionStringBuilder.ConnectionString);
            this.connection.Open();
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
        public bool UpdateIfExists(EpubInfo info)
        {
            using (var command = this.connection.CreateCommand())
            {
                command.CommandText = "SELECT b.path, d.name FROM books b INNER JOIN data d ON b.id = d.book INNER JOIN books_publishers_link bpl ON b.id = bpl.book INNER JOIN publishers p ON bpl.publisher = p.id INNER JOIN books_authors_link bal ON b.id = bal.book INNER JOIN authors a ON bal.author = a.id WHERE d.format = :extension AND a.name = :author AND b.title = :title AND p.name = :publisher";
                command.Parameters.AddWithValue(":extension", info.Extension.ToUpperInvariant());
                command.Parameters.AddWithValue(":author", info.Authors.First().Replace(',', '|'));
                command.Parameters.AddWithValue(":title", info.Title);
                command.Parameters.AddWithValue(":publisher", "Standard EBooks");

                string path = null;
                string name = null;
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        path = reader.GetString(0);
                        name = reader.GetString(1);
                    }
                }

                if (path == null || name == null)
                {
                    return false;
                }

                var fullPath = System.IO.Path.Combine(this.Path, path, string.Format("{0}{1}", name, System.IO.Path.GetExtension(info.Path)));

                if (System.IO.File.Exists(fullPath))
                {
                    // see if this has changed at all
                    if (!CheckFiles(info.Path, fullPath))
                    {
                        // files are not the same. Copy in the new file
                        System.IO.File.Copy(info.Path, fullPath, true);
                    }

                    return true;
                }

                return false;
            }
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

        private static bool CheckFiles(string source, string destination)
        {
            var sourceFileInfo = new System.IO.FileInfo(source);
            var destinationFileInfo = new System.IO.FileInfo(destination);

            if (sourceFileInfo.Length != destinationFileInfo.Length)
            {
                return false;
            }

            var sourceHash = GetFileHash(sourceFileInfo.FullName);
            var destinationHash = GetFileHash(destinationFileInfo.FullName);

            if (sourceHash.Length != destinationHash.Length)
            {
                return false;
            }

            for (var i = 0; i < sourceHash.Length; i++)
            {
                if (sourceHash[i] != destinationHash[i])
                {
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