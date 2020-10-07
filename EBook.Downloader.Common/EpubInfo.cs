// -----------------------------------------------------------------------
// <copyright file="EpubInfo.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    using System.Linq;

    /// <summary>
    /// EPUB information.
    /// </summary>
    public record EpubInfo
    {
        private EpubInfo(
            System.Collections.Generic.IEnumerable<string> authors,
            string title,
            System.Collections.Generic.IEnumerable<string> publishers,
            System.Collections.Generic.IEnumerable<string> tags,
            System.Collections.Generic.IReadOnlyDictionary<string, string> identifiers,
            string? description,
            System.Xml.XmlElement? longDescription,
            string? seriesName,
            int seriesIndex,
            string extension,
            string path) => (this.Authors, this.Title, this.Publishers, this.Tags, this.Identifiers, this.Description, this.LongDescription, this.SeriesName, this.SeriesIndex, this.Extension, this.Path) = (authors, title, publishers, tags, identifiers, description, longDescription, seriesName, seriesIndex, extension, path);

        /// <summary>
        /// Gets the authors.
        /// </summary>
        public System.Collections.Generic.IEnumerable<string> Authors { get; }

        /// <summary>
        /// Gets the title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the publishers.
        /// </summary>
        public System.Collections.Generic.IEnumerable<string> Publishers { get; }

        /// <summary>
        /// Gets the tags.
        /// </summary>
        public System.Collections.Generic.IEnumerable<string> Tags { get; }

        /// <summary>
        /// Gets the identifiers.
        /// </summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, string> Identifiers { get; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        public string? Description { get; }

        /// <summary>
        /// Gets the long description.
        /// </summary>
        public System.Xml.XmlElement? LongDescription { get; }

        /// <summary>
        /// Gets the series name.
        /// </summary>
        public string? SeriesName { get; }

        /// <summary>
        /// Gets the series index.
        /// </summary>
        public int SeriesIndex { get; }

        /// <summary>
        /// Gets the path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the extension.
        /// </summary>
        public string Extension { get; }

        /// <summary>
        /// Parses EPUB information from a path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="parseDescription">Set to <see langword="true" /> to parse the description.</param>
        /// <returns>The EPUB information.</returns>
        public static EpubInfo Parse(string path, bool parseDescription)
        {
            string GetContents()
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                var content = zip.GetEntry("epub/content.opf");

                using var stream = content.Open();
                using var reader = new System.IO.StreamReader(stream);

                return reader.ReadToEnd();
            }

            // open the zip file
            string contents = GetContents();

            var document = new System.Xml.XmlDocument();
            document.LoadXml(contents);
            var manager = new System.Xml.XmlNamespaceManager(document.NameTable);
            manager.AddNamespace(string.Empty, "http://www.idpf.org/2007/opf");
            manager.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
            manager.AddNamespace("x", document.DocumentElement.NamespaceURI);

            var title = document.SelectSingleNode("/x:package/x:metadata/dc:title[@id='title']", manager).InnerText;
            var publishers = GetPublishers(document, manager);
            var authors = GetAuthors(document, manager);
            var tags = GetTags(document, manager);
            var identifiers = GetIdentifiers(document, manager).ToDictionary(x => x.Key, x => x.Value);
            var (description, longDescription) = parseDescription ? GetDescription(document, manager) : (default, default);
            var (_, seriesName, _, seriesPosition) = GetCollections(document, manager).FirstOrDefault(collection => collection.Type == "series");

            return new EpubInfo(authors.ToArray(), title, publishers.ToArray(), tags.ToArray(), identifiers, description, longDescription, seriesName, seriesPosition, System.IO.Path.GetExtension(path).TrimStart('.'), path);

            static System.Collections.Generic.IEnumerable<string> GetPublishers(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
            {
                var publisher = document.SelectSingleNode("/x:package/x:metadata/dc:publisher[@id='publisher']", manager);
                if (publisher is null)
                {
                    for (var index = 1; (publisher = document.SelectSingleNode($"/x:package/x:metadata/dc:publisher[@id='publisher-{index}']", manager)) is not null; index++)
                    {
                        yield return publisher.InnerText;
                    }
                }
                else
                {
                    yield return publisher.InnerText;
                }
            }

            static System.Collections.Generic.IEnumerable<string> GetAuthors(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
            {
                var author = document.SelectSingleNode("/x:package/x:metadata/dc:creator[@id='author']", manager);
                if (author is null)
                {
                    for (var index = 1; (author = document.SelectSingleNode($"/x:package/x:metadata/dc:creator[@id='author-{index}']", manager)) is not null; index++)
                    {
                        yield return author.InnerText;
                    }
                }
                else
                {
                    yield return author.InnerText;
                }
            }

            static System.Collections.Generic.IEnumerable<string> GetTags(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
            {
                var collection = document.SelectSingleNode("/x:package/x:metadata/dc:subject[@id='subject']", manager);
                if (collection is null)
                {
                    for (var index = 1; (collection = document.SelectSingleNode($"/x:package/x:metadata/dc:subject[@id='subject-{index}']", manager)) is not null; index++)
                    {
                        yield return collection.InnerText;
                    }
                }
                else
                {
                    yield return collection.InnerText;
                }
            }

            static System.Collections.Generic.IEnumerable<(string Key, string Value)> GetIdentifiers(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
            {
                var identifier = document.SelectSingleNode("/x:package/x:metadata/dc:identifier[@id='uid']", manager);
                if (identifier is not null)
                {
                    var split = identifier.InnerText.Split(new[] { ':' }, 2);
                    yield return (split[0], split[1]);
                }
            }

            static (string? Description, System.Xml.XmlElement? LongDescription) GetDescription(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
            {
                var description = document.SelectSingleNode("/x:package/x:metadata/dc:description[@id='description']", manager)?.InnerText ?? default;
                var node = document.SelectSingleNode("/x:package/x:metadata/x:meta[@id='long-description']", manager);
                System.Xml.XmlElement? longDescription = default;
                if (node?.InnerText is not null)
                {
                    longDescription = document.CreateElement("div");
                    longDescription.InnerXml = node.InnerText.Replace("\t", string.Empty);
                }

                return (description, longDescription);
            }

            static System.Collections.Generic.IEnumerable<(string Id, string Name, string? Type, int Position)> GetCollections(System.Xml.XmlDocument document, System.Xml.XmlNamespaceManager manager)
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

                    var collectionType = document.SelectSingleNode($"/x:package/x:metadata/x:meta[@refines='#{id}' and @property='collection-type']", manager)?.InnerText;
                    var groupPosition = document.SelectSingleNode($"/x:package/x:metadata/x:meta[@refines='#{id}' and @property='group-position']", manager)?.InnerText;

                    yield return (id, name, collectionType, groupPosition is null ? 0 : int.Parse(groupPosition, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
    }
}