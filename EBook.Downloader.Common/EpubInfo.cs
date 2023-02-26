// -----------------------------------------------------------------------
// <copyright file="EpubInfo.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common;

/// <summary>
/// EPUB information.
/// </summary>
public record class EpubInfo
{
    /// <summary>
    /// Gets the authors.
    /// </summary>
    public IEnumerable<string> Authors { get; init; } = Enumerable.Empty<string>();

    /// <summary>
    /// Gets the title.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets the date.
    /// </summary>
    public DateTimeOffset Date { get; init; }

    /// <summary>
    /// Gets the publishers.
    /// </summary>
    public IEnumerable<string> Publishers { get; init; } = Enumerable.Empty<string>();

    /// <summary>
    /// Gets the tags.
    /// </summary>
    public IEnumerable<string> Tags { get; init; } = Enumerable.Empty<string>();

    /// <summary>
    /// Gets the identifiers.
    /// </summary>
    public IReadOnlyDictionary<string, string> Identifiers { get; init; } = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the long description.
    /// </summary>
    public System.Xml.XmlElement? LongDescription { get; init; }

    /// <summary>
    /// Gets the collections.
    /// </summary>
    public IEnumerable<EpubCollection> Collections { get; init; } = Enumerable.Empty<EpubCollection>();

    /// <summary>
    /// Gets the path.
    /// </summary>
    public FileInfo Path { get; init; } = new(Environment.SystemDirectory);

    /// <summary>
    /// Parses EPUB information from a path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="parseDescription">Set to <see langword="true" /> to parse the description.</param>
    /// <returns>The EPUB information.</returns>
    public static EpubInfo Parse(string path, bool parseDescription)
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml(GetContents(path));

        var manager = CreateNamespaceManager(document);

        var title = document.SelectSingleNode("/x:package/x:metadata/dc:title[@id='title']", manager).InnerText;
        var date = DateTimeOffset.Parse(document.SelectSingleNode("/x:package/x:metadata/dc:date", manager).InnerText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None);
        var publishers = GetPublishers(document, manager);
        var authors = GetAuthors(document, manager);
        var tags = GetTags(document, manager);
        var identifiers = GetIdentifiers(document, manager)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        (var description, var longDescription) = parseDescription
            ? GetDescription(document, manager)
            : default;
        var collections = GetCollections(document, manager);

        return new EpubInfo
        {
            Authors = authors.ToArray(),
            Title = title,
            Date = date,
            Publishers = publishers.ToArray(),
            Tags = tags.ToArray(),
            Identifiers = identifiers,
            Description = description,
            LongDescription = longDescription,
            Collections = collections,
            Path = new FileInfo(path),
        };

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "These are XML namespaces")]
        static System.Xml.XmlNamespaceManager CreateNamespaceManager(System.Xml.XmlDocument document)
        {
            var manager = new System.Xml.XmlNamespaceManager(document.NameTable);
            manager.AddNamespace(string.Empty, "http://www.idpf.org/2007/opf");
            manager.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
            manager.AddNamespace("x", document.DocumentElement.NamespaceURI);
            return manager;
        }

        static IEnumerable<string> GetPublishers(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
        {
            var publisher = document.SelectSingleNode("/x:package/x:metadata/dc:publisher[@id='publisher']", manager);
            if (publisher is not null)
            {
                yield return publisher.InnerText;
                yield break;
            }

            for (var index = 1; (publisher = document.SelectSingleNode(FormattableString.Invariant($"/x:package/x:metadata/dc:publisher[@id='publisher-{index}']"), manager)) is not null; index++)
            {
                yield return publisher.InnerText;
            }
        }

        static IEnumerable<string> GetAuthors(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
        {
            var author = document.SelectSingleNode("/x:package/x:metadata/dc:creator[@id='author']", manager);
            if (author is not null)
            {
                yield return author.InnerText;
                yield break;
            }

            for (var index = 1; (author = document.SelectSingleNode(FormattableString.Invariant($"/x:package/x:metadata/dc:creator[@id='author-{index}']"), manager)) is not null; index++)
            {
                yield return author.InnerText;
            }
        }

        static IEnumerable<string> GetTags(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
        {
            var collection = document.SelectSingleNode("/x:package/x:metadata/dc:subject[@id='subject']", manager);
            if (collection is not null)
            {
                yield return collection.InnerText;
                yield break;
            }

            for (var index = 1; (collection = document.SelectSingleNode(FormattableString.Invariant($"/x:package/x:metadata/dc:subject[@id='subject-{index}']"), manager)) is not null; index++)
            {
                yield return collection.InnerText;
            }
        }

        static IEnumerable<(string Key, string Value)> GetIdentifiers(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
        {
            var identifier = document.SelectSingleNode("/x:package/x:metadata/dc:identifier[@id='uid']", manager);
            if (identifier is null)
            {
                yield break;
            }

            var split = identifier.InnerText.Split(new[] { ':' }, 2);
            yield return (split[0], split[1]);
        }

        static (string? Description, System.Xml.XmlElement? LongDescription) GetDescription(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
        {
            var description = document.SelectSingleNode("/x:package/x:metadata/dc:description[@id='description']", manager)?.InnerText ?? default;
            System.Xml.XmlElement? longDescription = default;
            if (document.SelectSingleNode("/x:package/x:metadata/x:meta[@id='long-description']", manager) is System.Xml.XmlNode node && node.InnerText is not null)
            {
                longDescription = document.CreateElement("div");
                longDescription.InnerXml = node.InnerText.Replace("\t", string.Empty);
            }

            return (description, longDescription);
        }

        static IEnumerable<EpubCollection> GetCollections(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
        {
            var nodes = document.SelectNodes("/x:package/x:metadata/x:meta[@property='belongs-to-collection']", manager);
            if (nodes is null)
            {
                yield break;
            }

            foreach (var node in nodes.OfType<System.Xml.XmlNode>())
            {
                var id = node.Attributes["id"].Value;
                var name = node.InnerText;

                var collectionType = document.SelectSingleNode($"/x:package/x:metadata/x:meta[@refines='#{id}' and @property='collection-type']", manager) is System.Xml.XmlNode collectionTypeNode && collectionTypeNode.InnerText is not null
                    ? (EpubCollectionType)Enum.Parse(typeof(EpubCollectionType), collectionTypeNode.InnerText, ignoreCase: true)
                    : EpubCollectionType.None;

                var groupPosition = document.SelectSingleNode($"/x:package/x:metadata/x:meta[@refines='#{id}' and @property='group-position']", manager) is System.Xml.XmlNode groupPositionNode && groupPositionNode.InnerText is not null
                    ? int.Parse(groupPositionNode.InnerText, System.Globalization.CultureInfo.InvariantCulture)
                    : 0;

                yield return new EpubCollection(id, name, collectionType, groupPosition);
            }
        }

        static string GetContents(string path)
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var content = zip.GetEntry("epub/content.opf");

            using var stream = content.Open();
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}