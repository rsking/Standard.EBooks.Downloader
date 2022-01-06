// <copyright file="CommandLine.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

namespace EBook.Downloader;

using System.CommandLine;

/// <summary>
/// Command line arguments, and options.
/// </summary>
internal static class CommandLine
{
    /// <summary>
    /// Gets the library path argument.
    /// </summary>
    public static Argument<DirectoryInfo> LibraryPathArgument { get; } = new Argument<DirectoryInfo>("CALIBRE-LIBRARY-PATH", "The path to the directory containing the calibre library") { Arity = ArgumentArity.ExactlyOne }.ExistingOnly();

    /// <summary>
    /// Gets the use content server argument.
    /// </summary>
    public static Option<bool> UseContentServerOption { get; } = new(new[] { "-s", "--use-content-server" }, "Whether to use the content server or not");
}