﻿// <copyright file="Identifier.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

namespace EBook.Downloader.Calibre;

/// <summary>
/// A calibre identifier.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Value">The value.</param>
public record class Identifier(string Name, object Value)
{
    /// <inheritdoc/>
    public override string ToString() => FormattableString.Invariant($"{this.Name}:{this.Value}");
}