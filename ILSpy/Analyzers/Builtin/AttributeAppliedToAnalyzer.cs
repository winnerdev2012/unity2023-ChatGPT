﻿// Copyright (c) 2018 Siegfried Pammer
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
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.ILSpy.Analyzers.Builtin
{
	[ExportAnalyzer(Header = "Applied To", Order = 10)]
	class AttributeAppliedToAnalyzer : IAnalyzer
	{
		public IEnumerable<ISymbol> Analyze(ISymbol analyzedSymbol, AnalyzerContext context)
		{
			if (!(analyzedSymbol is IEntity attributeEntity))
				yield break;

			var scope = context.GetScopeOf(attributeEntity);
			var genericContext = new GenericContext(); // type arguments do not matter for this analyzer.

			foreach (var module in scope.GetModulesInScope(context.CancellationToken)) {
				var ts = new DecompilerTypeSystem(module, module.GetAssemblyResolver());
				foreach (var h in module.Metadata.CustomAttributes) {
					var customAttribute = module.Metadata.GetCustomAttribute(h);
					var attributeCtor = ts.MainModule.ResolveMethod(customAttribute.Constructor, genericContext);
					if (attributeCtor.DeclaringTypeDefinition != null
						&& attributeCtor.ParentModule.PEFile == attributeEntity.ParentModule.PEFile
						&& attributeCtor.DeclaringTypeDefinition.MetadataToken == attributeEntity.MetadataToken) {
						var parent = GetParentEntity(ts, customAttribute);
						if (parent != null)
							yield return parent;
					}
				}
			}
		}

		ISymbol GetParentEntity(DecompilerTypeSystem ts, CustomAttribute customAttribute)
		{
			switch (customAttribute.Parent.Kind) {
				case HandleKind.MethodDefinition:
				case HandleKind.FieldDefinition:
				case HandleKind.PropertyDefinition:
				case HandleKind.EventDefinition:
				case HandleKind.TypeDefinition:
					return ts.MainModule.ResolveEntity(customAttribute.Parent);
				case HandleKind.AssemblyDefinition:
				case HandleKind.ModuleDefinition:
					return ts.MainModule;
				default:
					return null;
			}
		}

		public bool Show(ISymbol symbol)
		{
			return symbol is ITypeDefinition type && type.GetNonInterfaceBaseTypes()
				.Any(t => t.IsKnownType(KnownTypeCode.Attribute));
		}
	}
}
