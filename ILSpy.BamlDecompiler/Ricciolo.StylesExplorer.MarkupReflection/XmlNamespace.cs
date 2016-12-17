// Copyright (c) Cristian Civera (cristian@aspitalia.com)
// This code is distributed under the MS-PL (for details please see \doc\MS-PL.txt)

using System;

namespace Ricciolo.StylesExplorer.MarkupReflection
{
	public class XmlNamespace
	{
		public XmlNamespace(string prefix, string ns)
		{
			Prefix = prefix;
			Namespace = ns;
		}

		public string Prefix { get; }

		public string Namespace { get; }

		public override bool Equals(object obj)
		{
			XmlNamespace o = obj as XmlNamespace;
			if (o == null)
				return false;
			return o.Prefix.Equals(Prefix) && o.Namespace.Equals(Namespace);
		}

		public override int GetHashCode()
		{
			return Prefix.GetHashCode() + Namespace.GetHashCode() >> 20;
		}
	}
}