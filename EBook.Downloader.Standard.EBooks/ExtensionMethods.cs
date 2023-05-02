// -----------------------------------------------------------------------
// <copyright file="ExtensionMethods.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Standard.EBooks;

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
    public static string GetExtension(this Uri uri) => Path.GetExtension(GetFileName(uri));

    /// <summary>
    /// Gets the file name for the specified URL.
    /// </summary>
    /// <param name="uri">The URL.</param>
    /// <returns>The file name.</returns>
    public static string GetFileName(this Uri uri) => uri.Segments[^1] switch
    {
        string value when value.EndsWith(".kepub.epub", StringComparison.OrdinalIgnoreCase) => Path.GetFileNameWithoutExtension(value),
        string value when value.EndsWith(".epub3", StringComparison.OrdinalIgnoreCase) => Path.ChangeExtension(value, ".epub"),
        string value => value,
    };

    /// <summary>
    /// Throws if the specified value is null.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The non-null value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> was <see langword="null"/>.</exception>
    public static T ThrowIfNull<T>(this T? value)
        where T : class => value ?? throw new ArgumentNullException(nameof(value));
}