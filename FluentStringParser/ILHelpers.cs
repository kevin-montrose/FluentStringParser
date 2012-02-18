using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection.Emit;
using System.Reflection;
using System.Globalization;

namespace FluentStringParser
{
    internal static class ILHelpers
    {
        internal class ControlException : Exception { }

        internal static void Initialize(this ILGenerator il, int neededScratchSpace)
        {
            var toParseLength = il.DeclareLocal(typeof(int));
            var toParseAsChar = il.DeclareLocal(typeof(char[]));
            var accumulator = il.DeclareLocal(typeof(int));
            var parseBuffer = il.DeclareLocal(typeof(char[]));
            var scratchInt = il.DeclareLocal(typeof(int));
            var scratchLong = il.DeclareLocal(typeof(long));
            var scratchInt2 = il.DeclareLocal(typeof(int));
            if (toParseLength.LocalIndex != 0) throw new InvalidOperationException();
            if (toParseAsChar.LocalIndex != 1) throw new InvalidOperationException();
            if (accumulator.LocalIndex != 2) throw new InvalidOperationException();
            if (parseBuffer.LocalIndex != 3) throw new InvalidOperationException();
            if (scratchInt.LocalIndex != 4) throw new InvalidOperationException();
            if (scratchLong.LocalIndex != 5) throw new InvalidOperationException();
            if (scratchInt2.LocalIndex != 6) throw new InvalidOperationException();

            il.Emit(OpCodes.Ldarg_0);           // *string
            il.EmitCall(OpCodes.Call, typeof(string).GetMethod("ToCharArray", new Type[0]), null);  // *char[]
            il.Emit(OpCodes.Stloc, toParseAsChar);   // --empty--

            il.Emit(OpCodes.Ldloc, toParseAsChar);  // *char[]
            il.Emit(OpCodes.Ldlen);                 // <length of *char[]>
            il.Emit(OpCodes.Stloc, toParseLength);  // --empty--

            il.Emit(OpCodes.Ldc_I4_0);           // 0
            il.Emit(OpCodes.Stloc, accumulator); // --empty--

            il.LoadToParseLength();                 // toParse.Length
            il.Emit(OpCodes.Newarr, typeof(char));  // *char
            il.Emit(OpCodes.Stloc, parseBuffer);    // --empty--

            il.Emit(OpCodes.Ldc_I4_0);          // 0
            il.Emit(OpCodes.Stloc, scratchInt); // --empty--

            il.Emit(OpCodes.Ldc_I4_0);          // 0
            il.Emit(OpCodes.Conv_I8);           // <long 0>
            il.Emit(OpCodes.Stloc, scratchLong);// --empty--

            il.Emit(OpCodes.Ldc_I4_0);          // 0
            il.Emit(OpCodes.Stloc, scratchInt2);// --empty--

            if (neededScratchSpace > 0)
            {
                var scratch = il.DeclareLocal(typeof(char[]));
                if (scratch.LocalIndex != 7) throw new InvalidOperationException();

                il.Emit(OpCodes.Ldc_I4, neededScratchSpace);  // NeededStringScrathSpace
                il.Emit(OpCodes.Newarr, typeof(char));              // *char[]
                il.Emit(OpCodes.Stloc, scratch);                    // --empty--
            }
        }

        internal static void LoadObjectBeingBuild(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldarg_1);
        }

        internal static void SetScratchSpace(this ILGenerator il, string toStr)
        {
            // Stack (pretending): --empty--

            for (int i = 0; i < toStr.Length; i++)
            {
                il.LoadScratchSpace();
                il.Emit(OpCodes.Ldc_I4, i);                 // i <*char[] scratch>
                il.Emit(OpCodes.Ldc_I4_S, toStr[i]);        // c i <*char[] scratch>
                il.Emit(OpCodes.Stelem, typeof(char));      // --empty--
            }
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

        internal static void LoadParseBuffer(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc_3);
        }

        internal static void LoadScratchInt(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc, 4);
        }

        internal static void StoreScratchInt(this ILGenerator il)
        {
            il.Emit(OpCodes.Stloc, 4);
        }

        internal static void LoadScratchLong(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc, 5);
        }

        internal static void StoreScratchLong(this ILGenerator il)
        {
            il.Emit(OpCodes.Stloc, 5);
        }

        internal static void LoadScratchInt2(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc, 6);
        }

        internal static void StoreScratchInt2(this ILGenerator il)
        {
            il.Emit(OpCodes.Stloc, 6);
        }

        internal static void LoadScratchSpace(this ILGenerator il)
        {
            il.Emit(OpCodes.Ldloc, 7);
        }

        internal static void IncrementAccumulator(this ILGenerator il)
        {
            il.LoadAccumulator();      // accumulator
            il.Emit(OpCodes.Ldc_I4_1); // 1 accumulator
            il.Emit(OpCodes.Add);      // accumuatlor++
            il.Emit(OpCodes.Stloc_2);  // --empty--
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
                il.LoadToParse();           // <*char[] toParse>
                il.LoadAccumulator();       // accumulator <*char[] toParse>
                il.Emit(OpCodes.Ldc_I4, i); // i accumulator <*char[] toParse>
                il.Emit(OpCodes.Add);       // <accumulator+i> <*char[] toParse>
                il.Emit(OpCodes.Ldelem_I2); // char
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
            if (t == typeof(double) || t == typeof(double?))
            {
                return typeof(double).GetMethod("Parse", new[] { typeof(string) });
            }

            if (t == typeof(float) || t == typeof(float?))
            {
                return typeof(float).GetMethod("Parse", new[] { typeof(string) });
            }

            if (t == typeof(decimal) || t == typeof(decimal?))
            {
                return typeof(decimal).GetMethod("Parse", new[] { typeof(string) });
            }

            throw new NotImplementedException("No parser for " + t.Name + " defined");
        }

        private static Type GetNullableEquivalentType(Type t)
        {
            return typeof(Nullable<>).MakeGenericType(t);
        }

        private static void CheckCharacter(this ILGenerator il, int loc, char against, Label ifNot)
        {
            var fallThrough = il.DefineLabel();

            //Stack: *char[]
            
            // Check upper case version of against
            il.Emit(OpCodes.Dup);                   // *char[] *char[]
            il.Emit(OpCodes.Ldc_I4, loc);           // loc *char[] *char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char *char[]
            il.Emit(OpCodes.Ldc_I4, char.ToUpperInvariant(against));       // char char *char[]
            il.Emit(OpCodes.Beq_S, fallThrough);    // *char[]

            // Check lower case
            il.Emit(OpCodes.Dup);                   // *char[] *char[]
            il.Emit(OpCodes.Ldc_I4, loc);           // loc *char[] *char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char *char[]
            il.Emit(OpCodes.Ldc_I4, char.ToLowerInvariant(against));       // char char *char[]
            il.Emit(OpCodes.Beq_S, fallThrough);    // *char[]

            il.Emit(OpCodes.Br, ifNot);             // *char[]

            // Branch here if we got a hit
            il.MarkLabel(fallThrough);              // *char[]
        }

        /// <summary>
        /// Stack is expected to be
        ///   - len 0 *char[]
        ///   
        /// for easy coercion into a string if needed
        /// 
        /// results in stack of
        ///   - value
        /// where value will be a boolean (int32 1 or 0)  
        /// 
        /// The emitted code may trash the scratch numbers
        /// </summary>
        private static void ParseBool(ILGenerator il)
        {
            // Stack: len 0 *char[]

            var isFalseCleanup3 = il.DefineLabel();
            var isFalseCleanup1 = il.DefineLabel();
            var isFalse = il.DefineLabel();
            var isTrue = il.DefineLabel();
            var isOneChar = il.DefineLabel();
            var finished = il.DefineLabel();

            // Check if there's nothing to parse, eq. false
            il.Emit(OpCodes.Dup);       // 0 len len 0 *char[]
            il.Emit(OpCodes.Ldc_I4_0);  // 0 len len 0 *char[]
            il.Emit(OpCodes.Beq, isFalseCleanup3);

            // If there's only one char, we need to do some more tests
            il.Emit(OpCodes.Dup);             // len len 0 *char[]
            il.Emit(OpCodes.Ldc_I4_1);        // 1 len len 0 *char[]
            il.Emit(OpCodes.Beq, isOneChar);  // len 0 *char[]

            // If it's not 4 chars long, it must be false
            il.Emit(OpCodes.Dup);               // len len 0 *char[]
            il.Emit(OpCodes.Ldc_I4_4);          // 4 len len 0 *char[]
            il.Emit(OpCodes.Bne_Un, isFalseCleanup3); // len 0 *char[]

            //We're here if we have 4 chars to check against (T|t)rue
            // Stack Starts:                            // len 0 char[]
            il.Emit(OpCodes.Pop);                       // 0 *char[]
            il.Emit(OpCodes.Pop);                       // *char[]
            il.CheckCharacter(0, 't', isFalseCleanup1); // *char[]
            il.CheckCharacter(1, 'r', isFalseCleanup1); // *char[]
            il.CheckCharacter(2, 'u', isFalseCleanup1); // *char[]
            il.CheckCharacter(3, 'e', isFalseCleanup1); // *char[]
            il.Emit(OpCodes.Pop);                       // --empty--

            // We know we're true now
            il.MarkLabel(isTrue);      // --empty--
            il.Emit(OpCodes.Ldc_I4_1); // false
            il.Emit(OpCodes.Br_S, finished);

            // Branch here if we have to check just one character
            il.MarkLabel(isOneChar);                // len 0 *char[]
            il.Emit(OpCodes.Pop);                   // 0 *char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char
            il.Emit(OpCodes.Ldc_I4, (char)'1');     // '1' char
            il.Emit(OpCodes.Beq_S, isTrue);         // --empty--
            il.Emit(OpCodes.Br_S, isFalse);         // --empty--

            // Branch here if we know the value is false
            il.MarkLabel(isFalseCleanup3);     //len 0 *char[]
            il.Emit(OpCodes.Pop);      // 0 *char[]
            il.Emit(OpCodes.Pop);      // *char[]
            // Separate label for when we only have 1 thing on the stack
            il.MarkLabel(isFalseCleanup1);
            il.Emit(OpCodes.Pop);      // --empty--
            // Separate label for false case that doesn't need cleanup
            il.MarkLabel(isFalse);
            il.Emit(OpCodes.Ldc_I4_0); // false

            // Branch here if we've got true/false on the stack and are ready to return
            il.MarkLabel(finished);
        }

        /// <summary>
        /// Stack is expected to be
        ///   - len 0 *char[]
        ///   
        /// for easy coercion into a string if needed
        /// 
        /// results in stack of
        ///   - value
        /// where value will be a TimeSpan
        ///   
        /// The emitted code may trash the scratch numbers
        /// </summary>
        private static void ParseTime(ILGenerator il, string timeSpanFormat)
        {
            var strConst = typeof(string).GetConstructor(new[] { typeof(char[]), typeof(int), typeof(int) });

            // Stack: len 0 *char[]
            il.Emit(OpCodes.Newobj, strConst);  // *string

            if (string.IsNullOrEmpty(timeSpanFormat))
            {
                var parse = typeof(TimeSpan).GetMethod("Parse", new[] { typeof(string) });
                il.Emit(OpCodes.Call, parse);   // value
                return;
            }

            var invariantCulture = typeof(CultureInfo).GetProperty("InvariantCulture").GetGetMethod();
            var parseFormat = typeof(TimeSpan).GetMethod("ParseExact", new[] { typeof(string), typeof(string), typeof(IFormatProvider) });

            il.Emit(OpCodes.Ldstr, timeSpanFormat);     // *string *string
            il.Emit(OpCodes.Call, invariantCulture);    // *IFormatProvider *string *string
            il.Emit(OpCodes.Call, parseFormat);         // value
        }

        /// <summary>
        /// Stack is expected to be
        ///   - len 0 *char[]
        ///   
        /// for easy coercion into a string if needed
        /// 
        /// results in stack of
        ///   - value
        /// where value will be a DateTime
        ///   
        /// The emitted code may trash the scratch numbers
        /// </summary>
        private static void ParseDate(ILGenerator il, string dateFormat)
        {
            var strConst = typeof(string).GetConstructor(new[] { typeof(char[]), typeof(int), typeof(int) });

            // Stack: len 0 *char[]
            il.Emit(OpCodes.Newobj, strConst);  // *string

            if (string.IsNullOrEmpty(dateFormat))
            {
                var parse = typeof(DateTime).GetMethod("Parse", new[] { typeof(string) });
                il.Emit(OpCodes.Call, parse);   // value
                return;
            }

            var invariantCulture = typeof(CultureInfo).GetProperty("InvariantCulture").GetGetMethod();
            var parseFormat = typeof(DateTime).GetMethod("ParseExact", new[]{typeof(string), typeof(string), typeof(IFormatProvider)});

            il.Emit(OpCodes.Ldstr, dateFormat);         // *string *string
            il.Emit(OpCodes.Call, invariantCulture);    // *IFormatProvider *string *string
            il.Emit(OpCodes.Call, parseFormat);         // value
        }

        /// <summary>
        /// Emits the (conceptual) for loop that parses a character array into an in64.
        /// 
        /// Stack Starts
        ///  - *char[] *char[]
        /// and ends
        ///  - int *char[] *char[]
        /// with the parsed long in the scratch long local
        /// 
        /// Does all math unsigned, negation must take place elsewhere.
        /// </summary>
        private static void ParseLongLoopImp(ILGenerator il, int startAt, Label done, Label beginLoop)
        {
            // Init scratch long
            il.Emit(OpCodes.Dup);                   // *char[] *char[]
            il.Emit(OpCodes.Ldc_I4, startAt);       // i *char[] *char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char *char[]
            il.Emit(OpCodes.Ldc_I4_S, '0');         // '0' char *char[]
            il.Emit(OpCodes.Sub);                   // <char - '0'> *char[]
            il.Emit(OpCodes.Conv_I8);               // <long char-'0'> *char[]
            il.StoreScratchLong();                  // *char[]

            // Setup the loop
            il.Emit(OpCodes.Dup);            // *char[] *char[]
            il.Emit(OpCodes.Ldc_I4, startAt);// i *char[] *char[]

            il.MarkLabel(beginLoop);        // i *char[] *char[]
            il.Emit(OpCodes.Ldc_I4_1);      // 1 i *char[] *char[]
            il.Emit(OpCodes.Add);           // <i+1> *char[] *char[]
            il.Emit(OpCodes.Dup);           // <i+1> <i+1> *char[] *char[]
            il.LoadScratchInt();            // len <i+1> <i+1> *char[] *char[]
            il.Emit(OpCodes.Beq, done);     // <i+1> *char[] *char[]

            il.Emit(OpCodes.Dup);                   // <i+1> <i+1> *char[] *char[]
            il.StoreScratchInt2();                  // <i+1> *char[] *char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char *char[]
            il.Emit(OpCodes.Ldc_I4_S, '0');         // '0' char *char[]
            il.Emit(OpCodes.Sub);                   // <char-'0'> *char[]
            il.Emit(OpCodes.Conv_I8);               // <long char-'0'> *char[]
            il.LoadScratchLong();                   // long <long char-'0'> *char[]
            il.Emit(OpCodes.Ldc_I4_S, 10);          // 10 long <long char-'0'> *char[]
            il.Emit(OpCodes.Conv_I8);               // <long 10> long <long char-'0'> *char[]
            il.Emit(OpCodes.Mul_Ovf_Un);            // <long*10> <long char-'0'> *char[]
            il.Emit(OpCodes.Add_Ovf_Un);            // <long*10 + (char-'0')> *char[]
            il.StoreScratchLong();                  // *char[]
            il.Emit(OpCodes.Dup);                   // *char[] *char[]
            il.LoadScratchInt2();                   // <i+1> *char[] *char[]
            il.Emit(OpCodes.Br_S, beginLoop);       // <i+1> *char[] *char[]
        }

        /// <summary>
        /// /// Stack is expected to be
        ///   - len 0 *char[]
        ///   
        /// for easy coercion into a string if needed
        /// 
        /// results in stack of
        ///   - value
        /// where value will be an in64
        /// 
        /// The emitted code may trash the scratch numbers
        /// </summary>
        private static void ParseLong(ILGenerator il)
        {
            var failure = il.DefineLabel();
            var done = il.DefineLabel();
            var beginPosLoop = il.DefineLabel();
            var negative = il.DefineLabel();
            var negDone = il.DefineLabel();
            var beginNegLoop = il.DefineLabel();
            var finished = il.DefineLabel();

            // Stack Starts:                // len 0 *char[]
            il.StoreScratchInt();           // 0 *char[]
            il.LoadScratchInt();            // len 0 *char[]
            il.Emit(OpCodes.Beq, failure);  // *char[]

            // Negative check
            il.Emit(OpCodes.Dup);                   // *char[] *char[]
            il.Emit(OpCodes.Ldc_I4_0);              // 0 *char[] *char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char *char[]
            il.Emit(OpCodes.Ldc_I4_S, '-');         // '-' char *char[]
            il.Emit(OpCodes.Beq, negative);         // *char[]

            ParseLongLoopImp(il, 0, done, beginPosLoop);

            // Branch here when we know we're parsing a negative number
            il.MarkLabel(negative);                     // *char[]
            ParseLongLoopImp(il, 1, negDone, beginNegLoop);
            
            // Branch here after parsing a negative number (as a positive one)
            il.MarkLabel(negDone);      // i *char[] *char[]
            il.Emit(OpCodes.Ldc_I4_0);  // 0 i *char[] *char[]
            il.Emit(OpCodes.Conv_I8);    // <long 0> i *char[] *char[]
            il.LoadScratchLong();       // long <long 0> i *char[] *char[]
            il.Emit(OpCodes.Sub_Ovf);   // -long i *char[] *char[]
            il.StoreScratchLong();      // i *char[] *char[]

            // Branch here when we're done converting
            il.MarkLabel(done);             // i *char[] *char[]
            il.Emit(OpCodes.Pop);           // *char[] *char[]
            il.Emit(OpCodes.Pop);           // *char[]
            il.Emit(OpCodes.Pop);           // --empty--
            il.LoadScratchLong();           // value
            il.Emit(OpCodes.Br_S, finished);// value

            // Branch here if we failed conversion
            il.MarkLabel(failure);      // *char[]
            il.Emit(OpCodes.Pop);       // --empty--
            il.Emit(OpCodes.Ldc_I4_0);  // 0
            il.Emit(OpCodes.Conv_I8);   // <long 0>

            // Branch here when the value is on the stack and we're ready to resume
            il.MarkLabel(finished);
        }

        private static T _ParseEnum<T>(string val) where T : struct
        {
            return (T)Enum.Parse(typeof(T), val, ignoreCase: true);
        }

        /// <summary>
        /// Stack is expected to be
        ///   - len 0 *char[]
        ///   
        /// for easy coercion into a string if needed
        /// 
        /// results in stack of
        ///   - value
        /// where value will be an in64
        /// 
        /// The emitted code may trash the scratch numbers
        /// </summary>
        private static void ParseEnum(ILGenerator il, Type enumType)
        {
            var underlying = Enum.GetUnderlyingType(enumType);
            var strConst = typeof(string).GetConstructor(new[] { typeof(char[]), typeof(int), typeof(int) });
            var parseEnum = typeof(ILHelpers).GetMethod("_ParseEnum", BindingFlags.Static | BindingFlags.NonPublic);
            parseEnum = parseEnum.MakeGenericMethod(enumType);

            var notNumber1 = il.DefineLabel();
            var notNumber0 = il.DefineLabel();
            var isNumber = il.DefineLabel();
            var isNumberLoop = il.DefineLabel();
            var finished = il.DefineLabel();

            // Stack Starts: len 0 *char[]

            // First, check to see if this is a number that needs to be coerced
            il.Emit(OpCodes.Dup);       // len len 0 *char[]
            il.StoreScratchInt();       // len 0 *char[]
            il.Emit(OpCodes.Pop);       // 0 *char[]
            il.Emit(OpCodes.Pop);       // *char[]
            il.Emit(OpCodes.Dup);       // *char[] *char[]
            il.Emit(OpCodes.Ldc_I4_0);  // 0 *char[] *char[]
            il.StoreScratchInt2();      // *char[] *char[]
            
            il.MarkLabel(isNumberLoop);             // *char[] *char[]
            il.LoadScratchInt2();                   // i *char[] *char[]
            il.Emit(OpCodes.Ldelem, typeof(char));  // char *char[]
            il.Emit(OpCodes.Dup);                   // char char *char[]
            il.Emit(OpCodes.Ldc_I4, '9');           // '9' char char *char[]
            il.Emit(OpCodes.Bgt, notNumber1);       // char *char[]
            il.Emit(OpCodes.Ldc_I4, '0');           // '0' char *char[]
            il.Emit(OpCodes.Blt, notNumber0);       // *char[]

            il.LoadScratchInt2();                   // i *char[]
            il.Emit(OpCodes.Ldc_I4_1);              // 1 i *char[]
            il.Emit(OpCodes.Add);                   // <i+1> *char[]
            il.Emit(OpCodes.Dup);                   // <i+1> <i+1> *char[]
            il.LoadScratchInt();                    // len <i+1> <i+1> *char[]
            il.Emit(OpCodes.Beq_S, isNumber);       // <i+1> *char[]
            il.StoreScratchInt2();                  // *char[]
            il.Emit(OpCodes.Dup);                   // *char[] *char[]
            il.Emit(OpCodes.Br_S, isNumberLoop);    // *char[] *char[]

            // branch here when we've determine that we're parsing a number
            il.MarkLabel(isNumber);     // len *char[]
            il.Emit(OpCodes.Pop);       // *char[]
            il.Emit(OpCodes.Ldc_I4_0);  // 0 *char[]
            il.LoadScratchInt();        // len 0 *char[]
            ParseLong(il);              // <long value>

            if (underlying == typeof(byte) || underlying == typeof(sbyte))
            {
                il.Emit(OpCodes.Conv_Ovf_I1);  // <byte value>
            }

            if(underlying == typeof(short) || underlying == typeof(ushort))
            {
                il.Emit(OpCodes.Conv_Ovf_I2);  // <short value>
            }

            if (underlying == typeof(int) || underlying == typeof(uint))
            {
                il.Emit(OpCodes.Conv_Ovf_I4); // <int value>
            }

            il.Emit(OpCodes.Br, finished);

            // Branch here if we determine we're not parsing a number
            il.MarkLabel(notNumber1);   // char *char[]
            il.Emit(OpCodes.Pop);       // *char[]
            il.MarkLabel(notNumber0);   // *char[]

            il.Emit(OpCodes.Ldc_I4_0);          // 0 *char[]
            il.LoadScratchInt();                // len 0 *char[]
            il.Emit(OpCodes.Newobj, strConst);  // <toParse string>
            il.Emit(OpCodes.Call, parseEnum);   // value

            // Branch here when we're done
            il.MarkLabel(finished);     // value
        }

        /// <summary>
        /// Emit IL to convert what's on the stack into the desired value.
        /// 
        /// Note that this code doesn't deal with Nullable requirements,
        /// that's done right before setting the field or property.
        /// 
        /// Stack is expected to be
        ///   - len 0 *char[]
        ///   
        /// for easy coercion into a string if needed
        /// 
        /// results in stack of
        ///   - value
        ///   
        /// The emitted code may trash the scratch int
        /// </summary>
        private static void ParseImpl(ILGenerator il, Type type, string format)
        {
            var strConst = typeof(string).GetConstructor(new[] { typeof(char[]), typeof(int), typeof(int) });

            type = Nullable.GetUnderlyingType(type) ?? type;

            // simple case first
            if (type == typeof(string))
            {
                il.Emit(OpCodes.Newobj, strConst);      // value
                return;
            }

            if (type == typeof(bool))
            {
                ParseBool(il);  // value
                return;
            }

            if (type == typeof(DateTime))
            {
                ParseDate(il, format);  // value
                return;
            }

            if (type == typeof(TimeSpan))
            {
                ParseTime(il, format);
                return;
            }

            if (type == typeof(int))
            {
                ParseLong(il);                // <long value>
                il.Emit(OpCodes.Conv_Ovf_I4); // value
                return;
            }

            if (type == typeof(uint))
            {
                ParseLong(il);                      // <long value>
                il.Emit(OpCodes.Conv_Ovf_I4_Un);    // value
                return;
            }

            if (type == typeof(short))
            {
                ParseLong(il);              // <long value>
                il.Emit(OpCodes.Conv_Ovf_I2);   // value
                return;
            }

            if (type == typeof(ushort))
            {
                ParseLong(il);                  // <long value>
                il.Emit(OpCodes.Conv_Ovf_I2_Un);// value
                return;
            }

            if (type == typeof(byte))
            {
                ParseLong(il);                   // <long value>
                il.Emit(OpCodes.Conv_Ovf_I1_Un); // value
                return;
            }

            if (type == typeof(sbyte))
            {
                ParseLong(il);                  // <long value>
                il.Emit(OpCodes.Conv_Ovf_I1);   // value
                return;
            }

            if (type == typeof(long) || type == typeof(ulong))
            {
                ParseLong(il);  // value
                return;
            }

            if (type.IsEnum)
            {
                ParseEnum(il, type); // value;
                return;
            }

            var parseMethodCall = ParseMethodFor(type);
            il.Emit(OpCodes.Newobj, strConst);      // *string
            il.Emit(OpCodes.Call, parseMethodCall); // value
        }

        /// <summary>
        /// Call this to emit IL that will parse whatever's on the stack into the appropriate type
        /// for member, and set member to it (on T, which is in arg 1).
        /// 
        /// The stack is 
        ///   - length *char[] *built 
        /// at the start of this method.
        /// 
        /// This method destroys the scratch integer space.
        /// </summary>
        internal static void ParseAndSet(this ILGenerator il, MemberInfo member, string dateFormat)
        {
            var hasValue = il.DefineLabel();
            var finished = il.DefineLabel();

            var memberType = 
                member is PropertyInfo ?
                    ((PropertyInfo)member).PropertyType :
                    ((FieldInfo)member).FieldType;

            // Stack: length *char[] *built

            // A length of zero means there's nothing to set
            il.Emit(OpCodes.Dup);                   // length length *char[] *built
            il.Emit(OpCodes.Brtrue_S, hasValue);    // length *char[] *built

            il.Emit(OpCodes.Pop);                   // *char[] *built
            il.Emit(OpCodes.Pop);                   // *built
            il.Emit(OpCodes.Pop);                   //--empty--
            il.Emit(OpCodes.Br, finished);

            il.MarkLabel(hasValue);                 // length *char[] *built
            il.StoreScratchInt();                   // *char[] *built
            il.Emit(OpCodes.Ldc_I4_0);              // 0 *char[] *built
            il.LoadScratchInt();                    // len 0 *char[] *built

            ParseImpl(il, memberType, dateFormat);

            // We've got the value to set, but it may need to be converted to a nullable
            if (Nullable.GetUnderlyingType(memberType) != null)
            {
                var nullableConstructor = memberType.GetConstructor(new[] { Nullable.GetUnderlyingType(memberType) });

                il.Emit(OpCodes.Newobj, nullableConstructor);   // <value as proper type> *built
            }

            //Now we just need to set the property/value
            if (member is FieldInfo)
            {
                var asField = (FieldInfo)member;
                il.Emit(OpCodes.Stfld, asField);
            }
            else
            {
                var asProp = (PropertyInfo)member;
                il.Emit(OpCodes.Call, asProp.GetSetMethod());
            }

            il.MarkLabel(finished);
        }
    }
}
