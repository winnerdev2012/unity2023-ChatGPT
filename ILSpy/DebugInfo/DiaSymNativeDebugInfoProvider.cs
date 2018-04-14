﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Util;
using Microsoft.DiaSymReader;

namespace ICSharpCode.ILSpy.DebugInfo
{
	class DiaSymNativeDebugInfoProvider : IDebugInfoProvider, ISymReaderMetadataProvider
	{
		PEFile module;
		string pdbFileName;
		Stream stream;
		MetadataReader metadata;
		ISymUnmanagedReader5 reader;

		public DiaSymNativeDebugInfoProvider(PEFile module, string pdbFileName, Stream stream)
		{
			this.module = module;
			this.pdbFileName = pdbFileName;
			this.stream = stream;
			this.metadata = module.GetMetadataReader();
			this.reader = SymUnmanagedReaderFactory.CreateReader<ISymUnmanagedReader5>(stream, this);
		}

		public IList<Decompiler.Metadata.SequencePoint> GetSequencePoints(MethodDefinitionHandle handle)
		{
			var method = reader.GetMethod(MetadataTokens.GetToken(handle));
			if (method.GetSequencePointCount(out int count) != 0)
				return Empty<Decompiler.Metadata.SequencePoint>.Array;
			var sequencePoints = new Decompiler.Metadata.SequencePoint[count];
			var points = method.GetSequencePoints();
			int i = 0;
			var buffer = new char[1024];
			foreach (var point in points) {
				string url;
				if (point.Document.GetUrl(buffer.Length, out int length, buffer) == 0) {
					url = new string(buffer, 0, length - 1);
				} else {
					url = "";
				}
				sequencePoints[i] = new Decompiler.Metadata.SequencePoint() {
					Offset = point.Offset,
					StartLine = point.StartLine,
					StartColumn = point.StartColumn,
					EndLine = point.EndLine,
					EndColumn = point.EndColumn,
					DocumentUrl = url
				};
				
				i++;
			}
			return sequencePoints;
		}

		public IList<Variable> GetVariables(MethodDefinitionHandle handle)
		{
			var method = reader.GetMethod(MetadataTokens.GetToken(handle));
			var scopes = new Queue<ISymUnmanagedScope>(new[] { method.GetRootScope() });
			var variables = new List<Variable>();

			while (scopes.Count > 0) {
				var scope = scopes.Dequeue();

				foreach (var local in scope.GetLocals()) {
					variables.Add(new Variable() { Name = local.GetName() });
				}

				foreach (var s in scope.GetChildren())
					scopes.Enqueue(s);
			}

			return variables;
		}

		public bool TryGetName(MethodDefinitionHandle handle, int index, out string name)
		{
			var method = reader.GetMethod(MetadataTokens.GetToken(handle));
			var scopes = new Queue<ISymUnmanagedScope>(new[] { method.GetRootScope() });
			name = null;

			while (scopes.Count > 0) {
				var scope = scopes.Dequeue();

				foreach (var local in scope.GetLocals()) {
					if (local.GetSlot() == index) {
						name = local.GetName();
						return true;
					}
				}

				foreach (var s in scope.GetChildren())
					scopes.Enqueue(s);
			}

			return false;
		}

		unsafe bool ISymReaderMetadataProvider.TryGetStandaloneSignature(int standaloneSignatureToken, out byte* signature, out int length)
		{
			var handle = (StandaloneSignatureHandle)MetadataTokens.Handle(standaloneSignatureToken);
			if (handle.IsNil) {
				signature = null;
				length = 0;
				return false;
			}

			var sig = metadata.GetStandaloneSignature(handle);
			var blob = metadata.GetBlobReader(sig.Signature);

			signature = blob.StartPointer;
			length = blob.Length;
			return true;
		}

		bool ISymReaderMetadataProvider.TryGetTypeDefinitionInfo(int typeDefinitionToken, out string namespaceName, out string typeName, out TypeAttributes attributes)
		{
			var handle = (TypeDefinitionHandle)MetadataTokens.Handle(typeDefinitionToken);
			if (handle.IsNil) {
				namespaceName = null;
				typeName = null;
				attributes = 0;
				return false;
			}

			var typeDefinition = metadata.GetTypeDefinition(handle);
			namespaceName = metadata.GetString(typeDefinition.Namespace);
			typeName = metadata.GetString(typeDefinition.Name);
			attributes = typeDefinition.Attributes;
			return true;
		}

		bool ISymReaderMetadataProvider.TryGetTypeReferenceInfo(int typeReferenceToken, out string namespaceName, out string typeName)
		{
			var handle = (TypeReferenceHandle)MetadataTokens.Handle(typeReferenceToken);
			if (handle.IsNil) {
				namespaceName = null;
				typeName = null;
				return false;
			}

			var typeReference = metadata.GetTypeReference(handle);
			namespaceName = metadata.GetString(typeReference.Namespace);
			typeName = metadata.GetString(typeReference.Name);
			return true;
		}
	}
}
