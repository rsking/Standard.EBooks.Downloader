// <copyright file="AutoMerge.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

namespace EBook.Downloader.Calibre;

/// <summary>
/// The auto-merge enumeration.
/// </summary>
public enum AutoMerge
{
    /// <summary>
    /// Default.
    /// </summary>
    Default,

    /// <summary>
    /// Ignore.
    /// </summary>
    Ignore,

    /// <summary>
    /// Overwrite.
    /// </summary>
    Overwrite,

    /// <summary>
    /// New record.
    /// </summary>
    NewRecord,
}