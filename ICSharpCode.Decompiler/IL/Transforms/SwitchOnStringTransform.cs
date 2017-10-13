﻿// Copyright (c) 2017 Siegfried Pammer
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
using ICSharpCode.Decompiler.IL.ControlFlow;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.IL.Transforms
{
	/// <summary>
	/// Detects switch-on-string patterns employed by the C# compiler and transforms them to an ILAst-switch-instruction.
	/// </summary>
	class SwitchOnStringTransform : IILTransform
	{
		public void Run(ILFunction function, ILTransformContext context)
		{
			if (!context.Settings.SwitchStatementOnString)
				return;

			HashSet<BlockContainer> changedContainers = new HashSet<BlockContainer>();

			foreach (var block in function.Descendants.OfType<Block>()) {
				bool changed = false;
				for (int i = block.Instructions.Count - 1; i >= 0; i--) {
					if (SimplifyCascadingIfStatements(block.Instructions, ref i)) {
						changed = true;
						continue;
					}
					if (MatchLegacySwitchOnStringWithHashtable(block.Instructions, ref i)) {
						changed = true;
						continue;
					}
					if (MatchLegacySwitchOnStringWithDict(block.Instructions, ref i)) {
						changed = true;
						continue;
					}
					if (MatchRoslynSwitchOnString(block.Instructions, ref i)) {
						changed = true;
						continue;
					}
				}
				if (!changed) continue;
				SwitchDetection.SimplifySwitchInstruction(block);
				if (block.Parent is BlockContainer container)
					changedContainers.Add(container);
			}

			foreach (var container in changedContainers)
				container.SortBlocks(deleteUnreachableBlocks: true);
		}

		bool SimplifyCascadingIfStatements(InstructionCollection<ILInstruction> instructions, ref int i)
		{
			if (i < 1) return false;
			// match first block: checking switch-value for null or first value (Roslyn)
			// if (call op_Equality(ldloc switchValueVar, ldstr value)) br firstBlock
			// -or-
			// if (comp(ldloc switchValueVar == ldnull)) br defaultBlock
			if (!(instructions[i].MatchIfInstruction(out var condition, out var firstBlockJump)))
				return false;
			if (!firstBlockJump.MatchBranch(out var firstBlock))
				return false;
			List<(string, Block)> values = new List<(string, Block)>();
			ILInstruction switchValue = null;
			bool extraLoad = false;
			bool keepAssignmentBefore = false;

			// Roslyn: match call to operator ==(string, string)
			if (MatchStringEqualityComparison(condition, out var switchValueVar, out string value)) {
				values.Add((value, firstBlock));
				if(!instructions[i - 1].MatchStLoc(switchValueVar, out switchValue)) {
					switchValue = new LdLoc(switchValueVar);
				}
			} else {
				// match null check with different variable:
				// this is used by the old C# compiler.
				if (MatchCompEqualsNull(condition, out var otherSwitchValueVar)) {
					values.Add((null, firstBlock));
				} else {
					return false;
				}

				// in case of optimized legacy code there are two stlocs:
				// stloc otherSwitchValueVar(ldloc switchValue)
				// stloc switchValueVar(ldloc otherSwitchValueVar)
				// if (comp(ldloc otherSwitchValueVar == ldnull)) br nullCase
				if (i > 1 && otherSwitchValueVar != null && instructions[i - 2].MatchStLoc(otherSwitchValueVar, out switchValue)
					&& instructions[i - 1].MatchStLoc(out switchValueVar, out var switchValueCopyInst)
					&& switchValueCopyInst.MatchLdLoc(otherSwitchValueVar) && otherSwitchValueVar.IsSingleDefinition && otherSwitchValueVar.LoadCount == 2) {
					extraLoad = true;
				} else if (instructions[i - 1].MatchStLoc(out switchValueVar, out switchValue)) {
					// unoptimized legacy switch
				}
			}

			// if instruction must be followed by a branch to the next case
			if (!(instructions.ElementAtOrDefault(i + 1) is Branch nextCaseJump))
				return false;
			// extract all cases and add them to the values list.
			Block currentCaseBlock = nextCaseJump.TargetBlock;
			Block nextCaseBlock;
			while ((nextCaseBlock = MatchCaseBlock(currentCaseBlock, switchValueVar, out value, out Block block)) != null) {
				values.Add((value, block));
				currentCaseBlock = nextCaseBlock;
			}
			// We didn't find enough cases, exit
			if (values.Count < 3)
				return false;
			// if the switchValueVar is used in other places as well, do not eliminate the store.
			if (switchValueVar.LoadCount > values.Count) {
				keepAssignmentBefore = true;
				switchValue = new LdLoc(switchValueVar);
			}
			var sections = new List<SwitchSection>(values.SelectWithIndex((index, b) => new SwitchSection { Labels = new LongSet(index), Body = new Branch(b.Item2) }));
			sections.Add(new SwitchSection { Labels = new LongSet(new LongInterval(0, sections.Count)).Invert(), Body = new Branch(currentCaseBlock) });
			var stringToInt = new StringToInt(switchValue, values.SelectArray(item => item.Item1));
			var inst = new SwitchInstruction(stringToInt);
			inst.Sections.AddRange(sections);
			if (extraLoad) {
				instructions[i - 2].ReplaceWith(inst);
				instructions.RemoveRange(i - 1, 3);
				i -= 2;
			} else {
				if (keepAssignmentBefore) {
					instructions[i].ReplaceWith(inst);
					instructions.RemoveAt(i + 1);
				} else {
					instructions[i - 1].ReplaceWith(inst);
					instructions.RemoveRange(i, 2);
					i--;
				}
			}
			return true;
		}

		/// <summary>
		/// Each case consists of two blocks:
		/// 1. block:
		/// if (call op_Equality(ldloc switchVariable, ldstr value)) br caseBlock
		/// br nextBlock
		/// -or-
		/// if (comp(ldloc switchValueVar == ldnull)) br nextBlock
		/// br caseBlock
		/// 2. block is caseBlock
		/// This method matches the above pattern or its inverted form:
		/// the call to ==(string, string) is wrapped in logic.not and the branch targets are reversed.
		/// Returns the next block that follows in the block-chain.
		/// The <paramref name="switchVariable"/> is updated if the value gets copied to a different variable.
		/// See comments below for more info.
		/// </summary>
		Block MatchCaseBlock(Block currentBlock, ILVariable switchVariable, out string value, out Block caseBlock)
		{
			value = null;
			caseBlock = null;

			if (currentBlock.IncomingEdgeCount != 1 || currentBlock.Instructions.Count != 2)
				return null;
			if (!currentBlock.Instructions[0].MatchIfInstruction(out var condition, out var caseBlockBranch))
				return null;
			if (!caseBlockBranch.MatchBranch(out caseBlock))
				return null;
			Block nextBlock;
			if (condition.MatchLogicNot(out var inner)) {
				condition = inner;
				nextBlock = caseBlock;
				if (!currentBlock.Instructions[1].MatchBranch(out caseBlock))
					return null;
			} else {
				if (!currentBlock.Instructions[1].MatchBranch(out nextBlock))
					return null;
			}
			if (!MatchStringEqualityComparison(condition, out var v, out value)) {
				if (MatchCompEqualsNull(condition, out v)) {
					value = null;
				} else {
					return null;
				}
			}
			if (v != switchVariable)
				return null;
			return nextBlock;
		}

		/// <summary>
		/// Returns true if <paramref name="left"/> is only assigned once and the initialization is done by copying <paramref name="right"/>.
		/// </summary>
		bool IsInitializedBy(ILVariable left, ILVariable right)
		{
			if (!left.IsSingleDefinition)
				return false;
			var storeInst = left.StoreInstructions.OfType<StLoc>().SingleOrDefault();
			if (storeInst == null)
				return false;
			return storeInst.Value.MatchLdLoc(right);
		}

		/// <summary>
		/// Matches the C# 2.0 switch-on-string pattern, which uses Dictionary&lt;string, int&gt;.
		/// </summary>
		bool MatchLegacySwitchOnStringWithDict(InstructionCollection<ILInstruction> instructions, ref int i)
		{
			if (i < 1) return false;
			// match first block: checking switch-value for null
			if (!(instructions[i].MatchIfInstruction(out var condition, out var exitBlockJump) &&
				instructions[i - 1].MatchStLoc(out var switchValueVar, out var switchValue) && switchValueVar.Type.IsKnownType(KnownTypeCode.String)))
				return false;
			if (!switchValueVar.IsSingleDefinition)
				return false;
			if (!exitBlockJump.MatchBranch(out var nullValueCaseBlock))
				return false;
			if (!(condition.MatchCompEquals(out var left, out var right) && right.MatchLdNull() && (left.Match(switchValue).Success || left.MatchLdLoc(switchValueVar))))
				return false;
			var nextBlockJump = instructions.ElementAtOrDefault(i + 1) as Branch;
			if (nextBlockJump == null || nextBlockJump.TargetBlock.IncomingEdgeCount != 1)
				return false;
			// match second block: checking compiler-generated Dictionary<string, int> for null
			var nextBlock = nextBlockJump.TargetBlock;
			if (nextBlock.Instructions.Count != 2 || !nextBlock.Instructions[0].MatchIfInstruction(out condition, out var tryGetValueBlockJump))
				return false;
			if (!tryGetValueBlockJump.MatchBranch(out var tryGetValueBlock))
				return false;
			if (!nextBlock.Instructions[1].MatchBranch(out var dictInitBlock) || dictInitBlock.IncomingEdgeCount != 1)
				return false;
			if (!(condition.MatchCompNotEquals(out left, out right) && right.MatchLdNull() &&
				MatchDictionaryFieldLoad(left, IsStringToIntDictionary, out var dictField, out var dictionaryType)))
				return false;
			// match third block: initialization of compiler-generated Dictionary<string, int>
			if (dictInitBlock.IncomingEdgeCount != 1 || dictInitBlock.Instructions.Count < 3)
				return false;
			if (!ExtractStringValuesFromInitBlock(dictInitBlock, out var stringValues, tryGetValueBlock, dictionaryType, dictField))
				return false;
			// match fourth block: TryGetValue on compiler-generated Dictionary<string, int>
			if (tryGetValueBlock.IncomingEdgeCount != 2 || tryGetValueBlock.Instructions.Count != 2)
				return false;
			if (!tryGetValueBlock.Instructions[0].MatchIfInstruction(out condition, out var defaultBlockJump))
				return false;
			if (!defaultBlockJump.MatchBranch(out var defaultBlock))
				return false;
			if (!(condition.MatchLogicNot(out var arg) && arg is Call c && c.Method.Name == "TryGetValue" &&
				MatchDictionaryFieldLoad(c.Arguments[0], IsStringToIntDictionary, out var dictField2, out _) && dictField2.Equals(dictField)))
				return false;
			if (!c.Arguments[1].MatchLdLoc(switchValueVar) || !c.Arguments[2].MatchLdLoca(out var switchIndexVar))
				return false;
			if (!tryGetValueBlock.Instructions[1].MatchBranch(out var switchBlock))
				return false;
			// match fifth block: switch-instruction block
			if (switchBlock.IncomingEdgeCount != 1 || switchBlock.Instructions.Count != 1)
				return false;
			if (!(switchBlock.Instructions[0] is SwitchInstruction switchInst && switchInst.Value.MatchLdLoc(switchIndexVar)))
				return false;
			var sections = new List<SwitchSection>(switchInst.Sections);
			// switch contains case null:
			if (nullValueCaseBlock != defaultBlock) {
				var label = new Util.LongSet(switchInst.Sections.Count);
				var possibleConflicts = switchInst.Sections.Where(sec => sec.Labels.Overlaps(label)).ToArray();
				if (possibleConflicts.Length > 1)
					return false;
				else if (possibleConflicts.Length == 1)
					possibleConflicts[0].Labels = possibleConflicts[0].Labels.ExceptWith(label);
				stringValues.Add(null);
				sections.Add(new SwitchSection() { Labels = label, Body = new Branch(nullValueCaseBlock) });
			}
			bool keepAssignmentBefore = false;
			if (switchValueVar.LoadCount > 2) {
				switchValue = new LdLoc(switchValueVar);
				keepAssignmentBefore = true;
			}
			var stringToInt = new StringToInt(switchValue, stringValues.ToArray());
			var inst = new SwitchInstruction(stringToInt);
			inst.Sections.AddRange(sections);
			instructions[i + 1].ReplaceWith(inst);
			if (keepAssignmentBefore) {
				instructions.RemoveAt(i);
				i--;
			} else {
				instructions.RemoveRange(i - 1, 2);
				i -= 2;
			}
			return true;
		}

		bool MatchDictionaryFieldLoad(ILInstruction inst, Func<IType, bool> typeMatcher, out IField dictField, out IType dictionaryType)
		{
			dictField = null;
			dictionaryType = null;
			return inst.MatchLdObj(out var dictionaryFieldLoad, out dictionaryType) &&
				typeMatcher(dictionaryType) &&
				dictionaryFieldLoad.MatchLdsFlda(out dictField) &&
				(dictField.IsCompilerGeneratedOrIsInCompilerGeneratedClass() || dictField.Name.StartsWith("$$method", StringComparison.Ordinal));
		}

		bool ExtractStringValuesFromInitBlock(Block block, out List<string> values, Block targetBlock, IType dictionaryType, IField dictionaryField)
		{
			values = null;
			if (!(block.Instructions[0].MatchStLoc(out var dictVar, out var newObjDict) &&
				newObjDict is NewObj newObj && newObj.Arguments.Count >= 1 && newObj.Arguments[0].MatchLdcI4(out var valuesLength)))
				return false;
			values = new List<string>(valuesLength);
			int i = 0;
			while (i + 1 < block.Instructions.Count - 2 && MatchAddCall(block.Instructions[i + 1], dictVar, i, out var value)) {
				values.Add(value);
				i++;
			}
			if (!(block.Instructions[i + 1].MatchStObj(out var loadField, out var dictVarLoad, out var dictType) &&
				dictType.Equals(dictionaryType) && loadField.MatchLdsFlda(out var dictField) && dictField.Equals(dictionaryField)) &&
				dictVarLoad.MatchLdLoc(dictVar))
				return false;
			return block.Instructions[i + 2].MatchBranch(targetBlock);
		}

		bool MatchAddCall(ILInstruction inst, ILVariable dictVar, int i, out string value)
		{
			value = null;
			return inst is Call c && c.Method.Name == "Add" && c.Arguments.Count == 3 &&
				c.Arguments[0].MatchLdLoc(dictVar) && c.Arguments[1].MatchLdStr(out value) &&
				(c.Arguments[2].MatchLdcI4(i) || (c.Arguments[2].MatchBox(out var arg, out _) && arg.MatchLdcI4(i)));
		}

		bool IsStringToIntDictionary(IType dictionaryType)
		{
			if (dictionaryType.FullName != "System.Collections.Generic.Dictionary")
				return false;
			if (dictionaryType.TypeArguments.Count != 2)
				return false;
			return dictionaryType.TypeArguments[0].IsKnownType(KnownTypeCode.String) &&
				dictionaryType.TypeArguments[1].IsKnownType(KnownTypeCode.Int32);
		}

		bool IsNonGenericHashtable(IType dictionaryType)
		{
			if (dictionaryType.FullName != "System.Collections.Hashtable")
				return false;
			if (dictionaryType.TypeArguments.Count != 0)
				return false;
			return true;
		}

		bool MatchLegacySwitchOnStringWithHashtable(InstructionCollection<ILInstruction> instructions, ref int i)
		{
			// match first block: checking compiler-generated Hashtable for null
			// if (comp(volatile.ldobj System.Collections.Hashtable(ldsflda $$method0x600003f-1) != ldnull)) br switchHeadBlock
			// br tableInitBlock
			if (!(instructions[i].MatchIfInstruction(out var condition, out var branchToSwitchHead) && i + 1 < instructions.Count))
				return false;
			if (!instructions[i + 1].MatchBranch(out var tableInitBlock) || tableInitBlock.IncomingEdgeCount != 1)
				return false;
			if (!(condition.MatchCompNotEquals(out var left, out var right) && right.MatchLdNull() &&
				MatchDictionaryFieldLoad(left, IsNonGenericHashtable, out var dictField, out var dictionaryType)))
				return false;
			if (!branchToSwitchHead.MatchBranch(out var switchHead))
				return false;
			// match second block: initialization of compiler-generated Hashtable
			// stloc table(newobj Hashtable..ctor(ldc.i4 capacity, ldc.f loadFactor))
			// call Add(ldloc table, ldstr value, box System.Int32(ldc.i4 index))
			// ... more calls to Add ...
			// volatile.stobj System.Collections.Hashtable(ldsflda $$method0x600003f - 1, ldloc table)
			// br switchHeadBlock
			if (tableInitBlock.IncomingEdgeCount != 1 || tableInitBlock.Instructions.Count < 3)
				return false;
			if (!ExtractStringValuesFromInitBlock(tableInitBlock, out var stringValues, switchHead, dictionaryType, dictField))
				return false;
			// match third block: checking switch-value for null
			// stloc tmp(ldloc switch-value)
			// stloc switchVariable(ldloc tmp)
			// if (comp(ldloc tmp == ldnull)) br nullCaseBlock
			// br getItemBlock
			if (switchHead.Instructions.Count != 4 || switchHead.IncomingEdgeCount != 2)
				return false;
			if (!switchHead.Instructions[0].MatchStLoc(out var tmp, out var switchValue))
				return false;
			if (!switchHead.Instructions[1].MatchStLoc(out var switchVariable, out var tmpLoad) || !tmpLoad.MatchLdLoc(tmp))
				return false;
			if (!switchHead.Instructions[2].MatchIfInstruction(out condition, out var nullCaseBlockBranch))
				return false;
			if (!switchHead.Instructions[3].MatchBranch(out var getItemBlock) || !nullCaseBlockBranch.MatchBranch(out var nullCaseBlock))
				return false;
			if (!(condition.MatchCompEquals(out left, out right) && right.MatchLdNull() && left.MatchLdLoc(tmp)))
				return false;
			// match fourth block: get_Item on compiler-generated Hashtable
			// stloc tmp2(call get_Item(volatile.ldobj System.Collections.Hashtable(ldsflda $$method0x600003f - 1), ldloc switchVariable))
			// stloc switchVariable(ldloc tmp2)
			// if (comp(ldloc tmp2 == ldnull)) br defaultCaseBlock
			// br switchBlock
			if (getItemBlock.IncomingEdgeCount != 1 || getItemBlock.Instructions.Count != 4)
				return false;
			if (!(getItemBlock.Instructions[0].MatchStLoc(out var tmp2, out var getItem) && getItem is Call getItemCall && getItemCall.Method.Name == "get_Item"))
				return false;
			if (!getItemBlock.Instructions[1].MatchStLoc(out var switchVariable2, out var tmp2Load) || !tmp2Load.MatchLdLoc(tmp2))
				return false;
			if (!ILVariableEqualityComparer.Instance.Equals(switchVariable, switchVariable2))
				return false;
			if (!getItemBlock.Instructions[2].MatchIfInstruction(out condition, out var defaultBlockBranch))
				return false;
			if (!getItemBlock.Instructions[3].MatchBranch(out var switchBlock) || !defaultBlockBranch.MatchBranch(out var defaultBlock))
				return false;
			if (!(condition.MatchCompEquals(out left, out right) && right.MatchLdNull() && left.MatchLdLoc(tmp2)))
				return false;
			if (!(getItemCall.Arguments.Count == 2 && MatchDictionaryFieldLoad(getItemCall.Arguments[0], IsStringToIntDictionary, out var dictField2, out _) && dictField2.Equals(dictField)) &&
				getItemCall.Arguments[1].MatchLdLoc(switchVariable2))
				return false;
			// match fifth block: switch-instruction block
			// switch (ldobj System.Int32(unbox System.Int32(ldloc switchVariable))) {
			// 	case [0..1): br caseBlock1
			//  ... more cases ...
			// 	case [long.MinValue..0),[13..long.MaxValue]: br defaultBlock
			// }
			if (switchBlock.IncomingEdgeCount != 1 || switchBlock.Instructions.Count != 1)
				return false;
			if (!(switchBlock.Instructions[0] is SwitchInstruction switchInst && switchInst.Value.MatchLdObj(out var target, out var ldobjType) &&
				target.MatchUnbox(out var arg, out var unboxType) && arg.MatchLdLoc(switchVariable2) && ldobjType.IsKnownType(KnownTypeCode.Int32) && unboxType.Equals(ldobjType)))
				return false;
			var sections = new List<SwitchSection>(switchInst.Sections);
			// switch contains case null: 
			if (nullCaseBlock != defaultBlock) {
				var label = new Util.LongSet(switchInst.Sections.Count);
				var possibleConflicts = switchInst.Sections.Where(sec => sec.Labels.Overlaps(label)).ToArray();
				if (possibleConflicts.Length > 1)
					return false;
				else if (possibleConflicts.Length == 1)
					possibleConflicts[0].Labels = possibleConflicts[0].Labels.ExceptWith(label);
				stringValues.Add(null);
				sections.Add(new SwitchSection() { Labels = label, Body = new Branch(nullCaseBlock) });
			}
			var stringToInt = new StringToInt(switchValue, stringValues.ToArray());
			var inst = new SwitchInstruction(stringToInt);
			inst.Sections.AddRange(sections);
			instructions[i + 1].ReplaceWith(inst);
			instructions.RemoveAt(i);
			return true;
		}

		bool MatchRoslynSwitchOnString(InstructionCollection<ILInstruction> instructions, ref int i)
		{
			if (i < 1) return false;
			if (!(instructions[i] is SwitchInstruction switchInst && switchInst.Value.MatchLdLoc(out var targetVar) &&
				MatchComputeStringHashCall(instructions[i - 1], targetVar, out var switchValue)))
				return false;

			var stringValues = new List<(int, string, Block)>();
			int index = 0;
			ILInstruction defaultBranch = null;
			foreach (var section in switchInst.Sections) {
				if (!section.Body.MatchBranch(out Block target)) {
					if (section.Body is Leave leave) {
						defaultBranch = leave;
						continue;
					}
					return false;
				}
				if (target.IncomingEdgeCount > 1) {
					defaultBranch = new Branch(target);
					continue;
				}
				if (target.Instructions.Count != 2)
					return false;
				if (!target.Instructions[0].MatchIfInstruction(out var condition, out var bodyBranch))
					return false;
				if (!bodyBranch.MatchBranch(out Block body))
					return false;
				if (!MatchStringEqualityOrNullComparison(condition, switchValue.Variable, out string stringValue)) {
					if (condition.MatchLogicNot(out condition) && MatchStringEqualityOrNullComparison(condition, switchValue.Variable, out stringValue)) {
						if (!target.Instructions[1].MatchBranch(out Block exit))
							return false;
						body = exit;
					} else
						return false;
				}
				stringValues.Add((index++, stringValue, body));
			}
			ILInstruction switchValueInst = switchValue;
			bool keepAssignmentBefore;
			if (i > 1 && instructions[i - 2].MatchStLoc(switchValue.Variable, out var switchValueTmp) &&
				switchValue.Variable.IsSingleDefinition && switchValue.Variable.LoadCount == switchInst.Sections.Count) {
				switchValueInst = switchValueTmp;
				keepAssignmentBefore = false;
			} else {
				keepAssignmentBefore = true;
			}
			var defaultLabel = new LongSet(new LongInterval(0, index)).Invert();
			var value = new StringToInt(switchValueInst, stringValues.Select(item => item.Item2).ToArray());
			var newSwitch = new SwitchInstruction(value);
			newSwitch.Sections.AddRange(stringValues.Select(section => new SwitchSection { Labels = new Util.LongSet(section.Item1), Body = new Branch(section.Item3) }));
			newSwitch.Sections.Add(new SwitchSection { Labels = defaultLabel, Body = defaultBranch });
			instructions[i].ReplaceWith(newSwitch);
			if (keepAssignmentBefore) {
				instructions.RemoveAt(i - 1);
				i--;
			} else {
				instructions.RemoveRange(i - 2, 2);
				i -= 2;
			}
			return true;
		}

		bool MatchComputeStringHashCall(ILInstruction inst, ILVariable targetVar, out LdLoc switchValue)
		{
			switchValue = null;
			if (!inst.MatchStLoc(targetVar, out var value))
				return false;
			if (!(value is Call c && c.Arguments.Count == 1 && c.Method.Name == "ComputeStringHash" && c.Method.IsCompilerGeneratedOrIsInCompilerGeneratedClass()))
				return false;
			if (!(c.Arguments[0] is LdLoc))
				return false;
			switchValue = (LdLoc)c.Arguments[0];
			return true;
		}

		bool MatchStringEqualityOrNullComparison(ILInstruction condition, ILVariable variable, out string stringValue)
		{
			if (!MatchStringEqualityComparison(condition, out var v, out stringValue)) {
				if (!MatchCompEqualsNull(condition, out v))
					return false;
				stringValue = null;
			}
				
			return v == variable;
		}

		bool MatchStringEqualityComparison(ILInstruction condition, out ILVariable variable, out string stringValue)
		{
			stringValue = null;
			variable = null;
			ILInstruction left, right;
			if (condition is Call c && c.Method.IsOperator && c.Method.Name == "op_Equality" && c.Arguments.Count == 2) {
				left = c.Arguments[0];
				right = c.Arguments[1];
				if (!right.MatchLdStr(out stringValue))
					return false;
			} else {
				return false;
			}
			return left.MatchLdLoc(out variable);
		}

		bool MatchCompEqualsNull(ILInstruction condition, out ILVariable variable)
		{
			variable = null;
			if (!condition.MatchCompEquals(out var left, out var right))
				return false;
			return right.MatchLdNull() && left.MatchLdLoc(out variable);
		}
	}
}
