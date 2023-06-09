﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.ILSpy.Analyzers;
using ICSharpCode.ILSpy.Analyzers.Builtin;
using ICSharpCode.ILSpyX;

using NUnit.Framework;

namespace ICSharpCode.ILSpy.Tests.Analyzers
{
	[TestFixture, Parallelizable(ParallelScope.All)]
	public class MethodUsesAnalyzerTests
	{
		AssemblyList assemblyList;
		CSharpLanguage language;
		LoadedAssembly testAssembly;
		ICompilation testAssemblyTypeSystem;
		ITypeDefinition typeDefinition;

		[OneTimeSetUp]
		public void Setup()
		{
			assemblyList = new AssemblyList();
			testAssembly = assemblyList.OpenAssembly(typeof(MethodUsesAnalyzerTests).Assembly.Location);
			assemblyList.OpenAssembly(typeof(void).Assembly.Location);
			testAssemblyTypeSystem = testAssembly.GetTypeSystemOrNull();
			language = new CSharpLanguage();
			typeDefinition = testAssemblyTypeSystem.FindType(typeof(TestCases.Main.MainAssembly)).GetDefinition();
		}

		[Test]
		public void MainAssemblyUsesSystemStringEmpty()
		{
			var context = new AnalyzerContext { AssemblyList = assemblyList, Language = language };
			IMethod symbol = typeDefinition.Methods.First(m => m.Name == "UsesSystemStringEmpty");

			var results = new MethodUsesAnalyzer().Analyze(symbol, context).ToList();

			Assert.IsTrue(results.Count == 1);
			var field = results.Single() as IField;
			Assert.IsNotNull(field);
			Assert.IsFalse(field.MetadataToken.IsNil);
			Assert.AreEqual(field.FullName, "System.String.Empty");
		}
	}
}
