using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace FluentStringParser
{
    public static class FStringParser
    {
        private static void KnownTypeCheck(string name, Type t)
        {
            // strip the nullable bits off
            t = Nullable.GetUnderlyingType(t) ?? t;

            var known =
                new[]
                { 
                    typeof(sbyte), typeof(byte), typeof(short), typeof(ushort),
                    typeof(int), typeof(uint), typeof(long), typeof(ulong),
                    typeof(float), typeof(double), typeof(decimal), typeof(bool),
                    typeof(DateTime), typeof(TimeSpan), typeof(string)
                }.Contains(t);

            if (!known && !t.IsEnum) throw new ArgumentException(name + " is not a supported type");
        }

        private static void ValidateMember<T>(MemberInfo member, string format)
        {
            if (member.DeclaringType != typeof(T))
            {
                throw new ArgumentException(member.Name + " must be on " + typeof(T).Name);
            }

            if (!(member is FieldInfo || member is PropertyInfo))
            {
                throw new ArgumentException(member.Name + " must be a field or property, found " + member);
            }

            if (member is PropertyInfo)
            {
                var asProp = (PropertyInfo)member;

                if (!asProp.CanWrite)
                {
                    throw new ArgumentException(member.Name + " is an unsettable property");
                }

                if (asProp.GetSetMethod().IsStatic)
                {
                    throw new ArgumentException(member.Name + " is static, must be an instance property");
                }

                KnownTypeCheck(member.Name, asProp.PropertyType);
            }

            if (member is FieldInfo)
            {
                var asField = (FieldInfo)member;

                if (asField.IsStatic)
                {
                    throw new ArgumentException(member.Name + " is static, must be an instance field");
                }

                KnownTypeCheck(member.Name, asField.FieldType);
            }

            if (!string.IsNullOrEmpty(format))
            {
                Type t = null;
                if (member is PropertyInfo) t = ((PropertyInfo)member).PropertyType;
                if (member is FieldInfo) t = ((FieldInfo)member).FieldType;

                if (!(t == typeof(DateTime) || t == typeof(DateTime?) || t == typeof(TimeSpan) || t == typeof(TimeSpan?)))
                {
                    throw new ArgumentException(member.Name + " is not a DateTime or TimeSpan, and cannot have a format specified");
                }

                try
                {
                    if (t == typeof(DateTime) || t == typeof(DateTime?))
                    {
                        DateTime.UtcNow.ToString(format);
                    }
                    else
                    {
                        TimeSpan.FromSeconds(1).ToString(format);
                    }
                }
                catch (FormatException f)
                {
                    throw new ArgumentException("format [" + format + "] is invalid, " + f.Message, f);
                }
            }
        }

        private static void ValidateNeedle(string val, string name)
        {
            if (string.IsNullOrEmpty(val))
            {
                throw new ArgumentException(name + " cannot be null or empty");
            }
        }

        /// <summary>
        /// Advance in the string until <paramref name="needle"/> is encountered.
        /// 
        /// Subsequent directives begin after <paramref name="needle"/>.
        /// 
        /// If <paramref name="needle"/> is not found, any Else directive is run.
        /// </summary>
        public static FStringTemplate<T> Until<T>(string needle) where T : class
        {
            ValidateNeedle(needle, "needle");

            return new FSkipUntil<T> { Until = needle };
        }

        /// <summary>
        /// Advance in the string until <paramref name="needle"/> is encountered.
        /// 
        /// Subsequent directives begin after <paramref name="needle"/>.
        /// 
        /// If <paramref name="needle"/> is not found, any Else directive is run.
        /// </summary>
        public static FStringTemplate<T> Until<T>(this FStringTemplate<T> template, string needle) where T : class
        {
            return template.Append(Until<T>(needle));
        }

        private static MemberInfo FindMember<T>(string member)
        {
            var members = typeof(T).GetMember(member).Where(w => w is PropertyInfo || w is FieldInfo).ToList();

            if (members.Count == 0) throw new ArgumentException(member + " field or property does not exist on " + typeof(T).Name);

            return members.Single();
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
        public static FStringTemplate<T> Take<T>(string until, string member, string format = null) where T : class
        {
            return Take<T>(until, FindMember<T>(member), format);
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
        public static FStringTemplate<T> Take<T>(string until, MemberInfo member, string format = null) where T : class
        {
            ValidateMember<T>(member, format);
            ValidateNeedle(until, "until");

            return new FTakeUntil<T> { Until = until, Into = member, Format = format };
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
        public static FStringTemplate<T> Take<T>(this FStringTemplate<T> template, string until, string member, string format = null) where T : class
        {
            return Take<T>(template, until, FindMember<T>(member), format);
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
        public static FStringTemplate<T> Take<T>(this FStringTemplate<T> template, string until, MemberInfo member, string format = null) where T : class
        {
            return template.Append(Take<T>(until, member, format));
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
        public static FStringTemplate<T> TakeRest<T>(this FStringTemplate<T> template, string member, string format = null) where T : class
        {
            return TakeRest<T>(template, FindMember<T>(member), format);
        }

        /// <summary>
        /// Every directive can have a single TakeRest directive.
        /// 
        /// This takes the remainder of the input string, and places it into the field
        /// or property specified by <paramref name="member"/>.
        /// 
        /// Nothing can follow a TakeRest directive except an Else.
        /// </summary>
        public static FStringTemplate<T> TakeRest<T>(this FStringTemplate<T> template, MemberInfo member, string format = null) where T : class
        {
            ValidateMember<T>(member, format);

            return template.Append(new FTakeRest<T> { Into = member, Format = format });
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

        /// <summary>
        /// Backtracks until <paramref name="until"/> is encountered in the string.
        /// 
        /// The current index will be placed after the found string.
        /// 
        /// If <paramref name="until"/> is not found, the directive fails.
        /// </summary>
        public static FStringTemplate<T> Back<T>(this FStringTemplate<T> template, string until) where T : class
        {
            ValidateNeedle(until, "until");

            return template.Append(new FMoveBackUntil<T> { Until = until });
        }

        /// <summary>
        /// Take a specific number of characters from the input string, parse them, and put
        /// them into the given property on T.
        /// 
        /// Expects a positive, non-zero n.
        /// </summary>
        public static FStringTemplate<T> Take<T>(int n, string into, string format = null) where T : class
        {
            return Take<T>(n, FindMember<T>(into), format);
        }

        /// <summary>
        /// Take a specific number of characters from the input string, parse them, and put
        /// them into the given property on T.
        /// 
        /// Expects a positive, non-zero n.
        /// </summary>
        public static FStringTemplate<T> Take<T>(int n, MemberInfo into, string format = null) where T : class
        {
            if (n <= 0) throw new ArgumentException("Take expects a positive, non-zero value; found [" + n + "]");

            ValidateMember<T>(into, format);

            return new FTakeN<T> { N = n, Into = into, Format = format };
        }

        /// <summary>
        /// Take a specific number of characters from the input string, parse them, and put
        /// them into the given property on T.
        /// 
        /// Expects a positive, non-zero n.
        /// </summary>
        public static FStringTemplate<T> Take<T>(this FStringTemplate<T> template, int n, string into, string format = null) where T : class
        {
            return Take<T>(template, n, FindMember<T>(into), format);
        }

        /// <summary>
        /// Take a specific number of characters from the input string, parse them, and put
        /// them into the given property on T.
        /// 
        /// Expects a positive, non-zero n.
        /// </summary>
        public static FStringTemplate<T> Take<T>(this FStringTemplate<T> template, int n, MemberInfo into, string format = null) where T : class
        {
            return template.Append(Take<T>(n, into, format));
        }

        /// <summary>
        /// Concatenate one series of directives with another.
        /// 
        /// Will error if multiple TakeRest or Else directives
        /// are defined by the union of the two series.
        /// </summary>
        public static FStringTemplate<T> Append<T>(this FStringTemplate<T> left, FStringTemplate<T> right) where T : class
        {
            return left.Append(right);
        }
    }
}
