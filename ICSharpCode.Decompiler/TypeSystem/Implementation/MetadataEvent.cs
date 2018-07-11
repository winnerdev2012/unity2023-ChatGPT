﻿// Copyright (c) 2018 Daniel Grunwald
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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	sealed class MetadataEvent : IEvent
	{
		readonly MetadataAssembly assembly;
		readonly EventDefinitionHandle handle;
		readonly EventAccessors accessors;
		readonly string name;

		// lazy-loaded:
		IType returnType;

		internal MetadataEvent(MetadataAssembly assembly, EventDefinitionHandle handle)
		{
			Debug.Assert(assembly != null);
			Debug.Assert(!handle.IsNil);
			this.assembly = assembly;
			this.handle = handle;

			var metadata = assembly.metadata;
			var ev = metadata.GetEventDefinition(handle);
			accessors = ev.GetAccessors();
			name = metadata.GetString(ev.Name);
		}

		public override string ToString()
		{
			return $"{MetadataTokens.GetToken(handle):X8} {DeclaringType?.ReflectionName}.{Name}";
		}

		public EntityHandle MetadataToken => handle;
		public string Name => name;
		
		SymbolKind ISymbol.SymbolKind => SymbolKind.Event;

		public bool CanAdd => !accessors.Adder.IsNil;
		public bool CanRemove => !accessors.Remover.IsNil;
		public bool CanInvoke => !accessors.Raiser.IsNil;
		public IMethod AddAccessor => assembly.GetDefinition(accessors.Adder);
		public IMethod RemoveAccessor => assembly.GetDefinition(accessors.Remover);
		public IMethod InvokeAccessor => assembly.GetDefinition(accessors.Raiser);
		IMethod AnyAccessor => assembly.GetDefinition(accessors.GetAny());

		#region Signature (ReturnType + Parameters)
		public IType ReturnType {
			get {
				var returnType = LazyInit.VolatileRead(ref this.returnType);
				if (returnType != null)
					return returnType;
				var metadata = assembly.metadata;
				var ev = metadata.GetEventDefinition(handle);
				var context = new GenericContext(DeclaringTypeDefinition?.TypeParameters);
				returnType = assembly.ResolveType(ev.Type, context, ev.GetCustomAttributes());
				return LazyInit.GetOrSet(ref this.returnType, returnType);
			}
		}
		#endregion

		public bool IsExplicitInterfaceImplementation => AnyAccessor?.IsExplicitInterfaceImplementation ?? false;
		public IEnumerable<IMember> ExplicitlyImplementedInterfaceMembers => GetInterfaceMembersFromAccessor(AnyAccessor);

		internal static IEnumerable<IMember> GetInterfaceMembersFromAccessor(IMethod method)
		{
			if (method == null)
				return EmptyList<IMember>.Instance;
			return method.ExplicitlyImplementedInterfaceMembers.Select(m => ((IMethod)m).AccessorOwner).Where(m => m != null);
		}

		public ITypeDefinition DeclaringTypeDefinition => AnyAccessor?.DeclaringTypeDefinition;
		public IType DeclaringType => AnyAccessor?.DeclaringType;
		IMember IMember.MemberDefinition => this;
		TypeParameterSubstitution IMember.Substitution => TypeParameterSubstitution.Identity;

		#region Attributes
		public IEnumerable<IAttribute> GetAttributes()
		{
			var b = new AttributeListBuilder(assembly);
			var metadata = assembly.metadata;
			var eventDef = metadata.GetEventDefinition(handle);
			b.Add(eventDef.GetCustomAttributes());
			return b.Build();
		}
		#endregion

		public Accessibility Accessibility => AnyAccessor?.Accessibility ?? Accessibility.None;
		public bool IsStatic => AnyAccessor?.IsStatic ?? false;
		public bool IsAbstract => AnyAccessor?.IsAbstract ?? false;
		public bool IsSealed => AnyAccessor?.IsSealed ?? false;
		public bool IsVirtual => AnyAccessor?.IsVirtual ?? false;
		public bool IsOverride => AnyAccessor?.IsOverride ?? false;
		public bool IsOverridable => AnyAccessor?.IsOverridable ?? false;

		public IAssembly ParentAssembly => assembly;
		public ICompilation Compilation => assembly.Compilation;

		public string FullName => $"{DeclaringType?.FullName}.{Name}";
		public string ReflectionName => $"{DeclaringType?.ReflectionName}.{Name}";
		public string Namespace => DeclaringType?.Namespace ?? string.Empty;

		public override bool Equals(object obj)
		{
			if (obj is MetadataEvent ev) {
				return handle == ev.handle && assembly.PEFile == ev.assembly.PEFile;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return 0x7937039a ^ assembly.PEFile.GetHashCode() ^ handle.GetHashCode();
		}

		bool IMember.Equals(IMember obj, TypeVisitor typeNormalization)
		{
			return Equals(obj);
		}

		public IMember Specialize(TypeParameterSubstitution substitution)
		{
			return SpecializedEvent.Create(this, substitution);
		}
	}
}
