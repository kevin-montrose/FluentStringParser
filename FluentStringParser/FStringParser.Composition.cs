using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace FluentStringParser
{
    public static partial class FStringParser
    {
        /// <summary>
        /// Advance in the string until <paramref name="needle"/> is encountered.
        /// 
        /// Subsequent directives begin after <paramref name="needle"/>.
        /// 
        /// If <paramref name="needle"/> is not found, any Else directive is run.
        /// </summary>
        public static FStringTemplate<T> Till<T>(string needle) where T : class
        {
            return new FSkipUntil<T> { Until = needle };
        }

        /// <summary>
        /// Advance in the string until <paramref name="needle"/> is encountered.
        /// 
        /// Subsequent directives begin after <paramref name="needle"/>.
        /// 
        /// If <paramref name="needle"/> is not found, any Else directive is run.
        /// </summary>
        public static FStringTemplate<T> Till<T>(this FStringTemplate<T> template, string needle) where T : class
        {
            return template.Append(Till<T>(needle));
        }

        private static void ValidateMember<T>(MemberInfo member, string dateFormat)
        {
            if (member.DeclaringType != typeof(T))
            {
                throw new InvalidOperationException(member.Name + " must be on " + typeof(T).Name);
            }

            if (!(member is FieldInfo || member is PropertyInfo))
            {
                throw new InvalidOperationException(member.Name + " must be a field or property, found " + member);
            }

            if (member is PropertyInfo)
            {
                var asProp = (PropertyInfo)member;

                if (!asProp.CanWrite)
                {
                    throw new InvalidOperationException(member.Name + " is an unsettable property");
                }
            }

            if (!string.IsNullOrEmpty(dateFormat))
            {
                Type t = null;
                if (member is PropertyInfo) t = ((PropertyInfo)member).PropertyType;
                if (member is FieldInfo) t = ((FieldInfo)member).FieldType;

                if (!(t == typeof(DateTime) || t == typeof(DateTime?)))
                {
                    throw new InvalidOperationException(member.Name + " is not a DateTime, and cannot have a dateFormat specified");
                }
            }
        }


        /// <summary>
        /// Takes characters from the input string, and puts them in the property
        /// or field referenced by <paramref name="member"/> until <paramref name="until"/> is encountered.
        /// 
        /// <paramref name="until"/> is not placed in <paramref name="member"/>.
        /// 
        /// Subsequent directives begin after <paramref name="until"/>
        /// 
        /// If <paramref name="until"/> is not found, any Else directive is run.
        /// </summary>
        public static FStringTemplate<T> Take<T>(string until, MemberInfo member, string dateFormat = null) where T : class
        {
            ValidateMember<T>(member, dateFormat);

            return new FTakeUntil<T> { Until = until, Into = member, DateFormat = dateFormat };
        }

        /// <summary>
        /// Takes characters from the input string, and puts them in the property
        /// or field referenced by <paramref name="member"/> until <paramref name="needle"/> is encountered.
        /// 
        /// <paramref name="needle"/> is not placed in <paramref name="member"/>.
        /// 
        /// Subsequent directives begin after <paramref name="needle"/>
        /// 
        /// If <paramref name="needle"/> is not found, any Else directive is run.
        /// </summary>
        public static FStringTemplate<T> Take<T>(this FStringTemplate<T> template, string until, MemberInfo member, string dateFormat = null) where T : class
        {
            return template.Append(Take<T>(until, member, dateFormat));
        }

        /// <summary>
        /// Every set of directives can have a single Else directive.
        /// 
        /// If any directive fails (expected string not found, moves before the start of the input string, or past
        /// the end) the Else directive will be invoked.
        /// 
        /// The Else directive must be the last one to appear.
        /// </summary>
        public static FStringTemplate<T> Else<T>(this FStringTemplate<T> template, Action<string, T> call) where T : class
        {
            return template.Append(new FElse<T> { Call = call });
        }

        /// <summary>
        /// Every directive can have a single TakeRest directive.
        /// 
        /// This takes the remainder of the input string, and places it into the field
        /// or property specified by <paramref name="member"/>.
        /// 
        /// Nothing can follow a TakeRest directive except an Else.
        /// </summary>
        public static FStringTemplate<T> TakeRest<T>(this FStringTemplate<T> template, MemberInfo member, string dateFormat = null) where T : class
        {
            ValidateMember<T>(member, dateFormat);

            return template.Append(new FTakeRest<T> { Into = member, DateFormat = dateFormat });
        }

        /// <summary>
        /// Advances a certain number of characters in the string.
        /// 
        /// If the current index into the string is moved out of bounds,
        /// the directive fails.
        /// </summary>
        public static FStringTemplate<T> Skip<T>(int n) where T : class
        {
            if (n <= 0) throw new ArgumentException("Skip expects a positive, non-zero, value; found [" + n + "]");

            return new FMoveN<T> { N = n };
        }

        /// <summary>
        /// Advances a certain number of characters in the string.
        /// 
        /// If the current index into the string is moved out of bounds,
        /// the directive fails.
        /// </summary>
        public static FStringTemplate<T> Skip<T>(this FStringTemplate<T> template, int n) where T : class
        {
            return template.Append(Skip<T>(n));
        }

        /// <summary>
        /// Backtracks a certain number of characters in the string.
        /// 
        /// If the current index into the string is moved out of bounds,
        /// the directive fails.
        /// </summary>
        public static FStringTemplate<T> Back<T>(int n) where T : class
        {
            if (n <= 0) throw new ArgumentException("Back expects a positive, non-zero, value; found [" + n + "]");

            return new FMoveN<T> { N = -1 * n };
        }

        /// <summary>
        /// Backtracks a certain number of characters in the string.
        /// 
        /// If the current index into the string is moved out of bounds,
        /// the directive fails.
        /// </summary>
        public static FStringTemplate<T> Back<T>(this FStringTemplate<T> template, int n) where T : class
        {
            return template.Append(Back<T>(n));
        }
    }
}
