using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;

namespace FluentStringParser
{
    public static partial class FStringParser
    {
        public abstract class FStringTemplate<T> where T : class
        {
            internal abstract int NeededStringScratchSpace { get; }

            internal virtual FStringTemplate<T> Append(FStringTemplate<T> template)
            {
                var copy = new List<FStringTemplate<T>>();
                copy.Add(this);

                var asCombo = template as Combo<T>;
                if (asCombo != null)
                {
                    copy.AddRange(asCombo.Templates);
                }
                else
                {
                    copy.Add(template);
                }

                return new Combo<T> { Templates = copy };
            }

            /// <summary>
            /// Emits code to execute the operation desired.
            /// 
            /// Stack starts empty.
            /// 
            /// The length of toParse is available as local 0.
            /// toParse as a char[] is available as local 1
            /// the accumulator is available as local 2
            /// A parsing buffer char[] is available as local 3 of size toParse.Length
            /// local 4 is a scratch int32
            /// A scatch char[] is available as local 5, it is *at least* size NeededStringScratchSpace.
            /// 
            /// Argument 0 is the string (as a string) being parsed.
            /// Argument 1 is the object being built.
            /// Argument 2 is the "onFailure(string, T)" function.
            /// 
            /// If the operation cannot be completed (the string to find is not found, for instance)
            /// then onFailure should be called and the function should return immediately.
            /// 
            /// Otherwise, execution should fall through with the stack empty, but accumulator should
            /// have been updated to the current position in the string.
            /// </summary>
            internal abstract void Emit(ILGenerator il);

            public virtual Action<string, T> Seal()
            {
                throw new InvalidOperationException("Cannot Seal " + this.GetType().Name);
            }
        }

        class Combo<T> : FStringTemplate<T> where T : class
        {
            internal override int NeededStringScratchSpace
            {
                get { return Templates.Max(m => m.NeededStringScratchSpace); }
            }

            public List<FStringTemplate<T>> Templates { get; set; }

            internal override void Emit(ILGenerator il)
            {
                foreach (var template in Templates.Where(e => !(e is FElse<T>)))
                {
                    template.Emit(il);
                }
            }

            internal override FStringTemplate<T> Append(FStringTemplate<T> template)
            {
                if (Templates.Last() is FElse<T>)
                {
                    throw new InvalidOperationException("No operation can follow an Else");
                }

                if (Templates.Last() is FTakeRest<T> && !(template is FElse<T>))
                {
                    throw new InvalidOperationException("No operation other than Else can follow a TakeRest");
                }

                var copy = new List<FStringTemplate<T>>(Templates);

                var asCombo = template as Combo<T>;
                if (asCombo != null)
                {
                    copy.AddRange(asCombo.Templates);
                }
                else
                {
                    copy.Add(template);
                }

                return new Combo<T> { Templates = copy };
            }

            public override Action<string, T> Seal()
            {
                var elses = Templates.OfType<FElse<T>>();
                if (elses.Count() > 1) throw new InvalidOperationException("Only one else can be used in a template");

                Action<string, T> onFailure;
                if (elses.Count() == 1)
                {
                    onFailure = elses.Single().Call;
                }
                else
                {
                    onFailure = (a, b) => { };
                }

                var name = "fstringcombo" + Guid.NewGuid();
                var method = new DynamicMethod(name, typeof(void), new[] { typeof(string), typeof(T), typeof(Action<string, T>) }, true);

                var il = method.GetILGenerator();

                il.Initialize(NeededStringScratchSpace);

                // Put the whole thing in a try/catch
                il.BeginExceptionBlock();

                Emit(il);

                // Being the catch block for the method wide try/catch
                il.BeginCatchBlock(typeof(Exception));                          // exception
                var skipFailure = il.DefineLabel();
                
                // Don't re-run the failure callback,
                il.Emit(OpCodes.Isinst, typeof(ILHelpers.ControlException));    // bool
                il.Emit(OpCodes.Brtrue_S, skipFailure);                         // --empty--

                il.CallFailureAndReturn<T>(0, dontReturn: true);                // --empty--

                il.MarkLabel(skipFailure);

                il.EndExceptionBlock();

                il.Emit(OpCodes.Ret);

                var inner = (Action<string, T, Action<string, T>>)method.CreateDelegate(typeof(Action<string, T, Action<string, T>>));

                Action<string, T> ret = (str, t) => inner(str, t, onFailure);

                return ret;
            }
        }

        class FSkipUntil<T> : FStringTemplate<T> where T : class
        {
            internal override int NeededStringScratchSpace
            {
                get { return Until.Length; }
            }

            internal string Until { get; set; }

            internal override void Emit(ILGenerator il)
            {
                il.SetScratchSpace(Until);

                var failure = il.DefineLabel();
                var forL = il.DefineLabel();
                var possibleMatch = il.DefineLabel();
                var resume = il.DefineLabel();
                var finished = il.DefineLabel();

                il.MarkLabel(forL);

                il.LoadAccumulator();                       // accumulator
                il.LoadToParseLength();                     // toParse.Length accumulator
                il.Emit(OpCodes.Ldc_I4, Until.Length - 1);  // <Until.Length - 1> toParse.Length accumulator
                il.Emit(OpCodes.Sub);                       // <toParse.Length - Until.Length + 1> accumulator
                il.Emit(OpCodes.Beq_S, failure);            // --empty--

                il.LoadToParse();           // *char[]
                il.LoadAccumulator();       // accumulator *char[]
                il.Emit(OpCodes.Ldelem_I2); // char
                il.LoadScratchSpace();      // *char[] char
                il.Emit(OpCodes.Ldc_I4_0);  // 0 *char[] char
                il.Emit(OpCodes.Ldelem_I2); // char char
                il.Emit(OpCodes.Beq_S, possibleMatch); // --empty--

                il.MarkLabel(resume);
                il.IncrementAccumulator();
                il.Emit(OpCodes.Br_S, forL);  // repeat the loop

                //-- when we've got a hit on the first char in scratch comparsed to toParse, we come here --//
                il.MarkLabel(possibleMatch);
                il.CheckForMatchFromOne(Until, resume, finished);

                //-- when something goes wrong, this gets called --//
                il.MarkLabel(failure);  // --empty--
                il.CallFailureAndReturn<T>(0);

                //-- branch here when we've actually had success matching --//
                il.MarkLabel(finished);
            }
        }

        class FTakeUntil<T> : FStringTemplate<T> where T : class
        {
            internal override int NeededStringScratchSpace
            {
                get { return Until.Length; }
            }

            internal string Until { get; set; }
            internal MemberInfo Into { get; set; }
            internal string DateFormat { get; set; }

            internal override void Emit(ILGenerator il)
            {
                il.SetScratchSpace(Until);

                il.LoadObjectBeingBuild();  // *built
                il.LoadAccumulator();       // start *built

                var failure = il.DefineLabel();
                var forL = il.DefineLabel();
                var possibleMatch = il.DefineLabel();
                var resume = il.DefineLabel();
                var finished = il.DefineLabel();

                il.MarkLabel(forL);

                il.LoadAccumulator();                       // accumulator start *built
                il.LoadToParseLength();                     // toParse.Length accumulator start *built
                il.Emit(OpCodes.Ldc_I4, Until.Length - 1);  // <Until.Length - 1> toParse.Length accumulator start *built
                il.Emit(OpCodes.Sub);                       // <toParse.Length - Until.Length + 1> accumulator start *built
                il.Emit(OpCodes.Beq, failure);              // start *built

                il.LoadToParse();           // *char[] start *built
                il.LoadAccumulator();       // accumulator *char[] start *built
                il.Emit(OpCodes.Ldelem_I2); // char start *built
                il.LoadScratchSpace();      // *char[] char start *built
                il.Emit(OpCodes.Ldc_I4_0);  // 0 *char[] char start *built
                il.Emit(OpCodes.Ldelem_I2); // char char start *built
                il.Emit(OpCodes.Beq_S, possibleMatch); // start *built

                il.MarkLabel(resume);
                il.IncrementAccumulator();      // start *built
                il.Emit(OpCodes.Br_S, forL);    // repeat the loop

                //-- when we've got a hit on the first char in scratch comparsed to toParse, we come here --//
                il.MarkLabel(possibleMatch);
                il.CheckForMatchFromOne(Until, resume, finished);

                //-- when something goes wrong, this gets called --//
                il.MarkLabel(failure);          // start *built
                il.CallFailureAndReturn<T>(2);

                //-- branch here when we've actually had success matching --//
                il.MarkLabel(finished);                 // start *built

                var copyArray = typeof(Array).GetMethod("Copy", new[] { typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int) });

                il.StoreScratchInt();                   // *built
                il.LoadToParse();                       // <*char[] toParse> *built
                il.LoadScratchInt();                    // start <*char[] toParse> *built
                il.LoadParseBuffer();                   // <*char[] parseBuffer> start <*char[] toParse> *built
                il.Emit(OpCodes.Ldc_I4_0);              // 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.LoadAccumulator();                   // accumulator 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.LoadScratchInt();                    // start accumulator 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.Emit(OpCodes.Sub);                   // <accumulator-start> 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.Emit(OpCodes.Ldc_I4, Until.Length);  // Until.Length <accumulator-start> 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.Emit(OpCodes.Sub);                   // <accumulator-start-Until.Length> 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.Emit(OpCodes.Dup);                   // <accumulator-start-Until.Length> <accumulator-start-Until.Length> 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.StoreScratchInt();                   // <accumulator-start-Until.Length> 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.Emit(OpCodes.Call, copyArray);       // *built
                il.LoadParseBuffer();                   // <*char[] toParse> *built
                il.LoadScratchInt();                    // length <*char[] toParse> *built
                il.ParseAndSet(Into, DateFormat);
            }

            public override Action<string, T> Seal()
            {
                var name = "fstringtakeuntil" + Guid.NewGuid();
                var method = new DynamicMethod(name, typeof(void), new[] { typeof(string), typeof(T), typeof(Action<string, T>) }, true);

                var il = method.GetILGenerator();

                il.Initialize(NeededStringScratchSpace);

                // Put the whole thing in a try/catch
                il.BeginExceptionBlock();

                Emit(il);

                // Being the catch block for the method wide try/catch
                il.BeginCatchBlock(typeof(Exception));                          // exception
                var skipFailure = il.DefineLabel();

                // Don't re-run the failure callback,
                il.Emit(OpCodes.Isinst, typeof(ILHelpers.ControlException));    // bool
                il.Emit(OpCodes.Brtrue_S, skipFailure);                         // --empty--

                il.CallFailureAndReturn<T>(0, dontReturn: true);                // --empty--

                il.MarkLabel(skipFailure);

                il.EndExceptionBlock();

                il.Emit(OpCodes.Ret);

                var inner = (Action<string, T, Action<string, T>>)method.CreateDelegate(typeof(Action<string, T, Action<string, T>>));

                Action<string, T> ret = (str, t) => inner(str, t, (a, b) => { });

                return ret;
            }
        }

        class FTakeRest<T> : FStringTemplate<T> where T : class
        {
            internal override int NeededStringScratchSpace
            {
                get { return 0; }
            }

            internal MemberInfo Into { get; set; }

            internal string DateFormat { get; set; }

            internal override FStringTemplate<T> Append(FStringTemplate<T> template)
            {
                throw new InvalidOperationException("TakeRest cannot be followed by any operation");
            }

            internal override void Emit(ILGenerator il)
            {
                il.LoadObjectBeingBuild();  // *built

                il.LoadToParse();           // <*char[] toParse> *built
                il.LoadAccumulator();       // start <*char[] toParse> *built
                il.LoadParseBuffer();       // <*char[] parseBuffer> start <*char[] toParse> *built
                il.Emit(OpCodes.Ldc_I4_0);  // 0 <*char[] parseBuffer> start <*char[] toParse> *built

                il.LoadToParseLength();     // toParseLength 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.LoadAccumulator();       // start toParseLength 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.Emit(OpCodes.Sub);       // <toParseLength - start> 0 <*char[] parseBuffer> start <*char[] toParse> *built
                
                il.Emit(OpCodes.Dup);       // <toParseLength - start> <toParseLength - start> 0 <*char[] parseBuffer> start <*char[] toParse> *built
                il.StoreScratchInt();       // <toParseLength - start> 0 <*char[] parseBuffer> start <*char[] toParse> *built

                var copyArray = typeof(Array).GetMethod("Copy", new[] { typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int) });
                
                il.Emit(OpCodes.Call, copyArray);   // *built
                il.LoadParseBuffer();               // *char[] *built
                il.LoadScratchInt();                // length *char[] *built
                il.ParseAndSet(Into, DateFormat);
            }
        }

        class FElse<T> : FStringTemplate<T> where T : class
        {
            internal override int NeededStringScratchSpace
            {
                get { return 0; }
            }

            internal Action<string, T> Call { get; set; }

            internal override void Emit(ILGenerator il)
            {
                throw new InvalidOperationException("Just an Else cannot be emitted");
            }
        }

        class FMoveN<T> : FStringTemplate<T> where T : class
        {
            internal override int NeededStringScratchSpace
            {
                get { return 0; }
            }

            internal int N { get; set; }

            internal override void Emit(ILGenerator il)
            {
                var finished = il.DefineLabel();
                var failure = il.DefineLabel();

                il.LoadAccumulator();       // accumulator
                il.Emit(OpCodes.Ldc_I4, N); // N accumulator
                il.Emit(OpCodes.Add);       // <accumulator + N>
                il.StoreAccumulator();      // --empty--

                // Bounds Checking
                il.LoadAccumulator();               // accumulator
                il.Emit(OpCodes.Ldc_I4_M1);         // -1 accumulator
                il.Emit(OpCodes.Ble_S, failure);    // --empty--

                il.LoadAccumulator();               // accumulator
                il.LoadToParseLength();             // toParse.Length accumulator
                il.Emit(OpCodes.Bge_S, failure);    // --empty--

                il.Emit(OpCodes.Br_S, finished);

                // branch here if bounds checking fails
                il.MarkLabel(failure);      
                il.CallFailureAndReturn<T>(0);

                // branch here when we're done, without error
                il.MarkLabel(finished);
            }
        }

        class FTakeN<T> : FStringTemplate<T> where T : class
        {
            internal override int NeededStringScratchSpace
            {
                get { return 0; }
            }

            internal int N { get; set; }
            internal MemberInfo Into { get; set; }
            internal string DateFormat { get; set; }

            internal override void Emit(ILGenerator il)
            {
                var finished = il.DefineLabel();
                var failure = il.DefineLabel();

                il.LoadAccumulator();           // accumulator
                il.Emit(OpCodes.Ldc_I4, N);     // N accumulator
                il.Emit(OpCodes.Add);           // <accumulator+N>
                il.LoadToParseLength();         // toParse.Length <accumulator+N>
                il.Emit(OpCodes.Bge, failure);  // --empty--

                var copyArray = typeof(Array).GetMethod("Copy", new[] { typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int) });

                il.LoadToParse();                 // <char[]* toParse>
                il.LoadAccumulator();             // accumulator <char[]* toParse>
                il.LoadParseBuffer();             // <char[]* parseBuffer> acccumulator <char[]* toParse>
                il.Emit(OpCodes.Ldc_I4_0);        // 0 <char[]* parseBuffer> acccumulator <char[]* toParse>
                il.Emit(OpCodes.Ldc_I4, N);       // N 0 <char[]* parseBuffer> acccumulator <char[]* toParse>
                il.Emit(OpCodes.Call, copyArray); // --empty--

                il.LoadObjectBeingBuild();        // *built
                il.LoadParseBuffer();             // <char[]* parseBuffer> *built
                il.Emit(OpCodes.Ldc_I4, N);   // N <char[]* parseBuffer> *built
                il.ParseAndSet(Into, DateFormat); // --empty--
                il.Emit(OpCodes.Br, finished);

                // branch here if bounds checking fails
                il.MarkLabel(failure);          // --empty--
                il.CallFailureAndReturn<T>(0);

                // branch here when we're ready to continue with parsing
                il.MarkLabel(finished);
            }

            public override Action<string, T> Seal()
            {
                var name = "fstringtaken" + Guid.NewGuid();
                var method = new DynamicMethod(name, typeof(void), new[] { typeof(string), typeof(T), typeof(Action<string, T>) }, true);

                var il = method.GetILGenerator();

                il.Initialize(NeededStringScratchSpace);

                // Put the whole thing in a try/catch
                il.BeginExceptionBlock();

                Emit(il);

                // Being the catch block for the method wide try/catch
                il.BeginCatchBlock(typeof(Exception));                          // exception
                var skipFailure = il.DefineLabel();

                // Don't re-run the failure callback,
                il.Emit(OpCodes.Isinst, typeof(ILHelpers.ControlException));    // bool
                il.Emit(OpCodes.Brtrue_S, skipFailure);                         // --empty--

                il.CallFailureAndReturn<T>(0, dontReturn: true);                // --empty--

                il.MarkLabel(skipFailure);

                il.EndExceptionBlock();

                il.Emit(OpCodes.Ret);

                var inner = (Action<string, T, Action<string, T>>)method.CreateDelegate(typeof(Action<string, T, Action<string, T>>));

                Action<string, T> ret = (str, t) => inner(str, t, (a, b) => { });

                return ret;
            }
        }
    }
}