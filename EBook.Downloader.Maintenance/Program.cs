// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using EBook.Downloader.Calibre;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var tagsCommandBuilder = new CommandBuilder(new Command("tags") { Handler = CommandHandler.Create<IHost, System.IO.DirectoryInfo>(Tags) })
    .AddArgument(new Argument<System.IO.DirectoryInfo>("CALIBRE-LIBRARY-PATH") { Description = "The path to the directory containing the calibre library", Arity = ArgumentArity.ExactlyOne }.ExistingOnly());

var builder = new CommandLineBuilder(new RootCommand("Calibre EBook Maintenence"))
    .AddCommand(tagsCommandBuilder.Command)
    .UseDefaults()
    .UseHost();

return await builder
    .Build()
    .InvokeAsync(args.Select(Environment.ExpandEnvironmentVariables).ToArray())
    .ConfigureAwait(false);

static async Task Tags(
    IHost host,
    System.IO.DirectoryInfo calibreLibraryPath)
{
    var calibreDb = new CalibreDb(calibreLibraryPath.FullName, host.Services.GetRequiredService<ILogger<CalibreDb>>());
    await foreach (var category in calibreDb
        .ListCategories()
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
                var words = new System.Collections.Generic.List<string>();

                foreach (var word in strX.Trim().Split(separator).Select(w => w.Trim()))
                {
                    if (words.Count > 0)
                    {
                        if (string.Equals(word, "a", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "for", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "of", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "and", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "in", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "the", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "it", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "it", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "it's", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "as", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "to", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "ca.", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(word, "into", StringComparison.OrdinalIgnoreCase))
                        {
                            words.Add(textInfo.ToLower(word));
                            continue;
                        }
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
                    foreach (char letter in word)
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
        var index = value.IndexOf('(');
        if (index == -1)
        {
            return (value, default);
        }

        return (value.Substring(0, index - 1), value.Substring(index));
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
