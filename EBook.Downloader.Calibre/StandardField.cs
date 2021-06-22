// <copyright file="StandardField.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

namespace EBook.Downloader.Calibre
{
    /// <summary>
    /// Standard fields.
    /// </summary>
    public enum StandardField
    {
        /// <summary>
        /// Author sort.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "author_sort")]
        AuthorSort,

        /// <summary>
        /// Authors
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "authors")]
        Authors,

        /// <summary>
        /// Comments.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "comments")]
        Comments,

        /// <summary>
        /// Cover.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "cover")]
        Cover,

        /// <summary>
        /// Identifiers.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "identifiers")]
        Identifiers,

        /// <summary>
        /// Languages.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "languages")]
        Languages,

        /// <summary>
        /// Published.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "pubdate")]
        Published,

        /// <summary>
        /// Publisher.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "publisher")]
        Publisher,

        /// <summary>
        /// Rating.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "rating")]
        Rating,

        /// <summary>
        /// Series.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "series")]
        Series,

        /// <summary>
        /// Series Index.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "series_index")]
        SeriesIndex,

        /// <summary>
        /// Size.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "size")]
        Size,

        /// <summary>
        /// Title Sort.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "sort")]
        TitleSort,

        /// <summary>
        /// Tags.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "tags")]
        Tags,

        /// <summary>
        /// Date.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "timestamp")]
        Date,

        /// <summary>
        /// Title.
        /// </summary>
        [System.Runtime.Serialization.EnumMember(Value = "title")]
        Title,
    }
}
