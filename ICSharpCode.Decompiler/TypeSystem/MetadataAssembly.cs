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
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem.Implementation;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.TypeSystem
{
	/// <summary>
	/// Used as context object for metadata TS entities;
	/// should be turned into IAssembly implementation when the TS refactoring is complete.
	/// </summary>
	[DebuggerDisplay("<MetadataAssembly: {AssemblyName}>")]
	public class MetadataAssembly : IAssembly
	{
		public ICompilation Compilation { get; }
		internal readonly MetadataReader metadata;
		readonly TypeSystemOptions options;
		internal readonly TypeProvider TypeProvider;

		readonly MetadataNamespace rootNamespace;
		readonly MetadataTypeDefinition[] typeDefs;
		readonly MetadataField[] fieldDefs;
		readonly MetadataMethod[] methodDefs;
		readonly MetadataProperty[] propertyDefs;
		readonly MetadataEvent[] eventDefs;

		internal MetadataAssembly(ICompilation compilation, Metadata.PEFile peFile, TypeSystemOptions options)
		{
			this.Compilation = compilation;
			this.PEFile = peFile;
			this.metadata = peFile.Metadata;
			this.options = options;
			this.TypeProvider = new TypeProvider(this);

			// assembly metadata
			if (metadata.IsAssembly) {
				var asmdef = metadata.GetAssemblyDefinition();
				this.AssemblyName = metadata.GetString(asmdef.Name);
				this.FullAssemblyName = metadata.GetFullAssemblyName();
			} else {
				var moddef = metadata.GetModuleDefinition();
				this.AssemblyName = metadata.GetString(moddef.Name);
				this.FullAssemblyName = this.AssemblyName;
			}
			this.rootNamespace = new MetadataNamespace(this, null, string.Empty, metadata.GetNamespaceDefinitionRoot());

			// create arrays for resolved entities, indexed by row index
			this.typeDefs = new MetadataTypeDefinition[metadata.TypeDefinitions.Count + 1];
			this.fieldDefs = new MetadataField[metadata.FieldDefinitions.Count + 1];
			this.methodDefs = new MetadataMethod[metadata.MethodDefinitions.Count + 1];
			this.propertyDefs = new MetadataProperty[metadata.PropertyDefinitions.Count + 1];
			this.eventDefs = new MetadataEvent[metadata.EventDefinitions.Count + 1];
		}

		internal string GetString(StringHandle name)
		{
			return metadata.GetString(name);
		}

		public TypeSystemOptions TypeSystemOptions => options;

		#region IAssembly interface
		public PEFile PEFile { get; }

		public bool IsMainAssembly => this == Compilation.MainAssembly;

		public string AssemblyName { get; }
		public string FullAssemblyName { get; }

		public INamespace RootNamespace => rootNamespace;

		public IEnumerable<ITypeDefinition> TopLevelTypeDefinitions => TypeDefinitions.Where(td => td.DeclaringTypeDefinition == null);
		
		public ITypeDefinition GetTypeDefinition(TopLevelTypeName topLevelTypeName)
		{
			var typeDefHandle = PEFile.GetTypeDefinition(topLevelTypeName);
			if (typeDefHandle.IsNil) {
				var forwarderHandle = PEFile.GetTypeForwarder(topLevelTypeName);
				if (!forwarderHandle.IsNil) {
					var forwarder = metadata.GetExportedType(forwarderHandle);
					return ResolveForwardedType(forwarder).GetDefinition();
				}
			}
			return GetDefinition(typeDefHandle);
		}
		#endregion

		#region InternalsVisibleTo
		public bool InternalsVisibleTo(IAssembly assembly)
		{
			if (this == assembly)
				return true;
			foreach (string shortName in GetInternalsVisibleTo()) {
				if (string.Equals(assembly.AssemblyName, shortName, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		string[] internalsVisibleTo;

		string[] GetInternalsVisibleTo()
		{
			var result = LazyInit.VolatileRead(ref this.internalsVisibleTo);
			if (result != null) {
				return result;
			}
			if (metadata.IsAssembly) {
				var list = new List<string>();
				foreach (var attrHandle in metadata.GetAssemblyDefinition().GetCustomAttributes()) {
					var attr = metadata.GetCustomAttribute(attrHandle);
					if (attr.IsKnownAttribute(metadata, KnownAttribute.InternalsVisibleTo)) {
						var attrValue = attr.DecodeValue(this.TypeProvider);
						if (attrValue.FixedArguments.Length == 1) {
							if (attrValue.FixedArguments[0].Value is string s) {
								list.Add(s);
							}
						}
					}
				}
				result = list.ToArray();
			} else {
				result = Empty<string>.Array;
			}
			return LazyInit.GetOrSet(ref this.internalsVisibleTo, result);
		}
		#endregion

		#region GetDefinition
		/// <summary>
		/// Gets all types in the assembly, including nested types.
		/// </summary>
		public IEnumerable<ITypeDefinition> TypeDefinitions {
			get {
				for (int row = 1; row < typeDefs.Length; row++) {
					var typeDef = LazyInit.VolatileRead(ref typeDefs[row]);
					if (typeDef != null) {
						yield return typeDef;
					} else {
						typeDef = new MetadataTypeDefinition(this, MetadataTokens.TypeDefinitionHandle(row));
						yield return LazyInit.GetOrSet(ref typeDefs[row], typeDef);
					}
				}
			}
		}

		public ITypeDefinition GetDefinition(TypeDefinitionHandle handle)
		{
			int row = MetadataTokens.GetRowNumber(handle);
			if (row >= typeDefs.Length)
				return null;
			var typeDef = LazyInit.VolatileRead(ref typeDefs[row]);
			if (typeDef != null || handle.IsNil)
				return typeDef;
			typeDef = new MetadataTypeDefinition(this, handle);
			return LazyInit.GetOrSet(ref typeDefs[row], typeDef);
		}

		public IField GetDefinition(FieldDefinitionHandle handle)
		{
			int row = MetadataTokens.GetRowNumber(handle);
			if (row >= fieldDefs.Length)
				return null;
			var field = LazyInit.VolatileRead(ref fieldDefs[row]);
			if (field != null || handle.IsNil)
				return field;
			field = new MetadataField(this, handle);
			return LazyInit.GetOrSet(ref fieldDefs[row], field);
		}

		public IMethod GetDefinition(MethodDefinitionHandle handle)
		{
			int row = MetadataTokens.GetRowNumber(handle);
			if (row >= methodDefs.Length)
				return null;
			var method = LazyInit.VolatileRead(ref methodDefs[row]);
			if (method != null || handle.IsNil)
				return method;
			method = new MetadataMethod(this, handle);
			return LazyInit.GetOrSet(ref methodDefs[row], method);
		}

		public IProperty GetDefinition(PropertyDefinitionHandle handle)
		{
			int row = MetadataTokens.GetRowNumber(handle);
			if (row >= methodDefs.Length)
				return null;
			var property = LazyInit.VolatileRead(ref propertyDefs[row]);
			if (property != null || handle.IsNil)
				return property;
			property = new MetadataProperty(this, handle);
			return LazyInit.GetOrSet(ref propertyDefs[row], property);
		}

		public IEvent GetDefinition(EventDefinitionHandle handle)
		{
			int row = MetadataTokens.GetRowNumber(handle);
			if (row >= methodDefs.Length)
				return null;
			var ev = LazyInit.VolatileRead(ref eventDefs[row]);
			if (ev != null || handle.IsNil)
				return ev;
			ev = new MetadataEvent(this, handle);
			return LazyInit.GetOrSet(ref eventDefs[row], ev);
		}
		#endregion

		#region Resolve Type
		public IType ResolveType(EntityHandle typeRefDefSpec, GenericContext context, CustomAttributeHandleCollection? typeAttributes = null)
		{
			return ResolveType(typeRefDefSpec, context, options, typeAttributes);
		}

		public IType ResolveType(EntityHandle typeRefDefSpec, GenericContext context, TypeSystemOptions customOptions,  CustomAttributeHandleCollection? typeAttributes = null)
		{
			if (typeRefDefSpec.Kind == HandleKind.ExportedType) {
				return ResolveForwardedType(metadata.GetExportedType((ExportedTypeHandle)typeRefDefSpec));
			}
			return MetadataTypeReference.Resolve(typeRefDefSpec, metadata, TypeProvider, context, customOptions, typeAttributes);
		}

		IType ResolveDeclaringType(EntityHandle declaringTypeReference, GenericContext context)
		{
			// resolve without substituting dynamic/tuple types
			return ResolveType(declaringTypeReference, context,
				options & ~(TypeSystemOptions.Dynamic | TypeSystemOptions.Tuple));
		}
		#endregion

		#region Resolve Method
		public IMethod ResolveMethod(EntityHandle methodReference, GenericContext context = default)
		{
			if (methodReference.IsNil)
				throw new ArgumentNullException(nameof(methodReference));
			switch (methodReference.Kind) {
				case HandleKind.MethodDefinition:
					return ResolveMethodDefinition((MethodDefinitionHandle)methodReference, expandVarArgs: true);
				case HandleKind.MemberReference:
					return ResolveMethodReference((MemberReferenceHandle)methodReference, context, expandVarArgs: true);
				case HandleKind.MethodSpecification:
					return ResolveMethodSpecification((MethodSpecificationHandle)methodReference, context, expandVarArgs: true);
				default:
					throw new BadImageFormatException("Metadata token must be either a methoddef, memberref or methodspec");
			}
		}

		IMethod ResolveMethodDefinition(MethodDefinitionHandle methodDefHandle, bool expandVarArgs)
		{
			var method = GetDefinition(methodDefHandle);
			if (method == null) {
				throw new NotImplementedException();
			}
			if (expandVarArgs && method.Parameters.LastOrDefault()?.Type.Kind == TypeKind.ArgList) {
				method = new VarArgInstanceMethod(method, EmptyList<IType>.Instance);
			}
			return method;
		}

		IMethod ResolveMethodSpecification(MethodSpecificationHandle methodSpecHandle, GenericContext context, bool expandVarArgs)
		{
			var methodSpec = metadata.GetMethodSpecification(methodSpecHandle);
			var methodTypeArgs = methodSpec.DecodeSignature(TypeProvider, context);
			IMethod method;
			if (methodSpec.Method.Kind == HandleKind.MethodDefinition) {
				// generic instance of a methoddef (=generic method in non-generic class in current assembly)
				method = ResolveMethodDefinition((MethodDefinitionHandle)methodSpec.Method, expandVarArgs);
				method = method.Specialize(new TypeParameterSubstitution(context.ClassTypeParameters, methodTypeArgs));
			} else {
				method = ResolveMethodReference((MemberReferenceHandle)methodSpec.Method, context, methodTypeArgs, expandVarArgs);
			}
			return method;
		}

		/// <summary>
		/// Resolves a method reference.
		/// </summary>
		/// <remarks>
		/// Class type arguments are provided by the declaring type stored in the memberRef.
		/// Method type arguments are provided by the caller.
		/// </remarks>
		IMethod ResolveMethodReference(MemberReferenceHandle memberRefHandle, GenericContext context, IReadOnlyList<IType> methodTypeArguments = null, bool expandVarArgs = true)
		{
			var memberRef = metadata.GetMemberReference(memberRefHandle);
			Debug.Assert(memberRef.GetKind() == MemberReferenceKind.Method);
			MethodSignature<IType> signature;
			IReadOnlyList<IType> classTypeArguments = null;
			IMethod method;
			if (memberRef.Parent.Kind == HandleKind.MethodDefinition) {
				method = ResolveMethodDefinition((MethodDefinitionHandle)memberRef.Parent, expandVarArgs: false);
				signature = memberRef.DecodeMethodSignature(TypeProvider, context);
			} else {
				var declaringType = ResolveDeclaringType(memberRef.Parent, context);
				var declaringTypeDefinition = declaringType.GetDefinition();
				if (declaringType.TypeArguments.Count > 0) {
					classTypeArguments = declaringType.TypeArguments;
				}
				// Note: declaringType might be parameterized, but the signature is for the original method definition.
				// We'll have to search the member directly on declaringTypeDefinition.
				string name = metadata.GetString(memberRef.Name);
				signature = memberRef.DecodeMethodSignature(TypeProvider,
					new GenericContext(declaringTypeDefinition?.TypeParameters));
				if (declaringTypeDefinition != null) {
					// Find the set of overloads to search:
					IEnumerable<IMethod> methods;
					if (name == ".ctor") {
						methods = declaringTypeDefinition.GetConstructors();
					} else if (name == ".cctor") {
						methods = declaringTypeDefinition.Methods.Where(m => m.IsConstructor && m.IsStatic);
					} else {
						methods = declaringTypeDefinition.GetMethods(m => m.Name == name, GetMemberOptions.IgnoreInheritedMembers)
							.Concat(declaringTypeDefinition.GetAccessors(m => m.Name == name, GetMemberOptions.IgnoreInheritedMembers));
					}
					// Determine the expected parameters from the signature:
					ImmutableArray<IType> parameterTypes;
					if (signature.Header.CallingConvention == SignatureCallingConvention.VarArgs) {
						parameterTypes = signature.ParameterTypes
							.Take(signature.RequiredParameterCount)
							.Concat(new[] { SpecialType.ArgList })
							.ToImmutableArray();
					} else {
						parameterTypes = signature.ParameterTypes;
					}
					// Search for the matching method:
					method = null;
					foreach (var m in methods) {
						if (m.TypeParameters.Count != signature.GenericParameterCount)
							continue;
						if (CompareSignatures(m.Parameters, parameterTypes) && CompareTypes(m.ReturnType, signature.ReturnType)) {
							method = m;
							break;
						}
					}
				} else {
					method = null;
				}
				if (method == null) {
					method = CreateFakeMethod(declaringType, name, signature);
				}
			}
			if (classTypeArguments != null || methodTypeArguments != null) {
				method = method.Specialize(new TypeParameterSubstitution(classTypeArguments, methodTypeArguments));
			}
			if (expandVarArgs && signature.Header.CallingConvention == SignatureCallingConvention.VarArgs) {
				method = new VarArgInstanceMethod(method, signature.ParameterTypes.Skip(signature.RequiredParameterCount));
			}
			return method;
		}

		static readonly NormalizeTypeVisitor normalizeTypeVisitor = new NormalizeTypeVisitor {
			ReplaceClassTypeParametersWithDummy = true,
			ReplaceMethodTypeParametersWithDummy = true,
		};

		static bool CompareTypes(IType a, IType b)
		{
			IType type1 = a.AcceptVisitor(normalizeTypeVisitor);
			IType type2 = b.AcceptVisitor(normalizeTypeVisitor);
			return type1.Equals(type2);
		}

		static bool CompareSignatures(IReadOnlyList<IParameter> parameters, ImmutableArray<IType> parameterTypes)
		{
			if (parameterTypes.Length != parameters.Count)
				return false;
			for (int i = 0; i < parameterTypes.Length; i++) {
				if (!CompareTypes(parameterTypes[i], parameters[i].Type))
					return false;
			}
			return true;
		}

		/// <summary>
		/// Create a dummy IMethod from the specified MethodReference
		/// </summary>
		IMethod CreateFakeMethod(IType declaringType, string name, MethodSignature<IType> signature)
		{
			SymbolKind symbolKind = SymbolKind.Method;
			if (name == ".ctor" || name == ".cctor")
				symbolKind = SymbolKind.Constructor;
			var m = new FakeMethod(Compilation, symbolKind);
			m.DeclaringType = declaringType;
			m.Name = name;
			m.ReturnType = signature.ReturnType;
			m.IsStatic = !signature.Header.IsInstance;

			TypeParameterSubstitution substitution = null;
			if (signature.GenericParameterCount > 0) {
				var typeParameters = new List<ITypeParameter>();
				for (int i = 0; i < signature.GenericParameterCount; i++) {
					typeParameters.Add(new DefaultTypeParameter(m, i));
				}
				m.TypeParameters = typeParameters;
				substitution = new TypeParameterSubstitution(null, typeParameters);
			}
			var parameters = new List<IParameter>();
			for (int i = 0; i < signature.RequiredParameterCount; i++) {
				var type = signature.ParameterTypes[i];
				if (substitution != null) {
					// replace the dummy method type parameters with the owned instances we just created
					type = type.AcceptVisitor(substitution);
				}
				parameters.Add(new DefaultParameter(type, ""));
			}
			m.Parameters = parameters;
			return m;
		}
		#endregion

		#region Resolve Entity
		/// <summary>
		/// Resolves a symbol.
		/// </summary>
		/// <remarks>
		/// * Types are resolved to their definition, as IType does not implement ISymbol.
		///    * types without definition will resolve to <c>null</c>
		///    * use ResolveType() properly resolve types
		/// * When resolving methods, varargs signatures are not expanded.
		///    * use ResolveMethod() instead to get an IMethod instance suitable for call-sites
		/// * May return specialized members, where generics are involved.
		/// * Other types of handles that don't correspond to TS entities, will return <c>null</c>.
		/// </remarks>
		public IEntity ResolveEntity(EntityHandle entityHandle, GenericContext context)
		{
			switch (entityHandle.Kind) {
				case HandleKind.TypeReference:
				case HandleKind.TypeDefinition:
				case HandleKind.TypeSpecification:
				case HandleKind.ExportedType:
					return ResolveType(entityHandle, context).GetDefinition();
				case HandleKind.MemberReference:
					var memberReferenceHandle = (MemberReferenceHandle)entityHandle;
					switch (metadata.GetMemberReference(memberReferenceHandle).GetKind()) {
						case MemberReferenceKind.Method:
							// for consistency with the MethodDefinition case, never expand varargs
							return ResolveMethodReference(memberReferenceHandle, context, expandVarArgs: false);
						case MemberReferenceKind.Field:
							return ResolveFieldReference(memberReferenceHandle, context);
						default:
							throw new BadImageFormatException("Unknown MemberReferenceKind");
					}
				case HandleKind.MethodDefinition:
					return GetDefinition((MethodDefinitionHandle)entityHandle);
				case HandleKind.MethodSpecification:
					return ResolveMethodSpecification((MethodSpecificationHandle)entityHandle, context, expandVarArgs: false);
				case HandleKind.FieldDefinition:
					return GetDefinition((FieldDefinitionHandle)entityHandle);
				case HandleKind.EventDefinition:
					return GetDefinition((EventDefinitionHandle)entityHandle);
				case HandleKind.PropertyDefinition:
					return GetDefinition((PropertyDefinitionHandle)entityHandle);
				default:
					return null;
			}
		}

		IField ResolveFieldReference(MemberReferenceHandle memberReferenceHandle, GenericContext context)
		{
			var memberRef = metadata.GetMemberReference(memberReferenceHandle);
			var declaringType = ResolveDeclaringType(memberRef.Parent, context);
			var declaringTypeDefinition = declaringType.GetDefinition();
			string name = metadata.GetString(memberRef.Name);
			// field signature is for the definition, not the generic instance
			var signature = memberRef.DecodeFieldSignature(TypeProvider,
				new GenericContext(declaringTypeDefinition?.TypeParameters));
			// 'f' in the predicate is also the definition, even if declaringType is a ParameterizedType
			var field = declaringType.GetFields(f => f.Name == name && CompareTypes(f.ReturnType, signature),
				GetMemberOptions.IgnoreInheritedMembers).FirstOrDefault();
			if (field == null) {
				field = new FakeField(Compilation) {
					ReturnType = signature,
					Name = name,
					DeclaringType = declaringType,
				};
			}
			return field;
		}
		#endregion

		#region Module / Assembly attributes
		IAttribute[] assemblyAttributes;
		IAttribute[] moduleAttributes;

		/// <summary>
		/// Gets the list of all assembly attributes in the project.
		/// </summary>
		public IReadOnlyList<IAttribute> AssemblyAttributes {
			get {
				var attrs = LazyInit.VolatileRead(ref this.assemblyAttributes);
				if (attrs != null)
					return attrs;
				var b = new AttributeListBuilder(this);
				if (metadata.IsAssembly) {
					var assembly = metadata.GetAssemblyDefinition();
					b.Add(metadata.GetCustomAttributes(Handle.AssemblyDefinition));
					b.AddSecurityAttributes(assembly.GetDeclarativeSecurityAttributes());

					// AssemblyVersionAttribute
					if (assembly.Version != null) {
						b.Add(KnownAttribute.AssemblyVersion, KnownTypeCode.String, assembly.Version.ToString());
					}

					AddTypeForwarderAttributes(ref b);
				}
				return LazyInit.GetOrSet(ref this.assemblyAttributes, b.Build());
			}
		}

		/// <summary>
		/// Gets the list of all module attributes in the project.
		/// </summary>
		public IReadOnlyList<IAttribute> ModuleAttributes {
			get {
				var attrs = LazyInit.VolatileRead(ref this.moduleAttributes);
				if (attrs != null)
					return attrs;
				var b = new AttributeListBuilder(this);
				b.Add(metadata.GetCustomAttributes(Handle.ModuleDefinition));
				if (!metadata.IsAssembly) {
					AddTypeForwarderAttributes(ref b);
				}
				return LazyInit.GetOrSet(ref this.moduleAttributes, b.Build());
			}
		}

		void AddTypeForwarderAttributes(ref AttributeListBuilder b)
		{
			foreach (ExportedTypeHandle t in metadata.ExportedTypes) {
				var type = metadata.GetExportedType(t);
				if (type.IsForwarder) {
					b.Add(KnownAttribute.TypeForwardedTo, KnownTypeCode.Type, ResolveForwardedType(type));
				}
			}
		}

		IType ResolveForwardedType(ExportedType forwarder)
		{
			IAssembly assembly = ResolveAssembly(forwarder);
			Debug.Assert(assembly != null);
			var typeName = forwarder.GetFullTypeName(metadata);
			using (var busyLock = BusyManager.Enter(this)) {
				if (busyLock.Success) {
					var td = assembly.GetTypeDefinition(typeName);
					if (td != null) {
						return td;
					}
				}
			}
			return new UnknownType(typeName);

			IAssembly ResolveAssembly(ExportedType type)
			{
				switch (type.Implementation.Kind) {
					case HandleKind.AssemblyFile:
						return this;
					case HandleKind.ExportedType:
						var outerType = metadata.GetExportedType((ExportedTypeHandle)type.Implementation);
						return ResolveAssembly(outerType);
					case HandleKind.AssemblyReference:
						var asmRef = metadata.GetAssemblyReference((AssemblyReferenceHandle)type.Implementation);
						string shortName = metadata.GetString(asmRef.Name);
						foreach (var asm in Compilation.Assemblies) {
							if (string.Equals(asm.AssemblyName, shortName, StringComparison.OrdinalIgnoreCase)) {
								return asm;
							}
						}
						return null;
					default:
						throw new NotSupportedException();
				}
			}
		}
		#endregion

		#region Attribute Helpers
		/// <summary>
		/// Cache for parameterless known attribute types.
		/// </summary>
		readonly IType[] knownAttributeTypes = new IType[KnownAttributes.Count];

		internal IType GetAttributeType(KnownAttribute attr)
		{
			var ty = LazyInit.VolatileRead(ref knownAttributeTypes[(int)attr]);
			if (ty != null)
				return ty;
			ty = Compilation.FindType(attr.GetTypeName());
			return LazyInit.GetOrSet(ref knownAttributeTypes[(int)attr], ty);
		}

		/// <summary>
		/// Cache for parameterless known attributes.
		/// </summary>
		readonly IAttribute[] knownAttributes = new IAttribute[KnownAttributes.Count];

		/// <summary>
		/// Construct a builtin attribute.
		/// </summary>
		internal IAttribute MakeAttribute(KnownAttribute type)
		{
			var attr = LazyInit.VolatileRead(ref knownAttributes[(int)type]);
			if (attr != null)
				return attr;
			attr = new DefaultAttribute(GetAttributeType(type),
				ImmutableArray.Create<CustomAttributeTypedArgument<IType>>(), 
				ImmutableArray.Create<CustomAttributeNamedArgument<IType>>());
			return LazyInit.GetOrSet(ref knownAttributes[(int)type], attr);
		}
		#endregion

		#region Visibility Filter
		internal bool IncludeInternalMembers => (options & TypeSystemOptions.OnlyPublicAPI) == 0;

		internal bool IsVisible(FieldAttributes att)
		{
			att &= FieldAttributes.FieldAccessMask;
			return IncludeInternalMembers
				|| att == FieldAttributes.Public
				|| att == FieldAttributes.Family
				|| att == FieldAttributes.FamORAssem;
		}

		internal bool IsVisible(MethodAttributes att)
		{
			att &= MethodAttributes.MemberAccessMask;
			return IncludeInternalMembers
				|| att == MethodAttributes.Public
				|| att == MethodAttributes.Family
				|| att == MethodAttributes.FamORAssem;
		}
		#endregion
	}
}
