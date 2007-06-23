﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <author name="Daniel Grunwald"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Text;
using ICSharpCode.SharpDevelop.Dom;
using ICSharpCode.XmlEditor;

namespace XamlBinding
{
	/// <summary>
	/// Represents the context of a location in a XAML document.
	/// </summary>
	public sealed class XamlExpressionContext : ExpressionContext
	{
		public readonly XmlElementPath ElementPath;
		public readonly string AttributeName;
		public readonly bool InAttributeValue;
		
		public XamlExpressionContext(XmlElementPath elementPath, string attributeName, bool inAttributeValue)
		{
			if (elementPath == null)
				throw new ArgumentNullException("elementPath");
			this.ElementPath = elementPath;
			this.AttributeName = attributeName;
			this.InAttributeValue = inAttributeValue;
		}
		
		public override bool ShowEntry(object o)
		{
			return true;
		}
		
		public override string ToString()
		{
			StringBuilder b = new StringBuilder();
			b.Append("[XamlExpressionContext ");
			for (int i = 0; i < ElementPath.Elements.Count; i ++) {
				if (i > 0) b.Append(">");
				b.Append(ElementPath.Elements[i].Name);
			}
			if (!string.IsNullOrEmpty(AttributeName)) {
				b.Append(" AttributeName=");
				b.Append(AttributeName);
				if (InAttributeValue) {
					b.Append(" InAttributeValue");
				}
			}
			b.Append("]");
			return b.ToString();
		}
	}
}
