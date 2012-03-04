using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection.Emit;
using System.Reflection;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;

namespace FluentStringParser
{
    internal static class ILHelpers
    {
        internal class ControlException : Exception { }

        internal static void Initialize(this ILGenerator il)
        {
            var toParseLength = il.DeclareLocal(typeof(int));
            var toParseAsChar = il.DeclareLocal(typeof(char[]));
            var accumulator = il.DeclareLocal(typeof(int));
            var scratchInt = il.DeclareLocal(typeof(int));
            var scratchLong = il.DeclareLocal(typeof(long));
            var scratchInt2 = il.DeclareLocal(typeof(int));

            il.Emit(OpCodes.Ldarg_0);           // *string
            il.EmitCall(OpCodes.Call, typeof(string).GetMethod("ToCharArray", new Type[0]), null);  // *char[]
            il.Emit(OpCodes.Stloc, toParseAsChar);   // --empty--

            il.Emit(OpCodes.Ldloc, toParseAsChar);  // *char[]
            il.Emit(OpCodes.Ldlen);                 // <length of *char[]>
            il.Emit(OpCodes.Stloc, toParseLength);  // --empty--

            il.Emit(OpCodes.Ldc_I4_0);           // 0
            il.Emit(OpCodes.Stloc, accumulator); // --empty--

            il.Emit(OpCodes.Ldc_I4_0);          // 0
            il.Emit(OpCodes.Stloc, scratchInt); // --empty--

            il.Emit(OpCodes.Ldc_I4_0);          // 0
            il.Emit(OpCodes.Conv_I8);           // <long 0>
            il.Emit(OpCodes.Stloc, scratchLong);// --empty--

            il.Emit(OpCodes.Ldc_I4_0);          // 0
            il.Emit(OpCodes.Stloc, scratchInt2);// --empty--
            
            // Break this out for code coverage purposes
            _InitializeFailureCheck(toParseLength, toParseAsChar, accumulator, scratchInt, scratchLong, scratchInt2);
        }

        [ExcludeFromCodeCoverage]
        private static void _InitializeFailureCheck(
            LocalBuilder toParseLength, 
            LocalBuilder toParseAsChar,
            LocalBuilder accumulator,
            LocalBuilder scratchInt,
            LocalBuilder scratchLong,
            LocalBuilder scratchInt2)
        {
            if (toParseLength.LocalIndex != 0) throw new InvalidOperationException();
            if (toParseAsChar.LocalIndex != 1) throw new InvalidOperationException();
            if (accumulator.LocalIndex != 2) throw new InvalidOperationException();
            if (scratchInt.LocalIndex != 3) throw new InvalidOperationException();
            if (scratchLong.LocalIndex != 4) throw new InvalidOperationException();
            if (scratchInt2.LocalIndex != 5) throw new InvalidOperationException();
        }

        internal static void LoadObjectBeingBuild(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldarg_1);
        }

        internal static void CallFailureAndReturn<T>(this ILGenerator il, int onStack, bool dontReturn = false)
        {
            // Stack starts: onStack # of items

            for (int i = 0; i < onStack; i++)
                il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Ldarg_2); // *onFailure
            il.Emit(OpCodes.Ldarg_0); // *<string toParse> *onFailure
            il.Emit(OpCodes.Ldarg_1); // *T *<string toParse> *onFailure
            il.EmitCall(OpCodes.Call, typeof(Action<string, T>).GetMethod("Invoke"), null); // --empty--

            if (!dontReturn)
            {
                // Can't actually return, freak out and throw an exception to get us out of here
                il.Emit(OpCodes.Newobj, typeof(ControlException).GetConstructor(new Type[0])); // exception
                il.Emit(OpCodes.Throw);
            }
        }

        internal static void LoadToParse(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc_1);
        }

        internal static void LoadToParseLength(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc_0);
        }

        internal static void LoadAccumulator(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc_2);
        }

        internal static void StoreAccumulator(this ILGenerator il)
        {
            il.Emit(OpCodes.Stloc_2);
        }

        internal static void LoadScratchInt(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc, 3);
        }

        internal static void StoreScratchInt(this ILGenerator il)
        {
            il.Emit(OpCodes.Stloc, 3);
        }

        internal static void LoadScratchLong(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc, 4);
        }

        internal static void StoreScratchLong(this ILGenerator il)
        {
            il.Emit(OpCodes.Stloc, 4);
        }

        internal static void LoadScratchInt2(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc, 5);
        }

        internal static void StoreScratchInt2(this ILGenerator il)
        {
            il.Emit(OpCodes.Stloc, 5);
        }

        internal static void IncrementAccumulator(this ILGenerator il)
        {
            il.LoadAccumulator();      // accumulator
            il.Emit(OpCodes.Ldc_I4_1); // 1 accumulator
            il.Emit(OpCodes.Add);      // accumuatlor++
            il.StoreAccumulator();     // --empty--
        }

        /// <summary>
        /// Places the character at accumulator in toParse
        /// on the stack
        /// </summary>
        internal static void LoadFromToParseAtAccumulator(this ILGenerator il, int? offset = null)
        {
            il.LoadToParse();           // *char[]
            il.LoadAccumulator();       // accumulator *char[]

            if (offset.HasValue)
            {
                il.Emit(OpCodes.Ldc_I4, offset.Value);  // offset accumulator *char[]
                il.Emit(OpCodes.Add);                   // <accumulator + offset> *char[]
            }

            il.Emit(OpCodes.Ldelem_I2); // char
        }

        /// <summary>
        /// This checks characters [1, toCheck.Length] in toCheck against those in toParse (starting at accumulator+1) for equality,
        /// if equal it increments accumulator past toCheck, and branches to finished.
        /// 
        /// If not equal, it branches to resume without modifying accumulator.
        /// </summary>
        internal static void CheckForMatchFromOne(this ILGenerator il, string toCheck, Label resume, Label finished)
        {
            for (int i = 1; i < toCheck.Length; i++)
            {
                il.LoadFromToParseAtAccumulator(offset: i);

                il.Emit(OpCodes.Ldc_I4, toCheck[i]); // char char
                il.Emit(OpCodes.Bne_Un, resume);     // --empty--
            }

            il.LoadAccumulator();                       // accumulator
            il.Emit(OpCodes.Ldc_I4, toCheck.Length);    // toCheck.Length accumulator
            il.Emit(OpCodes.Add);                       // <accumultaor + toCheck.Length>
            il.StoreAccumulator();                      // --empty--
            il.Emit(OpCodes.Br, finished);
        }

        private static MethodInfo ParseMethodFor(Type t)
        {
            if (t == typeof(double))
            {
                return typeof(double).GetMethod("Parse", new[] { typeof(string) });
            }

            if (t == typeof(float))
            {
                return typeof(float).GetMethod("Parse", new[] { typeof(string) });
            }

            if (t == typeof(decimal))
            {
                return typeof(decimal).GetMethod("Parse", new[] { typeof(string) });
            }

            return null;
        }

        /// <summary>
        /// Stack Starts
        ///   - long
        /// 
        /// Stack ends
        ///   - (type)long
        ///   
        /// throws on overflow
        /// </summary>
        public static void ConvertToFromLong(this ILGenerator il, Type type)
        {
            if (type == typeof(sbyte))
            {
                il.Emit(OpCodes.Conv_Ovf_I1);
                return;
            }

            if (type == typeof(byte))
            {
                il.Emit(OpCodes.Conv_Ovf_U1);
                return;
            }

            if (type == typeof(short))
            {
                il.Emit(OpCodes.Conv_Ovf_I2);
                return;
            }

            if (type == typeof(ushort))
            {
                il.Emit(OpCodes.Conv_Ovf_U2);
                return;
            }

            if (type == typeof(int))
            {
                il.Emit(OpCodes.Conv_Ovf_I4);
                return;
            }

            if (type == typeof(uint))
            {
                il.Emit(OpCodes.Conv_Ovf_U4);
                return;
            }

            // Break this out for code coverage purposes
            _ConvertToFromLongFailure(il, type);
        }

        [ExcludeFromCodeCoverage]
        private static void _ConvertToFromLongFailure(ILGenerator il, Type type)
        {
            if (type != typeof(long) && type != typeof(ulong))
            {
                throw new InvalidOperationException("Unexpected type to coerce from long, " + type.Name);
            }
        }

        /// <summary>
        /// Parse an enumeration from a string,
        /// we've already decided to pay for a method call; so I
        /// see no harm making it two.
        /// </summary>
        private static T _ParseEnum<T>(string val) where T : struct
        {
            return (T)Enum.Parse(typeof(T), val, ignoreCase: true);
        }

        /// <summary>
        /// Given a stack of
        ///   - start char[]
        ///   
        /// loads the character at start+offset, compares insensitively to toChar, and brances to onFalse
        /// if they are not equal.
        /// 
        /// Falls through if they are equal.
        ///
        /// In both cases the stack afterwards is:
        ///   - start char[]
        ///   
        /// Maye trash the scratch integers
        /// </summary>
        private static void CharacterCompare(this ILGenerator il, int offset, char toChar, Label onFalse)
        {
            var match = il.DefineLabel();

            var upper = Char.ToUpperInvariant(toChar);
            var lower = Char.ToLowerInvariant(toChar);

            // start char[]
            il.StoreScratchInt();               // char[]
            il.LoadScratchInt();                // start char[]

            il.Emit(OpCodes.Ldc_I4, offset);        // offset start char[]
            il.Emit(OpCodes.Add);                   // <start+offset> char[]
            il.StoreScratchInt2();                  // char[]
            il.Emit(OpCodes.Dup);                   // char[] char[]
            il.LoadScratchInt2();                   // <start+offset> char[] char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char char[]
            il.Emit(OpCodes.Dup);                   // char char char[]

            il.Emit(OpCodes.Ldc_I4, upper); // upper char char char[]
            il.Emit(OpCodes.Beq, match);    // char char[]
            il.Emit(OpCodes.Dup);           // char char char[]
            il.Emit(OpCodes.Ldc_I4, lower); // lower char char char[]
            il.Emit(OpCodes.Beq, match);    // char char[]
            
            // We know there's no match now
            il.Emit(OpCodes.Pop);           // char[]
            il.LoadScratchInt();            // start char[]
            il.Emit(OpCodes.Br, onFalse);   // start char[]

            // Branch here if we've got a match
            il.MarkLabel(match);            // char char[]
            il.Emit(OpCodes.Pop);           // char[]
            il.LoadScratchInt();            // start char[]
        }
        
        /// <summary>
        /// Parses a bool, of the form TRUE (case insensitive) or 1;
        /// everything else is false
        /// 
        /// Expects a stack of:
        ///   - length start char[]
        ///   
        /// May trash scratch values
        /// 
        /// Stack will be empty at end
        /// </summary>
        private static void NewParseBool(ILGenerator il)
        {
            var isFalse = il.DefineLabel();
            var isOneLetter = il.DefineLabel();
            var finished = il.DefineLabel();
            var isTrue = il.DefineLabel();
            var maybeTrue = il.DefineLabel();

            // length start char[]
            
            il.Emit(OpCodes.Dup);               // length length start char[]
            il.Emit(OpCodes.Ldc_I4_1);          // 1 length length start char[]
            il.Emit(OpCodes.Beq, isOneLetter);  // length start char[]
            il.Emit(OpCodes.Ldc_I4_4);          // 4 length start char[]
            il.Emit(OpCodes.Beq, maybeTrue);    // start char[]

            il.Emit(OpCodes.Br, isFalse);      // start char[]

            il.MarkLabel(isOneLetter);              // length start char[]
            il.Emit(OpCodes.Pop);                   // start char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char
            il.Emit(OpCodes.Ldc_I4, '1');           // '1' char
            il.Emit(OpCodes.Ceq);                   // value
            il.Emit(OpCodes.Br, finished);          // value

            il.MarkLabel(maybeTrue);              // start char[]
            il.CharacterCompare(0, 'T', isFalse); // start char[]
            il.CharacterCompare(1, 'R', isFalse); // start char[]
            il.CharacterCompare(2, 'U', isFalse); // start char[]
            il.CharacterCompare(3, 'E', isFalse); // start char[]
            il.Emit(OpCodes.Pop);                 // char[]
            il.Emit(OpCodes.Pop);                 // --empty--
            il.Emit(OpCodes.Ldc_I4_1);            // true
            il.Emit(OpCodes.Br, finished);        // true

            il.MarkLabel(isFalse);              // start char[]
            il.Emit(OpCodes.Pop);               // char[]
            il.Emit(OpCodes.Pop);               // --empty--
            il.Emit(OpCodes.Ldc_I4_0);          // false

            il.MarkLabel(finished);
        }

        /// <summary>
        /// Stack is expected to be
        ///   - length start char[]
        /// 
        /// for easy coercion into a string if needed
        /// 
        /// results in stack of
        ///   - value
        /// 
        /// The emitted code may trash the scratch numbers
        /// </summary>
        private static void NewParseEnum(ILGenerator il, Type enumType)
        {
            var underlying = Enum.GetUnderlyingType(enumType);
            var parseEnum = typeof(ILHelpers).GetMethod("_ParseEnum", BindingFlags.Static | BindingFlags.NonPublic);
            parseEnum = parseEnum.MakeGenericMethod(enumType);

            var notNumber = il.DefineLabel();
            var finished = il.DefineLabel();

            // length start char[]
            il.StoreScratchInt();                   // start char[]
            il.StoreScratchInt2();                  // char[]
            il.Emit(OpCodes.Dup);                   // char[] char[]
            il.LoadScratchInt2();                   // start char[] char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char char[]
            il.Emit(OpCodes.Dup);                   // char char char[]
            il.Emit(OpCodes.Ldc_I4, '0');           // '0' char char char[]
            il.Emit(OpCodes.Blt, notNumber);        // char char[]

            il.Emit(OpCodes.Dup);                   // char char char[]
            il.Emit(OpCodes.Ldc_I4, '9');           // '9' char char char[]
            il.Emit(OpCodes.Bgt, notNumber);        // char char[]

            il.Emit(OpCodes.Pop);                   // char[]
            il.LoadScratchInt2();                    // start char[]
            il.LoadScratchInt();                   // length start char[]
            NewParseLong(il);                       // <long value>
            il.ConvertToFromLong(underlying);       // value
            il.Emit(OpCodes.Br, finished);          // value

            il.MarkLabel(notNumber);                // char char[]
            il.Emit(OpCodes.Pop);                   // char[]
            il.LoadScratchInt2();                   // start char[]
            il.LoadScratchInt();                    // length start char[]
            SubstringAsNewString(il);               // string
            il.Emit(OpCodes.Call, parseEnum);       // value

            il.MarkLabel(finished);                 // value
        }

        /// <summary>
        /// Stack is expected to be
        ///   - char[]
        ///   
        /// With scratch int #2 = start, and scratch int #3 = stop, and scratch long = 0
        /// 
        /// Stack ends as
        ///   - value
        /// </summary>
        private static void NewParseLongImpl(ILGenerator il)
        {
            var notNumber = il.DefineLabel();
            var loop = il.DefineLabel();
            var finished = il.DefineLabel();

            // char[]
            il.MarkLabel(loop);
            il.Emit(OpCodes.Dup);                   // char[] char[]
            il.LoadScratchInt2();                   // i char[] char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char char[]
            il.Emit(OpCodes.Ldc_I4, '0');           // '0' char char[]
            il.Emit(OpCodes.Sub);                   // <char-'0'> char[]
            il.Emit(OpCodes.Dup);                   // <char-'0'> <char-'0'> char[]
            il.Emit(OpCodes.Ldc_I4_0);              // 0 <char-'0'> <char-'0'> char[]
            il.Emit(OpCodes.Blt, notNumber);        // <char-'0'> char[]

            il.Emit(OpCodes.Dup);           // <char-'0'> <char-'0'> char[]
            il.Emit(OpCodes.Ldc_I4, 9);     // 9 <char-'0'> <char-'0'> char[]
            il.Emit(OpCodes.Bgt, notNumber);// <char-'0'> char[]

            il.Emit(OpCodes.Conv_I8);       // <char-'0'> char[]
            il.LoadScratchLong();           // total <char-'0'> char[]
            il.Emit(OpCodes.Ldc_I4, 10);    // 10 total <char-'0'> char[]
            il.Emit(OpCodes.Conv_I8);       // 10 total <char-'0'> char[]
            il.Emit(OpCodes.Mul_Ovf_Un);    // <total*10> <char-'0'> char[]
            il.Emit(OpCodes.Add_Ovf_Un);    // <total*10+(char-'0')> char[]
            il.StoreScratchLong();          // char[]
            il.LoadScratchInt2();           // i char[]
            il.Emit(OpCodes.Ldc_I4_1);      // 1 i  char[]
            il.Emit(OpCodes.Add);           // <i+1> char[]
            il.StoreScratchInt2();          // char[]
            il.LoadScratchInt2();           // <i+1> char[]
            il.LoadScratchInt();            // stop <i+1> char[]
            il.Emit(OpCodes.Beq, finished); // char[]

            il.Emit(OpCodes.Br, loop);      // char[]

            il.MarkLabel(notNumber);    // <char-'0'> char[]
            il.Emit(OpCodes.Pop);       // char[]
            il.Emit(OpCodes.Pop);       // --empty--
            il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor(new Type[0])); // exception
            il.Emit(OpCodes.Throw);     // --empty--

            il.MarkLabel(finished);     // char[]
            il.Emit(OpCodes.Pop);       // --empty--
            il.LoadScratchLong();       // value
        }

        /// <summary>
        /// Stack is expected to be
        ///   - length start char[]
        ///   
        /// results in a stack of
        ///   - value
        ///   
        /// Where value is an int64;
        /// 
        /// The emitted code may trash the scratch numbers
        /// </summary>
        private static void NewParseLong(ILGenerator il)
        {
            var isNegative = il.DefineLabel();
            var finished = il.DefineLabel();

            // length start char[]

            il.StoreScratchInt();   // start char[]
            il.StoreScratchInt2();  // char[]
            il.LoadScratchInt();    // length char[]
            il.LoadScratchInt2();   // start length char[]
            il.Emit(OpCodes.Add);   // <length+start> char[]
            il.StoreScratchInt();   // char[]

            il.Emit(OpCodes.Ldc_I4_0);  // 0 char[]
            il.Emit(OpCodes.Conv_I8);   // 0 char[]
            il.StoreScratchLong();      // char[]

            il.Emit(OpCodes.Dup);                   // char[] char[]
            il.LoadScratchInt2();                   // i char[] char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char char[]

            il.Emit(OpCodes.Ldc_I4, '-');       // '-' char char[]
            il.Emit(OpCodes.Beq, isNegative);   // char[]

            NewParseLongImpl(il);               // char[]
            il.Emit(OpCodes.Br, finished);      // value

            il.MarkLabel(isNegative);           // char[]
            il.LoadScratchInt2();               // i char[]
            il.Emit(OpCodes.Ldc_I4_1);          // 1 i char[]
            il.Emit(OpCodes.Add);               // <i+1> char[]
            il.StoreScratchInt2();              // char[]
            NewParseLongImpl(il);               // value
            il.Emit(OpCodes.Neg);               // value

            il.MarkLabel(finished);
        }

        /// <summary>
        /// Expects a stack of
        ///  - length start char[]
        ///  
        /// Ends with 
        ///  - string
        ///  
        /// may smash the scratch numbers
        /// </summary>
        internal static void SubstringAsNewString(ILGenerator il)
        {
            var substring = typeof(string).GetMethod("Substring", new[] { typeof(int), typeof(int) });

            il.StoreScratchInt();               // start char[] *built
            il.StoreScratchInt2();              // char[] *built
            il.Emit(OpCodes.Pop);               // *built
            il.Emit(OpCodes.Ldarg_0);           // string *built
            il.LoadScratchInt2();               // start string *built
            il.LoadScratchInt();                // length start string *built
            il.Emit(OpCodes.Call, substring);   // string *built
        }

        internal static void NewParseImpl(ILGenerator il, Type memberType, string format)
        {
            // length start char[] *built

            if (memberType == typeof(string))
            {
                SubstringAsNewString(il);

                return;
            }

            if (memberType == typeof(bool))
            {
                NewParseBool(il);  // value *built
                return;
            }

            if (memberType == typeof(DateTime))
            {
                SubstringAsNewString(il);           // string *built

                if (string.IsNullOrEmpty(format))
                {
                    var parse = typeof(DateTime).GetMethod("Parse", new[] { typeof(string) });
                    il.Emit(OpCodes.Call, parse);   // DateTime *built
                }
                else
                {
                    var invariantCulture = typeof(CultureInfo).GetProperty("CurrentCulture").GetGetMethod();
                    var parse = typeof(DateTime).GetMethod("ParseExact", new[] { typeof(string), typeof(string), typeof(IFormatProvider) });

                    il.Emit(OpCodes.Ldstr, format);             // format string *built
                    il.Emit(OpCodes.Call, invariantCulture);    // IFormatProvider format string *built
                    il.Emit(OpCodes.Call, parse);               // DateTime *built
                }

                return;
            }

            if (memberType == typeof(TimeSpan))
            {
                SubstringAsNewString(il);  // string *built

                if (string.IsNullOrEmpty(format))
                {
                    var parse = typeof(TimeSpan).GetMethod("Parse", new[] { typeof(string) });
                    il.Emit(OpCodes.Call, parse);   // DateTime *built
                }
                else
                {
                    var invariantCulture = typeof(CultureInfo).GetProperty("CurrentCulture").GetGetMethod();
                    var parse = typeof(TimeSpan).GetMethod("ParseExact", new[] { typeof(string), typeof(string), typeof(IFormatProvider) });

                    il.Emit(OpCodes.Ldstr, format);             // format string *built
                    il.Emit(OpCodes.Call, invariantCulture);    // IFormatProvider format string *built
                    il.Emit(OpCodes.Call, parse);               // DateTime *built
                }

                return;
            }

            if (memberType == typeof(Guid))
            {
                SubstringAsNewString(il);   // string *built

                if (string.IsNullOrEmpty(format))
                {
                    var parse = typeof(Guid).GetMethod("Parse", new[] { typeof(string) });
                    il.Emit(OpCodes.Call, parse);   // Guid *built
                }
                else
                {
                    var parse = typeof(Guid).GetMethod("ParseExact", new[] { typeof(string), typeof(string) });
                    
                    il.Emit(OpCodes.Ldstr, format);     // format string *built
                    il.Emit(OpCodes.Call, parse);       // Guid *built
                }

                return;
            }

            if (memberType.IsEnum)
            {
                NewParseEnum(il, memberType);
                return;
            }

            // Deals with decimal numbers (floats, doubles, and so on)
            var parseMethodCall = ParseMethodFor(memberType);
            if (parseMethodCall != null)
            {
                SubstringAsNewString(il);               // string *built
                il.Emit(OpCodes.Call, parseMethodCall); // value *built
                return;
            }

            NewParseLong(il);
            il.ConvertToFromLong(memberType);
        }

        private static T _SetNoValue<T>()
        {
            return default(T);
        }

        /// <summary>
        /// Call this to emit IL that will parse whatever's on the stack into the appropriate type
        /// for member, and set member to it (on T, which is in arg 1).
        /// 
        /// Expects stack of :
        /// - length start char[] *built 
        /// 
        /// The stack is empty at the end, and scratch integers may be smashed.
        /// </summary>
        internal static void NewParseAndSet(this ILGenerator il, MemberInfo member, string dateFormat)
        {
            var memberType = 
                member is PropertyInfo ?
                    ((PropertyInfo)member).PropertyType :
                    ((FieldInfo)member).FieldType;
            var isNullable = Nullable.GetUnderlyingType(memberType) != null;
            memberType = Nullable.GetUnderlyingType(memberType) ?? memberType;

            var nullableType = isNullable ?  typeof(Nullable<>).MakeGenericType(memberType) : null;

            var setNoValue = typeof(ILHelpers).GetMethod("_SetNoValue", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(nullableType ?? memberType);

            var noValue = il.DefineLabel();
            var setMember = il.DefineLabel();

            // Stack Starts
            // - length start char[] *built

            il.Emit(OpCodes.Dup);           // length length start char[] *built
            il.Emit(OpCodes.Ldc_I4_0);      // 0 length length start char[] *built
            il.Emit(OpCodes.Beq, noValue);  // length start char[] *built

            NewParseImpl(il, memberType, dateFormat);   // value *built

            if (isNullable)
            {
                var nullableConst = nullableType.GetConstructor(new[] { memberType });
                
                il.Emit(OpCodes.Newobj, nullableConst); // value *built
            }

            il.Emit(OpCodes.Br, setMember); // value *built

            // Branch here if there's not value
            il.MarkLabel(noValue);  // length start char[] *built
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, setNoValue);

            il.MarkLabel(setMember);    // value *built

            if (member is FieldInfo)
            {
                var asField = (FieldInfo)member;
                il.Emit(OpCodes.Stfld, asField);    // --empty--
            }
            else
            {
                var asProp = (PropertyInfo)member;
                il.Emit(OpCodes.Call, asProp.GetSetMethod());   // --empty--
            }
        }
    }
}
