// -----------------------------------------------------------------------
// <copyright file="CalibreBook.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    /// <summary>
    /// EPUB information.
    /// </summary>
    public record CalibreBook
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="CalibreBook" /> class.
        /// </summary>
        /// <param name="id">The book ID.</param>
        /// <param name="name">The book name.</param>
        /// <param name="path">The book path.</param>
        /// <param name="lastModified">The book last modified date.</param>
        public CalibreBook(int id, string name, string path, string lastModified)
            : this(id, name, path, System.DateTime.Parse(lastModified, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal))
        {
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="CalibreBook" /> class.
        /// </summary>
        /// <param name="id">The book ID.</param>
        /// <param name="name">The book name.</param>
        /// <param name="path">The book path.</param>
        /// <param name="lastModified">The book last modified date.</param>
        public CalibreBook(int id, string name, string path, System.DateTime lastModified) => (this.Id, this.Name, this.Path, this.LastModified) = (id, name, path, lastModified);

        /// <summary>
        /// Gets the ID.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets the Name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the Path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the last modified time.
        /// </summary>
        public System.DateTime LastModified { get; }

        /// <summary>
        /// Gets the file info.
        /// </summary>
        /// <param name="path">The base path.</param>
        /// <param name="extension">The extension.</param>
        /// <returns>The file info for the book.</returns>
        public string GetFullPath(string path, string extension) => System.IO.Path.Combine(path, this.Path, $"{this.Name}{extension}");
    }
}