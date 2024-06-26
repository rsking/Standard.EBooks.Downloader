// -----------------------------------------------------------------------
// <copyright file="CalibreBook.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common;

/// <summary>
/// EPUB information.
/// </summary>
public record class CalibreBook
{
    /// <summary>
    /// Initialises a new instance of the <see cref="CalibreBook"/> class.
    /// </summary>
    /// <param name="element">The element.</param>
    public CalibreBook(System.Text.Json.JsonElement element)
    {
        this.Id = element.GetProperty("id").GetInt32();

        // authors
        var authors = element.GetProperty("authors").GetString()
            ?? throw new ArgumentException("Failed to get authors", nameof(element));
        this.Authors = authors.Split('&').Select(author => author.Trim()).ToArray();
        var sanitizedAuthor = Sanitise(this.Authors[0]);

        // name
        var name = element.GetProperty("title").GetString()
            ?? throw new ArgumentException("Failed to get title", nameof(element));
        var sanitisedName = Sanitise(name.Trim());
        this.Name = $"{TrimIfRequired(sanitisedName, 31)} - {sanitizedAuthor}";

        // path
        this.Path = FormattableString.Invariant($"{sanitizedAuthor}/{TrimIfRequired(sanitisedName, 35)} ({this.Id})");
        this.LastModified = element.GetProperty("last_modified").GetDateTime().ToUniversalTime();

        // identifiers
        if (element.TryGetProperty("identifiers", out var identifiers))
        {
            this.Identifiers = GetItems(identifiers).
                ToDictionary<KeyValuePair<string, System.Text.Json.JsonElement>, string, object>(
                    kvp => kvp.Key,
                    kvp =>
                    {
                        var stringValue = kvp.Value.ToString();
                        return Uri.TryCreate(stringValue, UriKind.Absolute, out var uri) ? uri : stringValue;
                    },
                    StringComparer.Ordinal);

            static IEnumerable<KeyValuePair<string, System.Text.Json.JsonElement>> GetItems(System.Text.Json.JsonElement element)
            {
                var enumerator = element.EnumerateObject();

                while (enumerator.MoveNext())
                {
                    yield return new(enumerator.Current.Name, enumerator.Current.Value);
                }
            }
        }

        static string TrimIfRequired(string input, int length)
        {
            return input.Length > length
                ? input.Substring(0, length).TrimEnd()
                : input;
        }
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="CalibreBook" /> class.
    /// </summary>
    /// <param name="lastModified">The book last modified date.</param>
    public CalibreBook(string lastModified) => this.LastModified = DateTime.Parse(lastModified, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);

    /// <summary>
    /// Gets the ID.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the Name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authors.
    /// </summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>
    /// Gets the identifiers.
    /// </summary>
    public IReadOnlyDictionary<string, object> Identifiers { get; init; } = System.Collections.Immutable.ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Gets the Path.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last modified time.
    /// </summary>
    public DateTime LastModified { get; init; }

    /// <summary>
    /// Gets the file info.
    /// </summary>
    /// <param name="path">The base path.</param>
    /// <param name="extension">The extension.</param>
    /// <returns>The file info for the book.</returns>
    public string GetFullPath(string path, string extension)
    {
        return System.IO.Path.Combine(GetEnumerable(path).Concat(GetPathsSegments(this.Path)).Concat(GetEnumerable(this.Name + extension)).ToArray());

        static IEnumerable<string> GetEnumerable(string value)
        {
            yield return value;
        }

        static IEnumerable<string> GetPathsSegments(string path)
        {
            return path.Split(System.IO.Path.AltDirectorySeparatorChar);
        }
    }

    private static char[] GetInvalidFileNameChars() =>
    [
        '\"', '<', '>', '|', '\0',
        (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
        (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
        (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
        (char)31, ':', '*', '?', '\\', '/',
    ];

    private static string Sanitise(string input)
    {
        input = Presanitize(input);
        var length = input.Length;
        var outputChars = new char[4 * length];

        var characters = Lucene.Net.Analysis.Miscellaneous.ASCIIFoldingFilter.FoldToASCII(input.ToCharArray(), 0, outputChars, 0, length);

        return RemoveOthers(outputChars, characters);

        static string Presanitize(string input)
        {
            // any special cases not convered by Lucene
            return input.Replace("�", "--");
        }

        static string RemoveOthers(char[] normalizedString, int length)
        {
            const char SmartStartSingleQuote = (char)699;
            const char SmartEndSingleQuote = (char)700;

            var stringBuilder = new System.Text.StringBuilder();
            var invalidChars = GetInvalidFileNameChars();

            for (var i = 0; i < length; i++)
            {
                var c = normalizedString[i];
                var category = char.GetUnicodeCategory(c);
                if (category == System.Globalization.UnicodeCategory.SpaceSeparator)
                {
                    // any space separator should just be a ' ' character
                    _ = stringBuilder.Append(' ');
                }
                else if (c is SmartStartSingleQuote)
                {
                    _ = stringBuilder.Append('`');
                }
                else if (c is SmartEndSingleQuote)
                {
                    _ = stringBuilder.Append('\'');
                }
                else if (c > 128)
                {
                    _ = stringBuilder.Append('_').Append('_');
                }
                else if (invalidChars.Contains(c))
                {
                    _ = stringBuilder.Append('_');
                }
                else if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    _ = stringBuilder.Append(c);
                }
            }

            if (stringBuilder[stringBuilder.Length - 1] == '.')
            {
                stringBuilder[stringBuilder.Length - 1] = '_';
            }

            return stringBuilder.ToString();
        }
    }
}