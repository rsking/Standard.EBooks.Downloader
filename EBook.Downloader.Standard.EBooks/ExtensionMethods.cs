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
    private static readonly Dictionary<(Type Key, Type), System.Reflection.MethodInfo> GetGroupings = [];

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

    /// <summary>
    /// Tries to get the values from the lookup.
    /// </summary>
    /// <typeparam name="TKey">The type of key.</typeparam>
    /// <typeparam name="TElement">The type of element.</typeparam>
    /// <param name="source">The source.</param>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>The result.</returns>
    public static bool TryGetValues<TKey, TElement>(this ILookup<TKey, TElement> source, TKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumerable<TElement>? values)
    {
        var method = GetGrouping(typeof(TKey), typeof(TElement));

        var grouping = method.Invoke(source, [key, false]) as IGrouping<TKey, TElement>;
        if (grouping is not null)
        {
            values = grouping;
            return true;
        }

        values = default;
        return false;
    }

    private static System.Reflection.MethodInfo GetGrouping(Type key, Type element)
    {
        var groupingKey = (key, element);
        if (GetGroupings.TryGetValue(groupingKey, out var method))
        {
            return method;
        }

        method = GetGrouping(groupingKey);
        GetGroupings.Add(groupingKey, method);
        return method;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields", Justification = "Checked")]
        static System.Reflection.MethodInfo GetGrouping((Type Key, Type Element) groupingKey)
        {
            var type = typeof(Lookup<,>);
            type = type.MakeGenericType(groupingKey.Key, groupingKey.Element);
            return type.GetMethod(nameof(GetGrouping), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        }
    }
}