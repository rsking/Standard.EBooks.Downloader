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
        /// <param name="lastModified">The book last modified date.</param>
        public CalibreBook(string lastModified) => this.LastModified = System.DateTime.Parse(lastModified, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);

        /// <summary>
        /// Gets the ID.
        /// </summary>
        public int Id { get; init; }

        /// <summary>
        /// Gets the Name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the Path.
        /// </summary>
        public string Path { get; init; } = string.Empty;

        /// <summary>
        /// Gets the last modified time.
        /// </summary>
        public System.DateTime LastModified { get; init; }

        /// <summary>
        /// Gets the file info.
        /// </summary>
        /// <param name="path">The base path.</param>
        /// <param name="extension">The extension.</param>
        /// <returns>The file info for the book.</returns>
        public string GetFullPath(string path, string extension) => System.IO.Path.Combine(path, this.Path, $"{this.Name}{extension}");
    }
}