﻿// <autogenerated />

namespace EBook.Downloader.Calibre.Opf
{
    /// <remarks/>
    [System.Serializable]
    [System.ComponentModel.DesignerCategory("code")]
    [System.Xml.Serialization.XmlType(AnonymousType = true, Namespace = Namespaces.Opf)]
    public partial class Metadata
    {
        /// <remarks/>
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.contributor), typeof(Contributor), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.creator), typeof(Creator), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.date), typeof(System.DateTime), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.description), typeof(string), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.identifier), typeof(Identifier), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.language), typeof(string), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.publisher), typeof(string), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.subject), typeof(string), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.title), typeof(string), Namespace = Namespaces.Purl)]
        [System.Xml.Serialization.XmlElement(nameof(ItemsChoiceType.meta), typeof(MetadataMeta))]
        [System.Xml.Serialization.XmlChoiceIdentifier(nameof(ItemsElementName))]
        public object[] Items { get; set; }

        /// <remarks/>
        [System.Xml.Serialization.XmlElement(nameof(ItemsElementName))]
        [System.Xml.Serialization.XmlIgnore]
        public ItemsChoiceType[] ItemsElementName { get; set; }
    }


}
