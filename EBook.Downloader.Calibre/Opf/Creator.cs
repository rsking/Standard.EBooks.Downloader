﻿// <autogenerated />

namespace EBook.Downloader.Calibre.Opf
{
    /// <remarks/>
    [System.Serializable]
    [System.ComponentModel.DesignerCategory("code")]
    [System.Xml.Serialization.XmlType(AnonymousType = true, Namespace = Namespaces.Purl)]
    [System.Xml.Serialization.XmlRoot(Namespace = Namespaces.Purl, IsNullable = false)]
    public partial class Creator
    {
        /// <remarks/>
        [System.Xml.Serialization.XmlAttribute("role", Form = System.Xml.Schema.XmlSchemaForm.Qualified, Namespace = Namespaces.Opf)]
        public string Role { get; set; }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttribute("file-as", Form = System.Xml.Schema.XmlSchemaForm.Qualified, Namespace = Namespaces.Opf)]
        public string FileAs { get; set; }

        /// <remarks/>
        [System.Xml.Serialization.XmlText]
        public string Value { get; set; }
    }
}
