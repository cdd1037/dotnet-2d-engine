using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace GameAuthoringLab;

// Framework XML tokenization with the same bounded profile for runtime and build-time use.
// No resource I/O, entity resolver, regular-expression grammar, or engine dependencies.
internal static class UiXmlParser
{
    internal static UiXmlDocument Parse(string source, string file)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 64 * 1024, MaxCharactersFromEntities = 1024, IgnoreComments = false };
        try
        {
            // Framework XML tokenization with a bounded profile-only model. No general LINQ-to-XML DOM.
            var document = new UiXmlDocument();
            var parents = new Stack<UiXmlElement>();
            // XmlReader supplies the token boundary and line position. Keep only the raw
            // interpolation-presence bit; do not reconstruct XML or reimplement its parser.
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == '\r') { if (i + 1 < source.Length && source[i + 1] == '\n') i++; lineStarts.Add(i + 1); }
                else if (source[i] == '\n') lineStarts.Add(i + 1);
            }
            bool HasRawInterpolation(int row, int column)
            {
                if (row < 1 || row > lineStarts.Count || column < 1) return false;
                int start = lineStarts[row - 1] + column - 1;
                if (start >= source.Length) return false;
                int end = source.IndexOf('<', start);
                if (end < 0) end = source.Length;
                return source.IndexOf("{{", start, end - start, StringComparison.Ordinal) >= 0;
            }
            string? version = null, encoding = null;
            using var reader = XmlReader.Create(new StringReader(source), settings);
            int elements = 0;
            while (reader.Read())
            {
                var line = (IXmlLineInfo)reader;
                int row = line.LineNumber, column = line.LinePosition;
                if (reader.Depth > 16 || reader.NodeType == XmlNodeType.Element && ++elements > 256)
                    throw new UiContractException("UI_LIMIT", file, row, column, "$", "Maximum depth 16 or element count 256 exceeded.");
                if (reader.NodeType is XmlNodeType.ProcessingInstruction or XmlNodeType.CDATA)
                    throw new UiContractException("UI_XML_PROFILE", file, row, column, "$", "Processing instructions and CDATA are unsupported.");
                switch (reader.NodeType)
                {
                    case XmlNodeType.XmlDeclaration:
                        version = reader.GetAttribute("version"); encoding = reader.GetAttribute("encoding"); break;
                    case XmlNodeType.Element:
                        bool empty = reader.IsEmptyElement;
                        var element = new UiXmlElement(new(reader.LocalName, reader.NamespaceURI), row, column);
                        if (parents.Count != 0) parents.Peek().Add(element); else document.Root = element;
                        if (reader.MoveToFirstAttribute())
                        {
                            do {
                                element.AddAttribute(new UiXmlAttribute(new(reader.LocalName, reader.NamespaceURI), reader.Value,
                                    reader.NamespaceURI == "http://www.w3.org/2000/xmlns/", line.LineNumber, line.LinePosition));
                            } while (reader.MoveToNextAttribute());
                            reader.MoveToElement();
                        }
                        if (!empty) parents.Push(element);
                        break;
                    case XmlNodeType.EndElement:
                        parents.Pop(); break;
                    case XmlNodeType.Text:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        if (parents.Count != 0) parents.Peek().Add(new UiXmlText(reader.Value, row, column, HasRawInterpolation(row, column)));
                        break;
                    // Comments are permitted but never interpreted. Separate text tokens stay separate.
                }
            }
            if (version is not null && (version != "1.0" || encoding is not null && !encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase)))
                throw new UiContractException("UI_XML_PROFILE", file, 1, 1, "$declaration", "Only XML 1.0 with UTF-8 encoding is supported.");
            return document;
        }
        catch (XmlException e) { throw new UiContractException("UI_XML", file, e.LineNumber, e.LinePosition, "$xml", e.Message, inner: e); }
    }

}
