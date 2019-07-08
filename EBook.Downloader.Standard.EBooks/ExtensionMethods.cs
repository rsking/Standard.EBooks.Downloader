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
        public static string GetFileName(this System.Uri uri) => uri.Segments.Last() switch
        {
            string value when value.EndsWith(".kepub.epub", System.StringComparison.OrdinalIgnoreCase) => System.IO.Path.GetFileNameWithoutExtension(value),
            string value when value.EndsWith(".epub3", System.StringComparison.OrdinalIgnoreCase) => System.IO.Path.ChangeExtension(value, ".epub"),
            string value => value,
        };
    }
}