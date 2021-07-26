// -----------------------------------------------------------------------
// <copyright file="EpubCollection.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    /// <summary>
    /// The collection record.
    /// </summary>
    /// <param name="Id">The ID.</param>
    /// <param name="Name">The name.</param>
    /// <param name="Type">The collection type.</param>
    /// <param name="Position">The position.</param>
    public record EpubCollection(string Id, string Name, EpubCollectionType Type, int Position);
}
