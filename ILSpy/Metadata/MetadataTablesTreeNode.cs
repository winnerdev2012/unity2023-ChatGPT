﻿// Copyright (c) 2021 AlphaSierraPapa for the SharpDevelop Team
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

using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.ILSpy.Options;
using ICSharpCode.ILSpy.TreeNodes;
using ICSharpCode.ILSpy.ViewModels;

namespace ICSharpCode.ILSpy.Metadata
{
	class MetadataTablesTreeNode : ILSpyTreeNode
	{
		private PEFile module;

		public MetadataTablesTreeNode(PEFile module)
		{
			this.module = module;
			this.LazyLoading = true;
		}

		public override object Text => "Tables";

		public override object Icon => Images.Literal;

		protected override void LoadChildren()
		{
			if (ShowTable(TableIndex.Module))
				this.Children.Add(new ModuleTableTreeNode(module));
			if (ShowTable(TableIndex.TypeRef))
				this.Children.Add(new TypeRefTableTreeNode(module));
			if (ShowTable(TableIndex.TypeDef))
				this.Children.Add(new TypeDefTableTreeNode(module));
			if (ShowTable(TableIndex.Field))
				this.Children.Add(new FieldTableTreeNode(module));
			if (ShowTable(TableIndex.MethodDef))
				this.Children.Add(new MethodTableTreeNode(module));
			if (ShowTable(TableIndex.Param))
				this.Children.Add(new ParamTableTreeNode(module));
			if (ShowTable(TableIndex.InterfaceImpl))
				this.Children.Add(new InterfaceImplTableTreeNode(module));
			if (ShowTable(TableIndex.MemberRef))
				this.Children.Add(new MemberRefTableTreeNode(module));
			if (ShowTable(TableIndex.Constant))
				this.Children.Add(new ConstantTableTreeNode(module));
			if (ShowTable(TableIndex.CustomAttribute))
				this.Children.Add(new CustomAttributeTableTreeNode(module));
			if (ShowTable(TableIndex.FieldMarshal))
				this.Children.Add(new FieldMarshalTableTreeNode(module));
			if (ShowTable(TableIndex.DeclSecurity))
				this.Children.Add(new DeclSecurityTableTreeNode(module));
			if (ShowTable(TableIndex.ClassLayout))
				this.Children.Add(new ClassLayoutTableTreeNode(module));
			if (ShowTable(TableIndex.FieldLayout))
				this.Children.Add(new FieldLayoutTableTreeNode(module));
			if (ShowTable(TableIndex.StandAloneSig))
				this.Children.Add(new StandAloneSigTableTreeNode(module));
			if (ShowTable(TableIndex.EventMap))
				this.Children.Add(new EventMapTableTreeNode(module));
			if (ShowTable(TableIndex.Event))
				this.Children.Add(new EventTableTreeNode(module));
			if (ShowTable(TableIndex.PropertyMap))
				this.Children.Add(new PropertyMapTableTreeNode(module));
			if (ShowTable(TableIndex.Property))
				this.Children.Add(new PropertyTableTreeNode(module));
			if (ShowTable(TableIndex.MethodSemantics))
				this.Children.Add(new MethodSemanticsTableTreeNode(module));
			if (ShowTable(TableIndex.MethodImpl))
				this.Children.Add(new MethodImplTableTreeNode(module));
			if (ShowTable(TableIndex.ModuleRef))
				this.Children.Add(new ModuleRefTableTreeNode(module));
			if (ShowTable(TableIndex.TypeSpec))
				this.Children.Add(new TypeSpecTableTreeNode(module));
			if (ShowTable(TableIndex.ImplMap))
				this.Children.Add(new ImplMapTableTreeNode(module));
			if (ShowTable(TableIndex.FieldRva))
				this.Children.Add(new FieldRVATableTreeNode(module));
			if (ShowTable(TableIndex.Assembly))
				this.Children.Add(new AssemblyTableTreeNode(module));
			if (ShowTable(TableIndex.AssemblyRef))
				this.Children.Add(new AssemblyRefTableTreeNode(module));
			if (ShowTable(TableIndex.File))
				this.Children.Add(new FileTableTreeNode(module));
			if (ShowTable(TableIndex.ExportedType))
				this.Children.Add(new ExportedTypeTableTreeNode(module));
			if (ShowTable(TableIndex.ManifestResource))
				this.Children.Add(new ManifestResourceTableTreeNode(module));
			if (ShowTable(TableIndex.NestedClass))
				this.Children.Add(new NestedClassTableTreeNode(module));
			if (ShowTable(TableIndex.GenericParam))
				this.Children.Add(new GenericParamTableTreeNode(module));
			if (ShowTable(TableIndex.MethodSpec))
				this.Children.Add(new MethodSpecTableTreeNode(module));
			if (ShowTable(TableIndex.GenericParamConstraint))
				this.Children.Add(new GenericParamConstraintTableTreeNode(module));

			bool ShowTable(TableIndex table) => !MainWindow.Instance.CurrentDisplaySettings.HideEmptyMetadataTables || module.Metadata.GetTableRowCount(table) > 0;

		}

		public override bool View(TabPageModel tabPage)
		{
			tabPage.Title = Text.ToString();
			tabPage.SupportsLanguageSwitching = false;

			return false;
		}

		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			language.WriteCommentLine(output, "Metadata Tables");
		}
	}
}
