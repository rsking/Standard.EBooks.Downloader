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
    /// Sets the arity of the option.
    /// </summary>
    /// <typeparam name="T">The type of option.</typeparam>
    /// <param name="option">The option.</param>
    /// <param name="arity">The arity.</param>
    /// <returns>The option for chaining.</returns>
    public static T WithArity<T>(this T option, System.CommandLine.IArgumentArity arity)
        where T : System.CommandLine.IOption
    {
        if (option.Argument is System.CommandLine.Argument argument)
        {
            argument.Arity = arity;
            return option;
        }

        throw new ArgumentException("option.Argument must be an Argument", nameof(option));
    }
}
