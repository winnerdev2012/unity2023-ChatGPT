﻿using System;

namespace ICSharpCode.Decompiler.Tests.TestCases.Pretty
{
	internal static class Ext
	{
		public static void ExtOnRef(this ref RefLocalsAndReturns.NormalStruct s)
		{
		}
		public static void ExtOnIn(this in RefLocalsAndReturns.NormalStruct s)
		{
		}
		public static void ExtOnRef(this ref RefLocalsAndReturns.ReadOnlyStruct s)
		{
		}
		public static void ExtOnIn(this in RefLocalsAndReturns.ReadOnlyStruct s)
		{
		}
		public static void ExtOnRef(this ref RefLocalsAndReturns.ReadOnlyRefStruct s)
		{
		}
		public static void ExtOnIn(this in RefLocalsAndReturns.ReadOnlyRefStruct s)
		{
		}
	}

	internal class RefLocalsAndReturns
	{
		public delegate ref T RefFunc<T>();
		public delegate ref readonly T ReadOnlyRefFunc<T>();
		public delegate ref TReturn RefFunc<T1, TReturn>(T1 param1);

		public ref struct RefStruct
		{
			private int dummy;
		}

		public readonly ref struct ReadOnlyRefStruct
		{
			private readonly int dummy;
		}

		public struct NormalStruct
		{
			private readonly int dummy;

			public void Method()
			{
			}
		}

		public readonly struct ReadOnlyStruct
		{
			private readonly int dummy;

			public void Method()
			{
			}
		}

		private static int[] numbers = new int[10] {
			1,
			3,
			7,
			15,
			31,
			63,
			127,
			255,
			511,
			1023
		};

		private static string[] strings = new string[2] {
			"Hello",
			"World"
		};

		private static string NullString = "";

		private static int DefaultInt = 0;

		public static ref T GetRef<T>()
		{
			throw new NotImplementedException();
		}

		public static ref readonly T GetReadonlyRef<T>()
		{
			throw new NotImplementedException();
		}

		public void CallOnRefReturn()
		{
			// Both direct calls:
			GetRef<NormalStruct>().Method();
			GetRef<ReadOnlyStruct>().Method();

			// call on a copy, not the original ref:
			NormalStruct @ref = GetRef<NormalStruct>();
			@ref.Method();

			ReadOnlyStruct ref2 = GetRef<ReadOnlyStruct>();
			ref2.Method();
		}

		public void CallOnReadOnlyRefReturn()
		{
			// uses implicit temporary:
			GetReadonlyRef<NormalStruct>().Method();
			// direct call:
			GetReadonlyRef<ReadOnlyStruct>().Method();
			// call on a copy, not the original ref:
			ReadOnlyStruct readonlyRef = GetReadonlyRef<ReadOnlyStruct>();
			readonlyRef.Method();
		}

		public void CallOnInParam(in NormalStruct ns, in ReadOnlyStruct rs)
		{
			// uses implicit temporary:
			ns.Method();
			// direct call:
			rs.Method();
			// call on a copy, not the original ref:
			ReadOnlyStruct readOnlyStruct = rs;
			readOnlyStruct.Method();
		}

		public static TReturn Invoker<T1, TReturn>(RefFunc<T1, TReturn> action, T1 value)
		{
			return action(value);
		}

		public static ref int FindNumber(int target)
		{
			for (int i = 0; i < numbers.Length; i++) {
				if (numbers[i] >= target) {
					return ref numbers[i];
				}
			}
			return ref numbers[0];
		}

		public static ref int LastNumber()
		{
			return ref numbers[numbers.Length - 1];
		}

		public static ref int ElementAtOrDefault(int index)
		{
			if (index >= 0 && index < numbers.Length) {
				return ref numbers[index];
			}
			return ref DefaultInt;
		}

		public static ref int LastOrDefault()
		{
			if (numbers.Length != 0) {
				return ref numbers[numbers.Length - 1];
			}
			return ref DefaultInt;
		}

		public static void DoubleNumber(ref int num)
		{
			Console.WriteLine("old: " + num);
			num *= 2;
			Console.WriteLine("new: " + num);
		}

		public static ref string GetOrSetString(int index)
		{
			if (index < 0 || index >= strings.Length) {
				return ref NullString;
			}

			return ref strings[index];
		}

		public void CallSiteTests(NormalStruct s, ReadOnlyStruct r, ReadOnlyRefStruct rr)
		{
			s.ExtOnIn();
			s.ExtOnRef();
			r.ExtOnIn();
			r.ExtOnRef();
			rr.ExtOnIn();
			rr.ExtOnRef();
			CallOnInParam(in s, in r);
		}

		public static void Main(string[] args)
		{
			DoubleNumber(ref args.Length == 1 ? ref numbers[0] : ref DefaultInt);
			DoubleNumber(ref FindNumber(32));
			Console.WriteLine(string.Join(", ", numbers));
			DoubleNumber(ref LastNumber());
			Console.WriteLine(string.Join(", ", numbers));
			Console.WriteLine(GetOrSetString(0));
			GetOrSetString(0) = "Goodbye";
			Console.WriteLine(string.Join(" ", strings));
			GetOrSetString(5) = "Here I mutated the null value!?";
			Console.WriteLine(GetOrSetString(-5));

			Console.WriteLine(Invoker((int x) => ref numbers[x], 0));
			Console.WriteLine(LastOrDefault());
			LastOrDefault() = 10000;
			Console.WriteLine(ElementAtOrDefault(-5));
		}
	}
}
