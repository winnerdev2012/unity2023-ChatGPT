// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using ICSharpCode.NRefactory.VB.Ast;
using Attribute = ICSharpCode.NRefactory.VB.Ast.Attribute;

namespace ICSharpCode.NRefactory.VB {
	public interface IAstVisitor<in T, out S>
	{
		S VisitBlockStatement(BlockStatement blockStatement, T data);
		S VisitComment(Comment comment, T data);
		S VisitCompilationUnit(CompilationUnit compilationUnit, T data);
		S VisitPatternPlaceholder(AstNode placeholder, PatternMatching.Pattern pattern, T data);
		S VisitTypeParameterDeclaration(TypeParameterDeclaration typeParameterDeclaration, T data);
		S VisitParameterDeclaration(ParameterDeclaration parameterDeclaration, T data);
		S VisitVBTokenNode(VBTokenNode vBTokenNode, T data);
		S VisitEventMemberSpecifier(EventMemberSpecifier eventMemberSpecifier, T data);
		S VisitInterfaceMemberSpecifier(InterfaceMemberSpecifier interfaceMemberSpecifier, T data);
		
		// Global scope
		S VisitAliasImportsClause(AliasImportsClause aliasImportsClause, T data);
		S VisitAttribute(Attribute attribute, T data);
		S VisitAttributeBlock(AttributeBlock attributeBlock, T data);
		S VisitImportsStatement(ImportsStatement importsStatement, T data);
		S VisitMemberImportsClause(MemberImportsClause memberImportsClause, T data);
		S VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration, T data);
		S VisitOptionStatement(OptionStatement optionStatement, T data);
		S VisitTypeDeclaration(TypeDeclaration typeDeclaration, T data);
		S VisitXmlNamespaceImportsClause(XmlNamespaceImportsClause xmlNamespaceImportsClause, T data);
		S VisitEnumDeclaration(EnumDeclaration enumDeclaration, T data);
		S VisitEnumMemberDeclaration(EnumMemberDeclaration enumMemberDeclaration, T data);
		S VisitDelegateDeclaration(DelegateDeclaration delegateDeclaration, T data);
		
		// TypeMember scope
		S VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration, T data);
		S VisitMethodDeclaration(MethodDeclaration methodDeclaration, T data);
		S VisitFieldDeclaration(FieldDeclaration fieldDeclaration, T data);
		S VisitVariableDeclarator(VariableDeclarator variableDeclarator, T data);
		S VisitVariableIdentifier(VariableIdentifier variableIdentifier, T data);
		S VisitAccessor(Accessor accessor, T data);
		S VisitPropertyDeclaration(PropertyDeclaration propertyDeclaration, T data);
		
		// Expression scope
		S VisitIdentifier(Identifier identifier, T data);
		S VisitXmlIdentifier(XmlIdentifier xmlIdentifier, T data);
		S VisitXmlLiteralString(XmlLiteralString xmlLiteralString, T data);
		S VisitSimpleNameExpression(SimpleNameExpression identifierExpression, T data);
		S VisitPrimitiveExpression(PrimitiveExpression primitiveExpression, T data);
		S VisitInstanceExpression(InstanceExpression instanceExpression, T data);
		S VisitParenthesizedExpression(ParenthesizedExpression parenthesizedExpression, T data);
		S VisitAddressOfExpression(AddressOfExpression addressOfExpression, T data);
		S VisitGetTypeExpression(GetTypeExpression getTypeExpression, T data);
		S VisitTypeOfIsExpression(TypeOfIsExpression typeOfIsExpression, T data);
		S VisitGetXmlNamespaceExpression(GetXmlNamespaceExpression getXmlNamespaceExpression, T data);
		S VisitMemberAccessExpression(MemberAccessExpression memberAccessExpression, T data);
		S VisitTypeReferenceExpression(TypeReferenceExpression typeReferenceExpression, T data);
		S VisitBinaryOperatorExpression(BinaryOperatorExpression binaryOperatorExpression, T data);
		S VisitAssignmentExpression(AssignmentExpression assignmentExpression, T data);
		S VisitIdentifierExpression(IdentifierExpression identifierExpression, T data);
		S VisitInvocationExpression(InvocationExpression invocationExpression, T data);
		S VisitArrayInitializerExpression(ArrayInitializerExpression arrayInitializerExpression, T data);
		S VisitObjectCreationExpression(ObjectCreationExpression objectCreationExpression, T data);
		
		// Statement scope
		S VisitLabelDeclarationStatement(LabelDeclarationStatement labelDeclarationStatement, T data);
		S VisitLocalDeclarationStatement(LocalDeclarationStatement localDeclarationStatement, T data);
		S VisitExpressionStatement(ExpressionStatement expressionStatement, T data);
		S VisitWithStatement(WithStatement withStatement, T data);
		S VisitSyncLockStatement(SyncLockStatement syncLockStatement, T data);
		S VisitIfElseStatement(IfElseStatement ifElseStatement, T data);
		S VisitTryStatement(TryStatement tryStatement, T data);
		S VisitThrowStatement(ThrowStatement throwStatement, T data);
		S VisitCatchBlock(CatchBlock catchBlock, T data);
		S VisitReturnStatement(ReturnStatement returnStatement, T data);
		
		// TypeName
		S VisitPrimitiveType(PrimitiveType primitiveType, T data);
		S VisitQualifiedType(QualifiedType qualifiedType, T data);
		S VisitComposedType(ComposedType composedType, T data);
		S VisitArraySpecifier(ArraySpecifier arraySpecifier, T data);
		S VisitSimpleType(SimpleType simpleType, T data);
	}
}
