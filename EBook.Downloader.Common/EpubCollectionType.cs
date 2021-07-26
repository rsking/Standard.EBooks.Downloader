// -----------------------------------------------------------------------
// <copyright file="EpubCollectionType.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    /// <summary>
    /// The collection type enum.
    /// </summary>
    public enum EpubCollectionType
    {
        /// <summary>
        /// No type.
        /// </summary>
        None = 0,

        /// <summary>
        /// This belongs to a set.
        /// </summary>
        Set = 1,

        /// <summary>
        /// This belongs to a series.
        /// </summary>
        Series = 2,
    }
}
