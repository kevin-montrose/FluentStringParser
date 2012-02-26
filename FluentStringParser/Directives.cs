using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;

namespace FluentStringParser
{
    public abstract class FSTemplate<T> where T : class
    {
        internal virtual FSTemplate<T> Append(FSTemplate<T> template)
        {
            var copy = new List<FSTemplate<T>>();
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

        /// <summary>
        /// Returns the "call this if things go wrong" delegate
        /// to bake into the parser when Seal() is called
        /// </summary>
        [ExcludeFromCodeCoverage]
        internal virtual Action<string, T> GetOnFailure()
        {
            throw new NotImplementedException();
        }

        private static Exception _Exec(Exception e)
        {
            if(!(e is ILHelpers.ControlException))
            {
                Debug.WriteLine(e);
            }

            return e;
        }

        public Action<string, T> Seal()
        {
            var onFailure = GetOnFailure();

            var name = "fstring" + GetType().Name.ToLowerInvariant() + Guid.NewGuid();
            var method = new DynamicMethod(name, typeof(void), new[] { typeof(string), typeof(T), typeof(Action<string, T>) }, true);

            var il = method.GetILGenerator();

            il.Initialize();

            // Put the whole thing in a try/catch
            il.BeginExceptionBlock();

            Emit(il);

            // Being the catch block for the method wide try/catch
            il.BeginCatchBlock(typeof(Exception));                          // exception

#if DEBUG
            il.Emit(OpCodes.Call, typeof(FSTemplate<T>).GetMethod("_Exec", BindingFlags.Static | BindingFlags.NonPublic));  // exception
#endif
            
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

    class Combo<T> : FSTemplate<T> where T : class
    {
        public List<FSTemplate<T>> Templates { get; set; }

        internal override void Emit(ILGenerator il)
        {
            foreach (var template in Templates.Where(e => !(e is FElse<T>)))
            {
                template.Emit(il);
            }
        }

        internal override Action<string, T> GetOnFailure()
        {
            var elses = Templates.OfType<FElse<T>>();

            Action<string, T> onFailure;
            if (elses.Count() == 1)
            {
                onFailure = elses.Single().Call;
            }
            else
            {
                onFailure = (a, b) => { };
            }

            return onFailure;
        }

        internal override FSTemplate<T> Append(FSTemplate<T> template)
        {
            if (Templates.Last() is FElse<T>)
            {
                throw new InvalidOperationException("No operation can follow an Else");
            }

            if (Templates.Last() is FTakeRest<T> && !(template is FElse<T>))
            {
                throw new InvalidOperationException("No operation other than Else can follow a TakeRest");
            }

            var copy = new List<FSTemplate<T>>(Templates);

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
    }

    class FSkipUntil<T> : FSTemplate<T> where T : class
    {
        internal string Until { get; set; }

        internal override void Emit(ILGenerator il)
        {
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

            il.LoadFromToParseAtAccumulator();  // char

            il.Emit(OpCodes.Ldc_I4, Until[0]);  // char char
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

        internal override Action<string, T> GetOnFailure()
        {
            return (s, o) => { };
        }
    }

    class FMoveBackUntil<T> : FSTemplate<T> where T : class
    {
        public string Until { get; set; }

        internal override void Emit(ILGenerator il)
        {
            var failure = il.DefineLabel();
            var finished = il.DefineLabel();
            var forLoop = il.DefineLabel();
            var possibleMatch = il.DefineLabel();
            var resume = il.DefineLabel();

            il.LoadToParseLength();                 // toParse.Length
            il.LoadAccumulator();                   // accumulator toParse.Length
            il.Emit(OpCodes.Sub);                   // <toParse.Length - accumulator>
            il.Emit(OpCodes.Ldc_I4, Until.Length);  // Until.Length <toParse.Length - accumulator>
            il.Emit(OpCodes.Bge_S, forLoop);        // --empty--

            // Set the accumulator to toParse.Length - Until.Length so we won't access past the buffer
            il.LoadToParseLength();     // toParse.Length
            il.LoadAccumulator();       // accumulator toParse.Length
            il.Emit(OpCodes.Sub);       // toParse.Length - accumulator
            il.StoreAccumulator();      // --empty--=

            // Start of the for loop
            il.MarkLabel(forLoop);          // --empty--
            il.LoadAccumulator();           // accumulator
            il.Emit(OpCodes.Ldc_I4_0);      // 0 accumulator
            il.Emit(OpCodes.Blt, failure);  // --empty--

            il.LoadFromToParseAtAccumulator();  // char
            il.Emit(OpCodes.Ldc_I4, Until[0]);  // char char
            il.Emit(OpCodes.Beq_S, possibleMatch); // --empty--

            il.MarkLabel(resume);           // --empty--
            il.LoadAccumulator();           // accumulator
            il.Emit(OpCodes.Ldc_I4_M1);     // -1 accumulator
            il.Emit(OpCodes.Add);           // <accumulator-1>
            il.StoreAccumulator();          // --empty--
            il.Emit(OpCodes.Br, forLoop);   // --empty--

            // branch here when we've got a possible match to check
            il.MarkLabel(possibleMatch);
            il.CheckForMatchFromOne(Until, resume, finished);

            // branch here when we know we've failed
            il.MarkLabel(failure);          // --empty--
            il.CallFailureAndReturn<T>(0);  // --empty--

            // branch here when we've got a match
            il.MarkLabel(finished);         // --empty--
        }
    }

    class FTakeUntil<T> : FSTemplate<T> where T : class
    {
        internal string Until { get; set; }
        internal MemberInfo Into { get; set; }
        internal string Format { get; set; }

        internal override void Emit(ILGenerator il)
        {
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

            il.LoadFromToParseAtAccumulator();  // char start *built

            il.Emit(OpCodes.Ldc_I4, Until[0]);  // char char start *built

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
            il.MarkLabel(finished);             // start *built

            il.StoreScratchInt();                  // *built
            il.LoadToParse();                      // char[] *built
            il.LoadScratchInt();                   // start char[] *built
            il.LoadAccumulator();                  // accumulator start char[] *built
            il.LoadScratchInt();                   // start accumulator start char[] *built
            il.Emit(OpCodes.Sub);                  // <accumulator - start> start char[] *built
            il.Emit(OpCodes.Ldc_I4, Until.Length); // Until.Length <accumulator - start> start char[] *built
            il.Emit(OpCodes.Sub);                  // <accumulator-start-Until.Length> start char[] *built
            il.NewParseAndSet(Into, Format);       // --empty--
        }

        internal override Action<string, T> GetOnFailure()
        {
            return (a, b) => { };
        }
    }

    class FTakeRest<T> : FSTemplate<T> where T : class
    {
        internal MemberInfo Into { get; set; }

        internal string Format { get; set; }

        [ExcludeFromCodeCoverage]
        internal override FSTemplate<T> Append(FSTemplate<T> template)
        {
            throw new InvalidOperationException("TakeRest cannot be followed by any operation");
        }

        internal override void Emit(ILGenerator il)
        {
            il.LoadObjectBeingBuild();             // *built
            il.LoadToParse();                      // char[] *built
            il.LoadAccumulator();                  // start char[] *built
            il.LoadToParseLength();                // toParse.Length start char[] *built
            il.LoadAccumulator();                  // start toParse.Length start char[] *built
            il.Emit(OpCodes.Sub);                  // <toParse.Length - start> start char[] *built
            il.NewParseAndSet(Into, Format);       // --empty--
        }
    }

    class FElse<T> : FSTemplate<T> where T : class
    {
        internal Action<string, T> Call { get; set; }

        [ExcludeFromCodeCoverage]
        internal override void Emit(ILGenerator il)
        {
            throw new InvalidOperationException("Just an Else cannot be emitted");
        }
    }

    class FMoveN<T> : FSTemplate<T> where T : class
    {
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

    class FTakeN<T> : FSTemplate<T> where T : class
    {
        internal int N { get; set; }
        internal MemberInfo Into { get; set; }
        internal string Format { get; set; }

        internal override void Emit(ILGenerator il)
        {
            var finished = il.DefineLabel();
            var failure = il.DefineLabel();

            il.LoadAccumulator();           // accumulator
            il.Emit(OpCodes.Ldc_I4, N);     // N accumulator
            il.Emit(OpCodes.Add);           // <accumulator+N>
            il.LoadToParseLength();         // toParse.Length <accumulator+N>
            il.Emit(OpCodes.Bgt, failure);  // --empty--

            il.LoadObjectBeingBuild();          // *built
            il.LoadToParse();                   // char[] *built
            il.LoadAccumulator();               // start char[] *built
            il.Emit(OpCodes.Ldc_I4, N);         // length start char[] *built
            il.NewParseAndSet(Into, Format);    // --empty--
            il.Emit(OpCodes.Br, finished);      // --empty--

            // branch here if bounds checking fails
            il.MarkLabel(failure);          // --empty--
            il.CallFailureAndReturn<T>(0);

            // branch here when we're ready to continue with parsing
            il.MarkLabel(finished);
            il.LoadAccumulator();           // accumulator
            il.Emit(OpCodes.Ldc_I4, N);     // N accumulator
            il.Emit(OpCodes.Add);           // <accumulator+N>
            il.StoreAccumulator();          // --empty--
        }

        internal override Action<string, T> GetOnFailure()
        {
            return (a, b) => { };
        }
    }
}