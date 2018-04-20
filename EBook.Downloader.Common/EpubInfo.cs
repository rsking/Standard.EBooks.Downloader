// -----------------------------------------------------------------------
// <copyright file="EpubInfo.cs" company="RossKing">
// Copyright (c) RossKing. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace EBook.Downloader.Common
{
    /// <summary>
    /// EPUB information.
    /// </summary>
    public struct EpubInfo
    {
        /// <summary>
        /// Gets the authors
        /// </summary>
        public System.Collections.Generic.IEnumerable<string> Authors { get; private set; }

        /// <summary>
        /// Gets the title
        /// </summary>
        public string Title { get; private set; }

        /// <summary>
        /// Gets the path
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        /// Gets the extension
        /// </summary>
        public string Extension { get; private set; }

        /// <summary>
        /// Parses EPUB information from a path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The EPUB information.</returns>
        public static EpubInfo Parse(string path)
        {
            // open the zip file
            string contents = null;
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                var content = zip.GetEntry("epub/content.opf");

                using (var stream = content.Open())
                {
                    using (var reader = new System.IO.StreamReader(stream))
                    {
                        contents = reader.ReadToEnd();
                    }
                }
            }

            var document = new System.Xml.XmlDocument();
            document.LoadXml(contents);
            var manager = new System.Xml.XmlNamespaceManager(document.NameTable);
            manager.AddNamespace(string.Empty, "http://www.idpf.org/2007/opf");
            manager.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
            manager.AddNamespace("x", document.DocumentElement.NamespaceURI);

            var title = document.SelectSingleNode("/x:package/x:metadata/dc:title[@id='title']", manager).InnerText;
            var authors = new System.Collections.Generic.List<string>();
            var author = document.SelectSingleNode("/x:package/x:metadata/dc:creator[@id='author']", manager);
            if (author != null)
            {
                authors.Add(author.InnerText);
            }
            else
            {
                var index = 1;
                while ((author = document.SelectSingleNode($"/x:package/x:metadata/dc:creator[@id='author-{index}']", manager)) != null)
                {
                    authors.Add(author.InnerText);
                    index++;
                }
            }

            return new EpubInfo { Title = title, Authors = authors.ToArray(), Path = path, Extension = System.IO.Path.GetExtension(path).TrimStart('.') };
        }
    }
}