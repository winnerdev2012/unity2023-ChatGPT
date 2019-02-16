﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Tests.Helpers;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.DiaSymReader.Tools;
using NUnit.Framework;

namespace ICSharpCode.Decompiler.Tests
{
	[TestFixture]
	public class PdbGenerationTestRunner
	{
		static readonly string TestCasePath = Tester.TestCasePath + "/PdbGen";

		[Test]
		public void HelloWorld()
		{
			TestGeneratePdb();
		}

		private void TestGeneratePdb([CallerMemberName] string testName = null)
		{
			const PdbToXmlOptions options = PdbToXmlOptions.IncludeEmbeddedSources | PdbToXmlOptions.ThrowOnError | PdbToXmlOptions.IncludeTokens | PdbToXmlOptions.ResolveTokens | PdbToXmlOptions.IncludeMethodSpans;

			string xmlFile = Path.Combine(TestCasePath, testName + ".xml");
			string xmlContent = File.ReadAllText(xmlFile);
			XDocument document = XDocument.Parse(xmlContent);
			var files = document.Descendants("file").ToDictionary(f => f.Attribute("name").Value, f => f.Value);
			Tester.CompileCSharpWithPdb(Path.Combine(TestCasePath, testName + ".expected"), files, options);

			string peFileName = Path.Combine(TestCasePath, testName + ".expected.dll");
			string pdbFileName = Path.Combine(TestCasePath, testName + ".expected.pdb");
			var moduleDefinition = new PEFile(peFileName);
			var resolver = new UniversalAssemblyResolver(peFileName, false, moduleDefinition.Reader.DetectTargetFrameworkId(), PEStreamOptions.PrefetchEntireImage);
			var decompiler = new CSharpDecompiler(moduleDefinition, resolver, new DecompilerSettings());
			using (FileStream pdbStream = File.Open(Path.Combine(TestCasePath, testName + ".pdb"), FileMode.OpenOrCreate, FileAccess.ReadWrite)) {
				pdbStream.SetLength(0);
				PortablePdbWriter.WritePdb(moduleDefinition, decompiler, new DecompilerSettings(), pdbStream, noLogo: true);
				pdbStream.Position = 0;
				using (Stream peStream = File.OpenRead(peFileName))
				using (Stream expectedPdbStream = File.OpenRead(pdbFileName)) {
					using (StreamWriter writer = new StreamWriter(Path.ChangeExtension(pdbFileName, ".xml"), false, Encoding.UTF8)) {
						PdbToXmlConverter.ToXml(writer, expectedPdbStream, peStream, options);
					}
					peStream.Position = 0;
					using (StreamWriter writer = new StreamWriter(Path.ChangeExtension(xmlFile, ".generated.xml"), false, Encoding.UTF8)) {
						PdbToXmlConverter.ToXml(writer, pdbStream, peStream, options);
					}
				}
			}
			ProcessXmlFile(Path.ChangeExtension(pdbFileName, ".xml"));
			ProcessXmlFile(Path.ChangeExtension(xmlFile, ".generated.xml"));
			Assert.AreEqual(File.ReadAllText(xmlFile), File.ReadAllText(Path.ChangeExtension(xmlFile, ".generated.xml")));
		}

		private void ProcessXmlFile(string v)
		{
			var document = XDocument.Load(v);
			foreach (var file in document.Descendants("file")) {
				file.Attribute("checksum").Remove();
				file.Attribute("embeddedSourceLength").Remove();
				file.ReplaceNodes(new XCData(file.Value.Replace("\uFEFF", "")));
			}
			document.Save(v, SaveOptions.None);
		}
	}

	class StringWriterWithEncoding : StringWriter
	{
		readonly Encoding encoding;

		public StringWriterWithEncoding(Encoding encoding)
		{
			this.encoding = encoding ?? throw new ArgumentNullException("encoding");
		}

		public override Encoding Encoding => encoding;
	}
}
