// -----------------------------------------------------------------------
// <copyright file="Words.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Maintenance;

using System.Collections.Generic;

/// <summary>
/// Class for words.
/// </summary>
internal static class Words
{
    /// <summary>
    /// Words that should be lower case.
    /// </summary>
    public static readonly IEnumerable<string> LowerCase = new[]
    {
            "a",
            "for",
            "of",
            "on",
            "and",
            "in",
            "the",
            "it",
            "it",
            "it's",
            "as",
            "to",
            "ca.",
            "into",
        };
}
