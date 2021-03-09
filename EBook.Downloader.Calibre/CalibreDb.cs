// <copyright file="CalibreDb.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>

namespace EBook.Downloader.Calibre
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Represents the calibredb process.
    /// </summary>
    public class CalibreDb
    {
        /// <summary>
        /// The default calibre path.
        /// </summary>
        public const string DefaultCalibrePath = "%PROGRAMFILES%\\Calibre2";

        private const char QuoteChar = '\"';

        private const string QuoteString = "\"";

        private const string EscapedQuoteString = QuoteString + QuoteString;

        private const string DefaultSeparator = " ";

        private const int DefaultLineWidth = -1;

        private const string DefaultSortBy = "id";

        private const int DefaultLimit = int.MaxValue;

        private const string IntegrationStatus = "Integration status";

        private static readonly string IntegrationStatusTrue = IntegrationStatus + ": " + bool.TrueString;

        private static readonly string IntegrationStatusFalse = IntegrationStatus + ": " + bool.FalseString;

        private readonly ILogger logger;

        private readonly string calibreDbPath;

        /// <summary>
        /// Initialises a new instance of the <see cref="CalibreDb"/> class.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="calibrePath">The calibre path.</param>
        public CalibreDb(string path, ILogger logger, string calibrePath = DefaultCalibrePath)
        {
            this.Path = path;
            this.logger = logger;
            this.calibreDbPath = Environment.ExpandEnvironmentVariables(System.IO.Path.Combine(calibrePath, "calibredb.exe"));
        }

        /// <summary>
        /// Gets the path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Performs the 'list' function.
        /// </summary>
        /// <param name="fields">The fields to display when listing books in the database. Should be a comma separated list of fields. Available fields: author_sort, authors, comments, cover, formats, identifiers, isbn, languages, last_modified, pubdate, publisher, rating, series, series_index, size, tags, timestamp, title, uuid Default: title,authors. The special field "all" can be used to select all fields. In addition to the builtin fields above, custom fields are also available as *field_name, for example, for a custom field #rating, use the name: *rating.</param>
        /// <param name="sortBy">The field by which to sort the results. Available fields: author_sort, authors, comments, cover, formats, identifiers, isbn, languages, last_modified, pubdate, publisher, rating, series, series_index, size, tags, timestamp, title, uuid Default: id.</param>
        /// <param name="ascending">Sort results in ascending order.</param>
        /// <param name="searchPattern">Filter the results by the search query. For the format of the search query, please see the search related documentation in the User Manual. Default is to do no filtering.</param>
        /// <param name="lineWidth">The maximum width of a single line in the output. Defaults to detecting screen size.</param>
        /// <param name="separator">The string used to separate fields. Default is a space.</param>
        /// <param name="prefix">The prefix for all file paths. Default is the absolute path to the library folder.</param>
        /// <param name="limit">The maximum number of results to display. Default: all.</param>
        /// <returns>The JSON document.</returns>
        public async System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ListAsync(IEnumerable<string>? fields = default, string sortBy = DefaultSortBy, bool ascending = default, string? searchPattern = default, int lineWidth = DefaultLineWidth, string? separator = DefaultSeparator, string? prefix = default, int limit = DefaultLimit)
        {
            var stringBuilder = new StringBuilder();
            var fieldsValue = fields is null ? string.Empty : string.Join(",", fields);
            stringBuilder.AppendFormatIf(!string.IsNullOrEmpty(fieldsValue), System.Globalization.CultureInfo.InvariantCulture, " --fields={0}", fieldsValue)
                .AppendFormatIf(!string.Equals(sortBy, DefaultSortBy, StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture, " --sort-by={0}", sortBy)
                .AppendIf(ascending, " --ascending")
                .AppendFormatIf(searchPattern is not null, System.Globalization.CultureInfo.InvariantCulture, " --search={0}", QuoteIfRequired(searchPattern))
                .AppendFormatIf(lineWidth != DefaultLineWidth, System.Globalization.CultureInfo.InvariantCulture, " --line-width={0}", lineWidth)
                .AppendFormatIf(!string.Equals(separator, DefaultSeparator, StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture, " --separator={0}", separator)
                .AppendFormatIf(prefix is not null, System.Globalization.CultureInfo.InvariantCulture, " --prefix={0}", prefix)
                .AppendFormatIf(limit != DefaultLimit, System.Globalization.CultureInfo.InvariantCulture, " --limit={0}", limit)
                .Append(" --for-machine");
            var command = stringBuilder.ToString();
            stringBuilder.Length = 0;

            await this.ExecuteCalibreDbAsync("list", command, data =>
            {
                if (Preprocess(data) is string value)
                {
                    stringBuilder.Append(value);
                }
            }).ConfigureAwait(false);

            return System.Text.Json.JsonDocument.Parse(stringBuilder.ToString());
        }

        /// <summary>
        /// Adds an empty book record.
        /// </summary>
        /// <returns>The book ID.</returns>
        public async System.Threading.Tasks.Task<int> AddEmptyAsync() => (await this.Add(Enumerable.Empty<System.IO.FileInfo>(), empty: true).ConfigureAwait(false)).Single();

        /// <summary>
        /// Performs the 'add' function.
        /// </summary>
        /// <param name="file">The file to add.</param>
        /// <param name="duplicates">Add books to database even if they already exist. Comparison is done based on book titles and authors. Note that the <paramref name="autoMerge"/> option takes precedence.</param>
        /// <param name="autoMerge">If books with similar titles and authors are found, merge the incoming formats (files) automatically into existing book records. A value of <see cref="AutoMerge.Ignore"/> means duplicate formats are discarded. A value of <see cref="AutoMerge.Overwrite"/> means duplicate formats in the library are overwritten with the newly added files. A value of <see cref="AutoMerge.NewRecord"/> means duplicate formats are placed into a new book record.</param>
        /// <param name="title">Set the title of the added book.</param>
        /// <param name="authors">Set the authors of the added book.</param>
        /// <param name="isbn">Set the ISBN of the added book.</param>
        /// <param name="identifiers">Set the identifiers for this book, for e.g. -I asin:XXX -I isbn:YYY.</param>
        /// <param name="tags">Set the tags of the added book.</param>
        /// <param name="series">Set the series of the added book.</param>
        /// <param name="seriesIndex">Set the series number of the added book.</param>
        /// <param name="cover">Path to the cover to use for the added book.</param>
        /// <param name="languages">A comma separated list of languages (best to use ISO639 language codes, though some language names may also be recognized).</param>
        /// <returns>The book ID.</returns>
        public async System.Threading.Tasks.Task<int> AddAsync(System.IO.FileInfo file, bool duplicates = false, AutoMerge autoMerge = default, string? title = default, string? authors = default, string? isbn = default, IEnumerable<Identifier>? identifiers = default, string? tags = default, string? series = default, int seriesIndex = -1, string? cover = default, string? languages = default)
        {
            var results = await this.Add(
                new[] { file },
                duplicates,
                autoMerge,
                empty: false,
                title,
                authors,
                isbn,
                identifiers,
                tags,
                series,
                seriesIndex,
                cover,
                languages).ConfigureAwait(false);
            return results.Single();
        }

        /// <summary>
        /// Performs the 'add_format' function.
        /// </summary>
        /// <param name="id">The book ID.</param>
        /// <param name="ebookFile">The EBook file.</param>
        /// <param name="dontReplace">Set to <see langword="true"/> to not replace the format if it already exists.</param>
        /// <returns>The task.</returns>
        public System.Threading.Tasks.Task AddFormatAsync(int id, System.IO.FileInfo ebookFile, bool dontReplace = false)
        {
            var stringBuilder = new StringBuilder()
                .AppendIf(dontReplace, " --dont-replace")
                .Append(' ')
                .Append(id)
                .Append(' ')
                .Append(QuoteIfRequired(ebookFile.FullName));

            return this.ExecuteCalibreDbAsync("add_format", stringBuilder.ToString());
        }

        /// <summary>
        /// Performs the 'search' function.
        /// </summary>
        /// <param name="searchExpression">The search expression.</param>
        /// <returns>The IDs.</returns>
        public async IAsyncEnumerable<int> SearchAsync(string searchExpression)
        {
            var results = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var end = false;
            var resetEvent = new AsyncManualResetEvent();

            var task = this.ExecuteCalibreDbAsync(
                "search",
                searchExpression,
                data =>
                {
                    if (Preprocess(data) is string { Length: > 0 } processedData)
                    {
                        foreach (var result in processedData.Split(',').Select(value => value.Trim()).Select(value => int.Parse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture)))
                        {
                            results.Enqueue(result);
                            resetEvent.Set();
                        }
                    }
                },
                complete: () =>
                {
                    end = true;
                    resetEvent.Set();
                });

            do
            {
                await resetEvent.WaitAsync().ConfigureAwait(false);
                while (results.TryDequeue(out var result))
                {
                    yield return result;
                }

                resetEvent.Reset();
            }
            while (!end);
        }

        /// <summary>
        /// Performs the 'set_metadata' function.
        /// </summary>
        /// <param name="id">The book ID.</param>
        /// <param name="field">The field name.</param>
        /// <param name="value">The value.</param>
        /// <returns>The task.</returns>
        public System.Threading.Tasks.Task SetMetadataAsync(int id, string field, object? value) => this.SetMetadataAsync(id, field, new[] { value });

        /// <summary>
        /// Performs the 'set_metadata' function.
        /// </summary>
        /// <param name="id">The book ID.</param>
        /// <param name="field">The field name.</param>
        /// <param name="values">The values.</param>
        /// <returns>The task.</returns>
        public System.Threading.Tasks.Task SetMetadataAsync(int id, string field, IEnumerable<object?> values) => this.SetMetadataAsync(id, values.ToLookup(_ => field, value => value, StringComparer.Ordinal));

        /// <summary>
        /// Performs the 'set_metadata' function.
        /// </summary>
        /// <param name="id">The book ID.</param>
        /// <param name="fields">The fields to set.</param>
        /// <returns>The task.</returns>
        public System.Threading.Tasks.Task SetMetadataAsync(int id, ILookup<string, object?> fields)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(id);
            foreach (var field in fields)
            {
                stringBuilder
                    .Append(" --field ")
                    .Append(field.Key)
                    .Append(':')
                    .Append(string.Join(",", field.Select(value => value?.ToString()).Select(QuoteIfRequired)));
            }

            return this.ExecuteCalibreDbAsync("set_metadata", stringBuilder.ToString());
        }

        /// <summary>
        /// Performs the 'show_metadata' function.
        /// </summary>
        /// <param name="id">The ID.</param>
        /// <returns>The OPF package.</returns>
        public async System.Threading.Tasks.Task<Opf.Package> ShowMetadataAsync(int id)
        {
            const int BufferSize = 4096;
            using var memoryStream = new System.IO.MemoryStream(BufferSize);
            var writer = new System.IO.StreamWriter(memoryStream, Encoding.UTF8, BufferSize, leaveOpen: true);
#if NETSTANDARD2_0
            using (writer)
#else
            await using (writer)
#endif
            {
                await this.ExecuteCalibreDbAsync("show_metadata", FormattableString.Invariant($"{id} --as-opf"), data =>
                {
                    if (Preprocess(data) is string value)
                    {
                        writer.WriteLine(value);
                    }
                }).ConfigureAwait(false);
            }

            memoryStream.Position = 0;
            var xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(Opf.Package));
            return (Opf.Package)xmlSerializer.Deserialize(memoryStream);
        }

        /// <summary>
        /// Performs the 'show_categories' function.
        /// </summary>
        /// <returns>The categories.</returns>
        public async IAsyncEnumerable<Category> ListCategories()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder
                .Append(" --csv");
            var command = stringBuilder.ToString();
            stringBuilder.Length = 0;

            bool hasHeader = false;
            var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var end = false;
            var resetEvent = new AsyncManualResetEvent();

            var execureCalibreDbTask = this.ExecuteCalibreDbAsync(
                "list_categories",
                command,
                data =>
                {
                    if (Preprocess(data) is string value)
                    {
                        if (!hasHeader)
                        {
                            hasHeader = true;
                            return;
                        }

                        if (value is not null)
                        {
                            lines.Enqueue(value);
                            resetEvent.Set();
                        }
                    }
                },
                () =>
                {
                    end = true;
                    resetEvent.Set();
                });

            string?[]? values = default;
            do
            {
                await resetEvent.WaitAsync().ConfigureAwait(false);
                while (lines.TryDequeue(out var line))
                {
                    string? lastItem = default;
                    if (values is null)
                    {
                        values = ProcessStrings(line, ',', combining: false);
                        lastItem = GetLastItem(values);
                    }
                    else
                    {
                        lastItem = UpdateValues(ref values, GetLastItem(values), ProcessStrings(line, ',', combining: true));
                    }

                    if (CheckStartQuote(lastItem) && !CheckEndQuote(lastItem))
                    {
                        continue;
                    }

                    CleanValues(values);

                    yield return new Category(
                        (CategoryType)Enum.Parse(typeof(CategoryType), values[0]?.Trim('#'), ignoreCase: true),
                        values[1]!,
                        int.Parse(values[2], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(values[3], System.Globalization.CultureInfo.InvariantCulture));

                    values = default;
                }

                resetEvent.Reset();
            }
            while (!end);

            static string? GetLastItem(string?[] items)
            {
                return
#if NETSTANDARD2_0
                    items[items.Length - 1];
#else
                    items[^1];
#endif

            }
        }

        private static string? Preprocess(string? line)
        {
            if (line is null)
            {
                return default;
            }

#if NETSTANDARD2_0
            if (line.Contains(IntegrationStatus))
            {
                line = line
                    .Replace(IntegrationStatusTrue, string.Empty)
                    .Replace(IntegrationStatusFalse, string.Empty);
            }
#else
            if (line.Contains(IntegrationStatus, StringComparison.Ordinal))
            {
                line = line
                    .Replace(IntegrationStatusTrue, string.Empty, StringComparison.Ordinal)
                    .Replace(IntegrationStatusFalse, string.Empty, StringComparison.Ordinal);
            }
#endif

            return line.Trim();
        }

        private static string? QuoteIfRequired(string? value) => value is not null
#if NETSTANDARD2_0
            && value.Contains(' ')
#else
            && value.Contains(' ', StringComparison.Ordinal)
#endif
            ? string.Concat("\"", value, "\"") : value;

        private static string?[] ProcessStrings(string value, char delimiter, bool combining)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            System.Diagnostics.Contracts.Contract.EndContractBlock();

            // work through each one
            var splitIndicies = new List<int>();
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == delimiter)
                {
                    if (!combining)
                    {
                        splitIndicies.Add(i);
                    }
                }
                else if (value[i] == QuoteChar)
                {
                    combining = !combining;
                }
            }

            var returnArray = new string[splitIndicies.Count + 1];
            var returnIndex = 0;
            var lastIndex = 0;

            foreach (var index in splitIndicies)
            {
                var length = index - lastIndex;
                returnArray[returnIndex] = value.Substring(lastIndex, length);
                lastIndex = index + 1;
                returnIndex++;
            }

            returnArray[returnIndex] =
#if NETSTANDARD2_0
                value.Substring(lastIndex, value.Length - lastIndex);
#else
                value[lastIndex..];
#endif
            returnIndex++;

            // resize the array
            if (returnArray.Length != returnIndex)
            {
                Array.Resize(ref returnArray, returnIndex);
            }

            return returnArray;
        }

        private static bool CheckStartQuote(string? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            System.Diagnostics.Contracts.Contract.EndContractBlock();

            // check the number of quotes at the start
            var end = 0;
            for (; end < value.Length; end++)
            {
                if (value[end] != QuoteChar)
                {
                    break;
                }
            }

            return end % 2 != 0;
        }

        private static bool CheckEndQuote(string? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            System.Diagnostics.Contracts.Contract.EndContractBlock();

            // check the number of quotes at the end
            var start = 0;
            for (; start < value.Length; start++)
            {
                if (value[value.Length - 1 - start] != QuoteChar)
                {
                    break;
                }
            }

            return start % 2 != 0;
        }

        private static void CleanValues(string?[] values)
        {
            // clean up any extraneous quotes
            for (var i = 0; i < values.Length; i++)
            {
                // we can assume that if the string starts/ends with a quote, then these are single quotes
                // as if there are any escaped quotes in the string, then it must be quoted.
                var value = values[i];
                if (value is null || string.IsNullOrEmpty(value))
                {
                    // set this to null, as it was an empty, unquoted string.
                    values[i] = null;
                    continue;
                }

                var start = 0;
                if (value.Length > 0 && value[start] == QuoteChar)
                {
                    start++;
                }

                var end = value.Length - 1;
                if (value.Length > 0 && value[end] == QuoteChar)
                {
                    end--;
                }

                var length = end - start + 1;
                if (length != value.Length)
                {
                    value = length <= 0 ? string.Empty : value.Substring(start, length);
                }

#if NETSTANDARD2_0
                values[i] = value.Replace(EscapedQuoteString, QuoteString);
#else
                values[i] = value.Replace(EscapedQuoteString, QuoteString, StringComparison.Ordinal);
#endif
            }
        }

        private static string? UpdateValues(ref string?[] values, string? lastItem, string?[] split)
        {
#if NETSTANDARD2_0
            values[values.Length - 1]
#else
            values[^1]
#endif
                = lastItem + Environment.NewLine + split[0];

            // resize the array
            if (split.Length > 1)
            {
                var index = values.Length;
                Array.Resize(ref values, values.Length + split.Length - 1);
                Array.Copy(split, 1, values, index, split.Length - 1);
            }

#if NETSTANDARD2_0
            return values[values.Length - 1];
#else
            return values[^1];
#endif
        }

        private async System.Threading.Tasks.Task<IEnumerable<int>> Add(IEnumerable<System.IO.FileInfo> files, bool duplicates = false, AutoMerge autoMerge = default, bool empty = false, string? title = default, string? authors = default, string? isbn = default, IEnumerable<Identifier>? identifiers = default, string? tags = default, string? series = default, int seriesIndex = -1, string? cover = default, string? languages = default)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder
                .AppendIf(duplicates, " --duplicates")
                .AppendIf(autoMerge != AutoMerge.Default, () => " --automerge=" + autoMerge switch { AutoMerge.Ignore => "ignore", AutoMerge.Overwrite => "overwrite", AutoMerge.NewRecord => "new_record", _ => null, })
                .AppendIf(empty, " --empty")
                .AppendIf(title is not null, () => FormattableString.Invariant($" --title={QuoteIfRequired(title)}"))
                .AppendIf(authors is not null, () => FormattableString.Invariant($" --authors={QuoteIfRequired(authors)}"))
                .AppendIf(isbn is not null, () => FormattableString.Invariant($" --isbn={QuoteIfRequired(isbn)}"))
                .AppendIf(identifiers is not null, () => string.Join(" ", identifiers.Select(identifier => FormattableString.Invariant($" --identifier={identifier}"))))
                .AppendIf(tags is not null, () => FormattableString.Invariant($" --tags={QuoteIfRequired(tags)}"))
                .AppendIf(series is not null, () => FormattableString.Invariant($" --series={QuoteIfRequired(series)}"))
                .AppendIf(seriesIndex != -1, () => FormattableString.Invariant($" --series_index={seriesIndex}"))
                .AppendIf(cover is not null, () => FormattableString.Invariant($" --cover={QuoteIfRequired(cover)}"))
                .AppendIf(languages is not null, () => FormattableString.Invariant($" --languages={QuoteIfRequired(languages)}"));

            foreach (var file in files)
            {
                stringBuilder
                    .Append(' ')
                    .Append(QuoteIfRequired(file.FullName));
            }

            var bookIds = new List<int>();
            await this.ExecuteCalibreDbAsync("add", stringBuilder.ToString(), data =>
            {
                if (Preprocess(data) is string { Length: > 0 } line && line.StartsWith("Added book ids", StringComparison.Ordinal))
                {
                    bookIds.AddRange(line
                        .Substring(16)
                        .Split(',')
                        .Select(value => value.Trim())
                        .Select(value => int.Parse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture)));
                }
            }).ConfigureAwait(false);

            return bookIds;
        }

        private async System.Threading.Tasks.Task ExecuteCalibreDbAsync(string command, string arguments, Action<string>? outputDataReceived = default, Action? complete = default)
        {
            var fullArguments = command + " --library-path \"" + this.Path + "\" " + arguments;
            this.logger.LogDebug(fullArguments);

            var processStartInfo = new System.Diagnostics.ProcessStartInfo(this.calibreDbPath, fullArguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = new System.Diagnostics.Process { StartInfo = processStartInfo };

            process.OutputDataReceived += (sender, args) =>
            {
                if (args?.Data is null)
                {
                    return;
                }

                if (outputDataReceived is not null)
                {
                    outputDataReceived(args.Data);
                }
                else
                {
                    this.logger.LogInformation(0, args.Data);
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args?.Data is null)
                {
                    return;
                }

                this.logger.LogError(0, args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (await process
                .WaitForExitAsync(System.Threading.Timeout.Infinite)
                .ConfigureAwait(false))
            {
                await process
                    .WaitForExitAsync()
                    .ConfigureAwait(false);
            }

            if (complete is not null)
            {
                complete();
            }
        }
    }
}
