// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using EBook.Downloader.Calibre;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var calibreLibraryPathArgument = new Argument<DirectoryInfo>("CALIBRE-LIBRARY-PATH") { Description = "The path to the directory containing the calibre library", Arity = ArgumentArity.ExactlyOne }.ExistingOnly();

var tagsCommandBuilder = new CommandBuilder(new Command(nameof(Tags).ToLowerInvariant()) { Handler = CommandHandler.Create<IHost, System.IO.DirectoryInfo, bool>(Tags) })
    .AddArgument(calibreLibraryPathArgument);

var descriptionCommandBuilder = new CommandBuilder(new Command(nameof(Description).ToLowerInvariant()) { Handler = CommandHandler.Create<IHost, System.IO.DirectoryInfo, bool>(Description) })
    .AddArgument(calibreLibraryPathArgument);

var builder = new CommandLineBuilder(new RootCommand("Calibre EBook Maintenence"))
    .AddCommand(tagsCommandBuilder.Command)
    .AddCommand(descriptionCommandBuilder.Command)
    .AddGlobalOption(new Option<bool>(new[] { "-s", "--use-content-server" }, "Whether to use the content server or not"))
    .UseDefaults()
    .UseHost(Host.CreateDefaultBuilder, builder => builder.ConfigureServices(services => services.Configure<InvocationLifetimeOptions>(options => options.SuppressStatusMessages = true)));

return await builder
    .Build()
    .InvokeAsync(args.Select(Environment.ExpandEnvironmentVariables).ToArray())
    .ConfigureAwait(false);

static async Task Tags(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    bool useContentServer)
{
    var calibreDb = new CalibreDb(calibreLibraryPath.FullName, useContentServer: useContentServer, host.Services.GetRequiredService<ILogger<CalibreDb>>());
    await foreach (var category in calibreDb
        .ShowCategoriesAsync()
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
                        if (count == 0)
                        {
                            currentWord.Append(textInfo.ToUpper(letter));
                        }
                        else
                        {
                            currentWord.Append(letter);
                        }

                        count++;
                    }

                    return currentWord.ToString();
                }
            }
        }
    }

    static (string Name, string? Character) Split(string value)
    {
        var index = value.IndexOf('(', StringComparison.Ordinal);
        if (index == -1)
        {
            return (value, default);
        }

        return (value[..(index - 1)], value[index..]);
    }

    static string Join(string name, string? character)
    {
        if (character is null)
        {
            return name;
        }

        return $"{name} {character}";
    }
}

static async Task Description(
    IHost host,
    DirectoryInfo calibreLibraryPath,
    bool useContentServer)
{
    var calibreDb = new CalibreDb(calibreLibraryPath.FullName, useContentServer: useContentServer, host.Services.GetRequiredService<ILogger<CalibreDb>>());
    var json = await calibreDb.ListAsync(new[] { "comments", "title" }).ConfigureAwait(false);
    if (json is null)
    {
        return;
    }

    foreach (var item in json.RootElement.EnumerateArray())
    {
        if (item.TryGetProperty("comments", out var descriptionProperty))
        {
            var description = descriptionProperty.GetString();
            if (string.IsNullOrEmpty(description))
            {
                Console.WriteLine("{0} {1} has an empty description", GetId(item), GetTitle(item));
            }
            else if (description?.Contains("**", StringComparison.Ordinal) == true)
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
