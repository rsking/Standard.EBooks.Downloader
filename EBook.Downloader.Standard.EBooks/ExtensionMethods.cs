// -----------------------------------------------------------------------
// <copyright file="ExtensionMethods.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Standard.EBooks
{
    using System.Linq;

    /// <summary>
    /// Extension methods.
    /// </summary>
    internal static class ExtensionMethods
    {
        /// <summary>
        /// Gets the extension for the specified URL.
        /// </summary>
        /// <param name="uri">The URL.</param>
        /// <returns>The file extension.</returns>
        public static string GetExtension(this System.Uri uri) => System.IO.Path.GetExtension(GetFileName(uri));

        /// <summary>
        /// Gets the file name for the specified URL.
        /// </summary>
        /// <param name="uri">The URL.</param>
        /// <returns>The file name.</returns>
        public static string GetFileName(this System.Uri uri)
        {
            // create the file name
            var fileName = uri.Segments.Last();

            // check to see if this is a kepub
            if (fileName.EndsWith(".kepub.epub", System.StringComparison.OrdinalIgnoreCase))
            {
                return System.IO.Path.GetFileNameWithoutExtension(fileName);
            }
            else if (fileName.EndsWith(".epub3", System.StringComparison.OrdinalIgnoreCase))
            {
                return System.IO.Path.ChangeExtension(fileName, ".epub");
            }

            return fileName;
        }
    }
}