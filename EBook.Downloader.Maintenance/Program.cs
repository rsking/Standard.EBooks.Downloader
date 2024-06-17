// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Hosting;
using EBook.Downloader.Calibre;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var tagsCommand = new CliCommand(nameof(Tags).ToLowerInvariant())
{
    EBook.Downloader.CommandLine.LibraryPathArgument,
};

tagsCommand.SetAction((parseResult, cancellationToken) => Tags(parseResult.GetHost(), parseResult.GetValue(EBook.Downloader.CommandLine.LibraryPathArgument)!, parseResult.GetValue(EBook.Downloader.CommandLine.UseContentServerOption), cancellationToken));

var descriptionCommand = new CliCommand(nameof(Description).ToLowerInvariant())
{
    EBook.Downloader.CommandLine.LibraryPathArgument,
};

descriptionCommand.SetAction((parseResult, cancellationToken) => Description(parseResult.GetHost(), parseResult.GetValue(EBook.Downloader.CommandLine.LibraryPathArgument)!, parseResult.GetValue(EBook.Downloader.CommandLine.UseContentServerOption), cancellationToken));

var rootCommand = new CliRootCommand("Calibre EBook Maintenence")
{
    tagsCommand,
    descriptionCommand,
    EBook.Downloader.CommandLine.UseContentServerOption,
};

var configuration = new CliConfiguration(rootCommand)
    .UseHost(Host.CreateDefaultBuilder, builder => builder.ConfigureServices(services => services.Configure<InvocationLifetimeOptions>(options => options.SuppressStatusMessages = true)));

return await configuration
    .InvokeAsync(args.Select(Environment.ExpandEnvironmentVariables).ToArray())
    .ConfigureAwait(false);

static async Task Tags(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    bool useContentServer,
    CancellationToken cancellationToken)
{
    var calibreDb = new CalibreDb(calibreLibraryPath.FullName, useContentServer: useContentServer, host.Services.GetRequiredService<ILogger<CalibreDb>>());
    await foreach (var category in calibreDb
        .ShowCategoriesAsync(cancellationToken)
        .Where(category => category.CategoryType == CategoryType.Tags)
        .ConfigureAwait(false))
    {
        var (name, character) = Split(category.TagName);

        var tagName = Join(
            ToProperCase(name).Replace(" and ", " & ", StringComparison.OrdinalIgnoreCase),
            character);
        if (!string.Equals(tagName, category.TagName, StringComparison.Ordinal))
        {
            Console.WriteLine("Incorrect tag name, is {0}, should be {1}", category.TagName, tagName);
        }

        static string ToProperCase(string strX)
        {
            return ToProperCaseImpl(strX, ' ', System.Globalization.CultureInfo.CurrentCulture.TextInfo);

            static string ToProperCaseImpl(string strX, char separator, System.Globalization.TextInfo textInfo)
            {
                var words = new List<string>();
                var split = strX.Trim().Split(separator);
                const int first = 0;
                var last = split.Length - 1;

                foreach (var (word, index) in split.Select((w, i) => (word: w.Trim(), index: i)))
                {
                    if (index != first
                        && index != last
                        && EBook.Downloader.Maintenance.Words.LowerCase.Contains(word, StringComparer.OrdinalIgnoreCase))
                    {
                        words.Add(textInfo.ToLower(word));
                        continue;
                    }

                    if (word.Contains('-', StringComparison.Ordinal))
                    {
                        words.Add(ToProperCaseImpl(word, '-', textInfo));
                    }
                    else
                    {
                        words.Add(ProcessWord(word, textInfo));
                    }
                }

                return string.Join(separator, words);

                static string ProcessWord(string word, System.Globalization.TextInfo textInfo)
                {
                    var count = 0;
                    var currentWord = new System.Text.StringBuilder();
                    foreach (var letter in word)
                    {
                        _ = count == 0 ? currentWord.Append(textInfo.ToUpper(letter)) : currentWord.Append(letter);

                        count++;
                    }

                    return currentWord.ToString();
                }
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2589:Boolean expressions should not be gratuitous", Justification = "False positive")]
    static (string Name, string? Character) Split(string value)
    {
        return value.IndexOf('(', StringComparison.Ordinal) switch
        {
            -1 => (value, default),
            (int index) => (value[..(index - 1)], value[index..]),
        };
    }

    static string Join(string name, string? character)
    {
        return character is null
            ? name
            : $"{name} {character}";
    }
}

static async Task Description(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    bool useContentServer,
    CancellationToken cancellationToken)
{
    var calibreDb = new CalibreDb(calibreLibraryPath.FullName, useContentServer: useContentServer, host.Services.GetRequiredService<ILogger<CalibreDb>>());
    if (await calibreDb.ListAsync(ListDescriptionArguments, cancellationToken: cancellationToken).ConfigureAwait(false) is { } json)
    {
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("comments", out var descriptionProperty))
            {
                var description = descriptionProperty.GetString();
                if (string.IsNullOrEmpty(description))
                {
                    Console.WriteLine("{0} {1} has an empty description", GetId(item), GetTitle(item));
                }
                else if (description.Contains("**", StringComparison.Ordinal))
                {
                    Console.WriteLine("{0} {1} has '**' in the description", GetId(item), GetTitle(item));
                }
            }
            else
            {
                Console.WriteLine("{0} {1} has no description", GetId(item), GetTitle(item));
            }

            static int GetId(System.Text.Json.JsonElement item)
            {
                return item.GetProperty("id").GetInt32();
            }

            static string? GetTitle(System.Text.Json.JsonElement item)
            {
                return item.GetProperty("title").GetString();
            }
        }
    }
}

/// <content>
/// Members for the program.
/// </content>
internal sealed partial class Program
{
    private static readonly string[] ListDescriptionArguments = ["comments", "title"];

    private Program()
    {
    }
}