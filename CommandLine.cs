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
    public static CliArgument<DirectoryInfo> LibraryPathArgument { get; } = new CliArgument<DirectoryInfo>("CALIBRE-LIBRARY-PATH") { Description = "The path to the directory containing the calibre library", Arity = ArgumentArity.ExactlyOne }.AcceptExistingOnly();

    /// <summary>
    /// Gets the use content server argument.
    /// </summary>
    public static CliOption<bool> UseContentServerOption { get; } = new("-s", "--use-content-server") { Description = "Whether to use the content server or not", Recursive = true };
}