// <copyright file="ExtensionMethods.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

namespace EBook.Downloader.Calibre
{
    using System;
    using System.Text;

    /// <summary>
    /// Extension methods.
    /// </summary>
    internal static class ExtensionMethods
    {
        /// <summary>
        /// Calls <see cref="StringBuilder.Append(string)"/> if <paramref name="condition"/> is met.
        /// </summary>
        /// <param name="stringBuilder">The string builder.</param>
        /// <param name="condition">The predicate.</param>
        /// <param name="generator">The value generator.</param>
        /// <returns>Returns <paramref name="stringBuilder"/>.</returns>
        public static StringBuilder AppendIf(this StringBuilder stringBuilder, bool condition, Func<string?> generator) => condition ? stringBuilder.Append(generator()) : stringBuilder;

        /// <summary>
        /// Calls <see cref="StringBuilder.Append(string)"/> if <paramref name="condition"/> is met.
        /// </summary>
        /// <param name="stringBuilder">The string builder.</param>
        /// <param name="condition">The predicate.</param>
        /// <param name="value">The value to add.</param>
        /// <returns>Returns <paramref name="stringBuilder"/>.</returns>
        public static StringBuilder AppendIf(this StringBuilder stringBuilder, bool condition, string? value) => condition ? stringBuilder.Append(value) : stringBuilder;

        /// <summary>
        /// Calls <see cref="StringBuilder.AppendFormat(IFormatProvider, string?, object)"/> if <paramref name="condition"/> is met.
        /// </summary>
        /// <param name="stringBuilder">The string builder.</param>
        /// <param name="condition">The predicate.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="format">A composite format string.</param>
        /// <param name="arg0">The object to format.</param>
        /// <returns>Returns <paramref name="stringBuilder"/>.</returns>
        public static StringBuilder AppendFormatIf(this StringBuilder stringBuilder, bool condition, IFormatProvider provider, string format, object? arg0) => condition
            ? stringBuilder.AppendFormat(provider, format, arg0)
            : stringBuilder;

        /// <summary>
        /// Calls <see cref="StringBuilder.AppendFormat(IFormatProvider, string?, object)"/> if <paramref name="condition"/> is met.
        /// </summary>
        /// <param name="stringBuilder">The string builder.</param>
        /// <param name="condition">The predicate.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="format">A composite format string.</param>
        /// <param name="args">An array of objects to format.</param>
        /// <returns>Returns <paramref name="stringBuilder"/>.</returns>
        public static StringBuilder AppendFormatIf(this StringBuilder stringBuilder, bool condition, IFormatProvider provider, string format, params object?[] args) => condition
            ? stringBuilder.AppendFormat(provider, format, args)
            : stringBuilder;
    }
}
