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
    public readonly struct EpubInfo : System.IEquatable<EpubInfo>
    {
        private EpubInfo(
            System.Collections.Generic.IEnumerable<string> authors,
            string title,
            System.Collections.Generic.IEnumerable<string> publishers,
            System.Collections.Generic.IEnumerable<string> tags,
            string extension,
            string path) => (this.Authors, this.Title, this.Publishers, this.Tags, this.Extension, this.Path) = (authors, title, publishers, tags, extension, path);

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
        /// Gets the path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the extension.
        /// </summary>
        public string Extension { get; }

        /// <summary>
        /// The equals operator.
        /// </summary>
        /// <param name="first">The first parameter.</param>
        /// <param name="second">The second parameter.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator ==(EpubInfo first, EpubInfo second) => first.Equals(second);

        /// <summary>
        /// The not-equals operator.
        /// </summary>
        /// <param name="first">The first parameter.</param>
        /// <param name="second">The second parameter.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator !=(EpubInfo first, EpubInfo second) => !first.Equals(second);

        /// <summary>
        /// Parses EPUB information from a path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The EPUB information.</returns>
        public static EpubInfo Parse(string path)
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
            var publishers = new System.Collections.Generic.List<string>();
            var publisher = document.SelectSingleNode("/x:package/x:metadata/dc:publisher[@id='publisher']", manager);
            if (publisher is null)
            {
                for (var index = 1; (publisher = document.SelectSingleNode($"/x:package/x:metadata/dc:publisher[@id='publisher-{index}']", manager)) != null; index++)
                {
                    publishers.Add(publisher.InnerText);
                }
            }
            else
            {
                publishers.Add(publisher.InnerText);
            }

            var authors = new System.Collections.Generic.List<string>();
            var author = document.SelectSingleNode("/x:package/x:metadata/dc:creator[@id='author']", manager);
            if (author is null)
            {
                for (var index = 1; (author = document.SelectSingleNode($"/x:package/x:metadata/dc:creator[@id='author-{index}']", manager)) != null; index++)
                {
                    authors.Add(author.InnerText);
                }
            }
            else
            {
                authors.Add(author.InnerText);
            }

            var tags = new System.Collections.Generic.List<string>();
            var tag = document.SelectSingleNode("/x:package/x:metadata/dc:subject[@id='subject']", manager);
            if (tag is null)
            {
                for (var index = 1; (tag = document.SelectSingleNode($"/x:package/x:metadata/dc:subject[@id='subject-{index}']", manager)) != null; index++)
                {
                    tags.Add(tag.InnerText);
                }
            }
            else
            {
                tags.Add(tag.InnerText);
            }

            return new EpubInfo(authors.ToArray(), title, publishers.ToArray(), tags.ToArray(), System.IO.Path.GetExtension(path).TrimStart('.'), path);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EpubInfo other ? this.Equals(other) : base.Equals(obj);

        /// <inheritdoc/>
        public bool Equals(EpubInfo other) => this.Title.Equals(other.Title, System.StringComparison.Ordinal)
            && this.Authors.SequenceEqual(other.Authors)
            && this.Publishers.SequenceEqual(other.Publishers)
            && this.Path.Equals(other.Path, System.StringComparison.Ordinal)
            && this.Extension.Equals(other.Extension, System.StringComparison.Ordinal);

        /// <inheritdoc/>
        public override int GetHashCode() => (this.Title, this.Authors, this.Publishers, this.Path, this.Extension).GetHashCode();
    }
}