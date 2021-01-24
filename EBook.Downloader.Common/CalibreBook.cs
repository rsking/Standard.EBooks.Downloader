// -----------------------------------------------------------------------
// <copyright file="CalibreBook.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    using System.Linq;

    /// <summary>
    /// EPUB information.
    /// </summary>
    public record CalibreBook
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="CalibreBook"/> class.
        /// </summary>
        /// <param name="element">The element.</param>
        public CalibreBook(System.Text.Json.JsonElement element)
        {
            this.Id = element.GetProperty("id").GetInt32();
            var file = new System.IO.FileInfo(element.GetProperty("formats").EnumerateArray().First().GetString());
            this.Name = file.Name.Substring(0, file.Name.Length - file.Extension.Length);
            this.Path = $"{file.Directory.Parent.Name}{System.IO.Path.AltDirectorySeparatorChar}{file.Directory.Name}";
            this.LastModified = element.GetProperty("last_modified").GetDateTime().ToUniversalTime();
        }

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
        public string GetFullPath(string path, string extension)
        {
            var paths = GetEnumerable(path).Concat(GetPathsSegments(this.Path)).Concat(GetEnumerable(this.Name + extension));
            return System.IO.Path.Combine(paths.ToArray());

            static System.Collections.Generic.IEnumerable<string> GetEnumerable(string value)
            {
                yield return value;
            }

            static System.Collections.Generic.IEnumerable<string> GetPathsSegments(string path)
            {
                return path.Split(System.IO.Path.AltDirectorySeparatorChar);
            }
        }
    }
}