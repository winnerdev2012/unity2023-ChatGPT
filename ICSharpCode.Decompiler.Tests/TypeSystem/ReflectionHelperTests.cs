﻿// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;

using NUnit.Framework;

namespace ICSharpCode.Decompiler.Tests.TypeSystem
{
	[TestFixture]
	public unsafe class ReflectionHelperTests
	{
		ICompilation compilation = new SimpleCompilation(TypeSystemLoaderTests.Mscorlib);

		void TestFindType(Type type)
		{
			IType t = compilation.FindType(type);
			Assert.IsNotNull(t, type.FullName);
			Assert.AreEqual(type.FullName, t.ReflectionName);
		}

		[Test]
		public void TestGetInnerClass()
		{
			TestFindType(typeof(Environment.SpecialFolder));
		}

		[Test]
		public void TestGetGenericClass1()
		{
			TestFindType(typeof(Action<>));
		}

		[Test]
		public void TestGetGenericClass2()
		{
			TestFindType(typeof(Action<,>));
		}

		[Test]
		public void TestGetInnerClassInGenericClass1()
		{
			TestFindType(typeof(Dictionary<,>.ValueCollection));
		}

		[Test]
		public void TestGetInnerClassInGenericClass2()
		{
			TestFindType(typeof(Dictionary<,>.ValueCollection.Enumerator));
		}

		[Test]
		public void TestToTypeReferenceInnerClass()
		{
			Assert.AreEqual("System.Environment+SpecialFolder",
							compilation.FindType(typeof(Environment.SpecialFolder)).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceUnboundGenericClass()
		{
			Assert.AreEqual("System.Action`1",
							compilation.FindType(typeof(Action<>)).ReflectionName);
			Assert.AreEqual("System.Action`2",
							compilation.FindType(typeof(Action<,>)).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceBoundGenericClass()
		{
			Assert.AreEqual("System.Action`1[[System.String]]",
							compilation.FindType(typeof(Action<string>)).ReflectionName);
			Assert.AreEqual("System.Action`2[[System.Int32],[System.Int16]]",
							compilation.FindType(typeof(Action<int, short>)).ReflectionName);
		}


		[Test]
		public void TestToTypeReferenceNullableType()
		{
			Assert.AreEqual("System.Nullable`1[[System.Int32]]",
							compilation.FindType(typeof(int?)).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceInnerClassInUnboundGenericType()
		{
			Assert.AreEqual("System.Collections.Generic.Dictionary`2+ValueCollection",
							compilation.FindType(typeof(Dictionary<,>.ValueCollection)).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceInnerClassInBoundGenericType()
		{
			Assert.AreEqual("System.Collections.Generic.Dictionary`2+KeyCollection[[System.String],[System.Int32]]",
							compilation.FindType(typeof(Dictionary<string, int>.KeyCollection)).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceArrayType()
		{
			Assert.AreEqual(typeof(int[]).FullName,
							compilation.FindType(typeof(int[])).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceMultidimensionalArrayType()
		{
			Assert.AreEqual(typeof(int[,]).FullName,
							compilation.FindType(typeof(int[,])).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceJaggedMultidimensionalArrayType()
		{
			Assert.AreEqual(typeof(int[,][,,]).FullName,
							compilation.FindType(typeof(int[,][,,])).ReflectionName);
		}

		[Test]
		public void TestToTypeReferencePointerType()
		{
			Assert.AreEqual(typeof(int*).FullName,
							compilation.FindType(typeof(int*)).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceByReferenceType()
		{
			Assert.AreEqual(typeof(int).MakeByRefType().FullName,
							compilation.FindType(typeof(int).MakeByRefType()).ReflectionName);
		}

		[Test]
		public void TestToTypeReferenceGenericType()
		{
			MethodInfo convertAllInfo = typeof(List<>).GetMethod("ConvertAll");
			ITypeReference parameterType = convertAllInfo.GetParameters()[0].ParameterType.ToTypeReference(); // Converter[[`0],[``0]]
																											  // cannot resolve generic types without knowing the parent entity:
			IType resolvedWithoutEntity = parameterType.Resolve(new SimpleTypeResolveContext(compilation));
			Assert.AreEqual("System.Converter`2[[`0],[``0]]", resolvedWithoutEntity.ReflectionName);
			Assert.IsNull(((ITypeParameter)((ParameterizedType)resolvedWithoutEntity).GetTypeArgument(0)).Owner);
			// now try with parent entity:
			IMethod convertAll = compilation.FindType(typeof(List<>)).GetMethods(m => m.Name == "ConvertAll").Single();
			IType resolvedWithEntity = parameterType.Resolve(new SimpleTypeResolveContext(convertAll));
			Assert.AreEqual("System.Converter`2[[`0],[``0]]", resolvedWithEntity.ReflectionName);
			Assert.AreSame(convertAll.DeclaringTypeDefinition, ((ITypeParameter)((ParameterizedType)resolvedWithEntity).GetTypeArgument(0)).Owner);
		}

		[Test]
		public void ParseReflectionName()
		{
			var context = new SimpleTypeResolveContext(compilation.MainModule);
			Assert.AreEqual("System.Int32", ReflectionHelper.ParseReflectionName("System.Int32").Resolve(context).ReflectionName);
			Assert.AreEqual("System.Int32&", ReflectionHelper.ParseReflectionName("System.Int32&").Resolve(context).ReflectionName);
			Assert.AreEqual("System.Int32*&", ReflectionHelper.ParseReflectionName("System.Int32*&").Resolve(context).ReflectionName);
			Assert.AreEqual("System.Int32", ReflectionHelper.ParseReflectionName(typeof(int).AssemblyQualifiedName).Resolve(context).ReflectionName);
			Assert.AreEqual("System.Action`1[[System.String]]", ReflectionHelper.ParseReflectionName("System.Action`1[[System.String]]").Resolve(context).ReflectionName);
			Assert.AreEqual("System.Action`1[[System.String]]", ReflectionHelper.ParseReflectionName("System.Action`1[[System.String, mscorlib]]").Resolve(context).ReflectionName);
			Assert.AreEqual("System.Int32[,,][,]", ReflectionHelper.ParseReflectionName(typeof(int[,][,,]).AssemblyQualifiedName).Resolve(context).ReflectionName);
			Assert.AreEqual("System.Environment+SpecialFolder", ReflectionHelper.ParseReflectionName("System.Environment+SpecialFolder").Resolve(context).ReflectionName);
		}

		[Test]
		public void ParseOpenGenericReflectionName()
		{
			ITypeReference typeRef = ReflectionHelper.ParseReflectionName("System.Converter`2[[`0],[``0]]");
			Assert.AreEqual("System.Converter`2[[`0],[``0]]", typeRef.Resolve(new SimpleTypeResolveContext(compilation.MainModule)).ReflectionName);
			IMethod convertAll = compilation.FindType(typeof(List<>)).GetMethods(m => m.Name == "ConvertAll").Single();
			Assert.AreEqual("System.Converter`2[[`0],[``0]]", typeRef.Resolve(new SimpleTypeResolveContext(convertAll)).ReflectionName);
		}

		[Test]
		public void ArrayOfTypeParameter()
		{
			var context = new SimpleTypeResolveContext(compilation.MainModule);
			Assert.AreEqual("`0[,]", ReflectionHelper.ParseReflectionName("`0[,]").Resolve(context).ReflectionName);
		}

		[Test]
		public void ParseNullReflectionName()
		{
			Assert.Throws<ArgumentNullException>(() => ReflectionHelper.ParseReflectionName(null));
		}

		[Test]
		public void ParseInvalidReflectionName1()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName(string.Empty));
		}

		[Test]
		public void ParseInvalidReflectionName2()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("`"));
		}

		[Test]
		public void ParseInvalidReflectionName3()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("``"));
		}

		[Test]
		public void ParseInvalidReflectionName4()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Action`A"));
		}

		[Test]
		public void ParseInvalidReflectionName5()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Environment+"));
		}

		[Test]
		public void ParseInvalidReflectionName5b()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Environment+`"));
		}

		[Test]
		public void ParseInvalidReflectionName6()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Int32["));
		}

		[Test]
		public void ParseInvalidReflectionName7()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Int32[`]"));
		}

		[Test]
		public void ParseInvalidReflectionName8()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Int32[,"));
		}

		[Test]
		public void ParseInvalidReflectionName9()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Int32]"));
		}

		[Test]
		public void ParseInvalidReflectionName10()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Int32*a"));
		}

		[Test]
		public void ParseInvalidReflectionName11()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Action`1[[]]"));
		}

		[Test]
		public void ParseInvalidReflectionName12()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Action`1[[System.Int32]a]"));
		}

		[Test]
		public void ParseInvalidReflectionName13()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Action`1[[System.Int32],]"));
		}

		[Test]
		public void ParseInvalidReflectionName14()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Action`1[[System.Int32]"));
		}

		[Test]
		public void ParseInvalidReflectionName15()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Action`1[[System.Int32"));
		}

		[Test]
		public void ParseInvalidReflectionName16()
		{
			Assert.Throws<ReflectionNameParseException>(() => ReflectionHelper.ParseReflectionName("System.Action`1[[System.Int32],[System.String"));
		}
	}
}
