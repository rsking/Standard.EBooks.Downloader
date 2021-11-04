// <copyright file="Category.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

namespace EBook.Downloader.Calibre;

/// <summary>
/// Represents a category.
/// </summary>
public record class Category(CategoryType CategoryType, string TagName, int Count, float Rating);
