using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;
using System.Reflection;
using System.IO;
using System.Globalization;
using FluentStringParser;
using System.Diagnostics;
using System.Threading;

namespace FluentStringParserTests
{
    [TestClass]
    public class Tests
    {
        #region Giant Regex

        private static readonly Regex Parser = new Regex(
@"(?<ClientIp>\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b):\d+\s
\[(?<CreationDate>.*)\]\s
(?<FrontEnd>[^\s]+)\s
(?<BackEnd>[^\s]+)/(?<Server>[0-9a-z-<>]+)\s
(?<Tq>[^/]+)/(?<Tw>[^/]+)/(?<Tc>[^/]+)/(?<Tr>[^/]+)/(?<Tt>[^\s]+)\s
(?<ResponseCode>[0-9-]+)\s
(?<Bytes>\d+)\s
-\s # request cookies aren't being captured
-\s # neither are response cookies
(?<TermState>.{4})\s
(?<ActConn>[^/]+)/(?<FeConn>[^/]+)/(?<BeConn>[^/]+)/(?<SrvConn>[^/]+)/(?<Retries>[^\s]+)\s
(?<SrvQueue>\d+)/(?<BackEndQueue>\d+)\s
\{ # request headers
(?<Referer>[^|]+)?\|
(?<UserAgent>[^|]+)?\|
(?<Host>[^|]+)?\|
(?<ForwardFor>[^|}]+)?\|?
(?<AcceptEncoding>[^}]+)?\}\s
(?:\{ # response headers - right now, ssl isn't sendning any response headers
(?<ContentEncoding>[^|]+)?\|
(?<IsPageView>[^|]+)?\|
(?<SqlDurationMs>[^|]+)?\|
(?<AccountId>[^|]+)?\|
(?<RouteName>[^}]+)?
\}\s)?
(?:
(?:""(?<Method>\w+)\s(?<Uri>[^\s]*)(?:\s(?<HttpVersion>http/1\.\d))?""?) # normal request; note long paths (e.g. /users/authenticate) won't be completely captured
|""(?<Uri><BADREQ>)"" # haproxy will reject some requests (e.g. path too long); BackEnd will also be ""http-in/<NOSRV>"", so IIS isn't touched
)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

        #endregion

        class LogRow
        {
            public DateTime CreationDate { get; set; }
            public string Host { get; set; }
            public string Server { get; set; }
            public Int16 ResponseCode { get; set; }
            public string Method { get; set; }
            public string Uri { get; set; }
            public string Query { get; set; }
            public string RouteName { get; set; }
            public bool IsPageView { get; set; }
            public string ClientIp { get; set; }
            public string Country { get; set; }
            public string ForwardFor { get; set; }
            public string HttpVersion { get; set; }
            public string Referer { get; set; }
            public string RefererHost { get; set; }
            public string UserAgent { get; set; }
            public string ClientOS { get; set; }
            public string ClientBrowser { get; set; }
            public string ClientBrowserVersion { get; set; }
            public string AcceptEncoding { get; set; }
            public string ContentEncoding { get; set; }
            public string FrontEnd { get; set; }
            public string BackEnd { get; set; }
            public Int32? Tq { get; set; }
            public Int32? Tw { get; set; }
            public Int32? Tc { get; set; }
            public Int32? Tr { get; set; }
            public Int32? Tt { get; set; }
            public Int32? Bytes { get; set; }
            public string TermState { get; set; }
            public Int32? ActConn { get; set; }
            public Int32? FeConn { get; set; }
            public Int32? BeConn { get; set; }
            public Int32? SrvConn { get; set; }
            public Int32? Retries { get; set; }
            public Int32? SrvQueue { get; set; }
            public Int32? BackEndQueue { get; set; }
            public Int64 Hash { get; set; }
            public Int32? AccountId { get; set; }
            public Int16? SqlDurationMs { get; set; }
            public Int32? ApplicationId { get; set; }

            public override string ToString()
            {
                var ret = "";

                foreach (var prop in this.GetType().GetProperties().OrderBy(o => o.Name))
                {
                    ret += prop.GetValue(this, null);
                }

                return ret;
            }
        }

        private static Action<string, LogRow> FTemplate;

        static Tests()
        {
            return;

            var step1 =
                FSBuilder
                .Until<LogRow>(" ");

            var step2 = 
                FSBuilder
                .Take<LogRow>(":", "ClientIp")
                .Until("[")
                .Take("]", LogProp("CreationDate"), format: "dd/MMM/yyyy:HH:mm:ss.fff")
                .Until(" ")
                .Take(" ", "FrontEnd")
                .Take("/", LogProp("BackEnd"))
                .Take(" ", "Server")
                .Take("/", LogProp("Tq"))
                .Take("/", "Tw")
                .Take("/", LogProp("Tc"))
                .Take("/", "Tr")
                .Take(" ", LogProp("Tt"))
                .Take(" ", "ResponseCode")
                .Take(" ", LogProp("Bytes"))
                .Until("- ")
                .Until("- ")
                .Take(4, "TermState")
                .Until(" ");

            var step3 = 
                FSBuilder
                .Take<LogRow>("/", LogProp("ActConn"))
                .Take("/", "FeConn")
                .Take("/", LogProp("BeConn"))
                .Take("/", "SrvConn")
                .Take(" ", LogProp("Retries"))
                .Take("/", "SrvQueue")
                .Take(" ", LogProp("BackEndQueue"))
                .Until("{")
                .Take("|", "Referer")
                .Take("|", LogProp("UserAgent"))
                .Take("|", "Host")
                .Take("|", LogProp("ForwardFor"))
                .Take("}", "AcceptEncoding")
                .Until("{")
                .Take("|", LogProp("ContentEncoding"))
                .Take("|", "IsPageView")
                .Take("|", LogProp("SqlDurationMs"))
                .Take("|", "AccountId")
                .Take("}", LogProp("RouteName"))
                .Until("\"")
                .Take(" ", "Method")
                .Take(" ", LogProp("Uri"))
                .Take("\"", "HttpVersion");

            FTemplate =
                step1.Append(step2).Append(step3)
                .Else(
                    (str, row) => 
                    { 
                        throw new Exception("Couldn't parse: " + str); 
                    }
                )
                .Seal();
        }

        private static PropertyInfo LogProp(string name)
        {
            return typeof(LogRow).GetProperty(name);
        }

        private static LogRow ParseWithRegex(string line)
        {
            var row = new LogRow();

            var match = Parser.Match(line);

            if (match.Success)
            {
                foreach (var name in Parser.GetGroupNames().Where(n => n != "0"))
                {
                    if (match.Groups[name].Length == 0) continue;

                    switch (name)
                    {
                        case "ClientIp": row.ClientIp = match.Groups[name].Value; break;
                        case "CreationDate": row.CreationDate = DateTime.ParseExact(match.Groups[name].Value, "dd/MMM/yyyy:HH:mm:ss.fff", CultureInfo.InvariantCulture); break;
                        case "FrontEnd": row.FrontEnd = match.Groups[name].Value; break;
                        case "BackEnd": row.BackEnd = match.Groups[name].Value; break;
                        case "Server": row.Server = match.Groups[name].Value; break;
                        case "Tq": row.Tq = int.Parse(match.Groups[name].Value); break;
                        case "Tw": row.Tw = int.Parse(match.Groups[name].Value); break;
                        case "Tc": row.Tc = int.Parse(match.Groups[name].Value); break;
                        case "Tr": row.Tr = int.Parse(match.Groups[name].Value); break;
                        case "Tt": row.Tt = int.Parse(match.Groups[name].Value); break;
                        case "ResponseCode": row.ResponseCode = short.Parse(match.Groups[name].Value); break;
                        case "Bytes": row.Bytes = int.Parse(match.Groups[name].Value); break;
                        case "TermState": row.TermState = match.Groups[name].Value; break;
                        case "ActConn": row.ActConn = int.Parse(match.Groups[name].Value); break;
                        case "FeConn": row.FeConn = int.Parse(match.Groups[name].Value); break;
                        case "BeConn": row.BeConn = int.Parse(match.Groups[name].Value); break;
                        case "SrvConn": row.SrvConn = int.Parse(match.Groups[name].Value); break;
                        case "Retries": row.Retries = int.Parse(match.Groups[name].Value); break;
                        case "SrvQueue": row.SrvQueue = int.Parse(match.Groups[name].Value); break;
                        case "BackEndQueue": row.BackEndQueue = int.Parse(match.Groups[name].Value); break;
                        case "Referer": row.Referer = match.Groups[name].Value; break;
                        case "UserAgent": row.UserAgent = match.Groups[name].Value; break;
                        case "Host": row.Host = match.Groups[name].Value; break;
                        case "ForwardFor": row.ForwardFor = match.Groups[name].Value; break;
                        case "AcceptEncoding": row.AcceptEncoding = match.Groups[name].Value; break;
                        case "ContentEncoding": row.ContentEncoding = match.Groups[name].Value; break;
                        case "IsPageView": row.IsPageView = match.Groups[name].Value == "1"; break;
                        case "RouteName": row.RouteName = match.Groups[name].Value; break;
                        case "AccountId": row.AccountId = int.Parse(match.Groups[name].Value); break;
                        case "SqlDurationMs": row.SqlDurationMs = short.Parse(match.Groups[name].Value); break;
                        case "ApplicationId": row.ApplicationId = int.Parse(match.Groups[name].Value); break;
                        case "Method": row.Method = match.Groups[name].Value; break;
                        case "Uri": row.Uri = match.Groups[name].Value; break;
                        case "HttpVersion": row.HttpVersion = match.Groups[name].Value; break;
                        default: throw new InvalidOperationException("Unexpected column [" + name + "]");
                    }
                }
            }

            return row;
        }

        private static LogRow ParseWithFTemplate(string line)
        {
            var row = new LogRow();
            FTemplate(line, row);
            return row;
        }

        private static LogRow ParseWithIndexOf(string _raw)
        {
            var row = new LogRow();

            int a = _raw.IndexOf(" ") + 1;
            int b = _raw.IndexOf(':', a);
            row.ClientIp = Substring(_raw, a, b);

            a = _raw.IndexOf('[', b) + 1;
            b = _raw.IndexOf(']', a);
            var date = Substring(_raw, a, b);
            row.CreationDate = ParseCreationDate(date);

            a = b + 2;
            b = _raw.IndexOf(' ', a);
            row.FrontEnd = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.BackEnd = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf(' ', a);
            row.Server = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.Tq = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.Tw = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.Tc = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.Tr = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf(' ', a);
            row.Tt = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf(' ', a);
            row.ResponseCode = short.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf(' ', a);
            row.Bytes = int.Parse(Substring(_raw, a, b));

            a = b + 5;
            b = a + 4;
            row.TermState = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.ActConn = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.FeConn = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.BeConn = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.SrvConn = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf(' ', a);
            row.Retries = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf('/', a);
            row.SrvQueue = int.Parse(Substring(_raw, a, b));

            a = b + 1;
            b = _raw.IndexOf(' ', a);
            row.BackEndQueue = int.Parse(Substring(_raw, a, b));

            a = b + 2;
            b = _raw.IndexOf('|', a);
            row.Referer = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf('|', a);
            row.UserAgent = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf('|', a);
            row.Host = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf('|', a);
            row.ForwardFor = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf('}', a);
            row.AcceptEncoding = Substring(_raw, a, b);

            a = b + 3;
            b = _raw.IndexOf('|', a);
            row.ContentEncoding = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf('|', a);
            var isPageView = Substring(_raw, a, b);
            if (!string.IsNullOrEmpty(isPageView))
            {
                row.IsPageView = byte.Parse(isPageView) == 1;
            }

            a = b + 1;
            b = _raw.IndexOf('|', a);
            var sqlDurationMs = Substring(_raw, a, b);
            if (!string.IsNullOrEmpty(sqlDurationMs))
            {
                row.SqlDurationMs = short.Parse(sqlDurationMs);
            }

            a = b + 1;
            b = _raw.IndexOf('|', a);
            var accountId = Substring(_raw, a, b);
            if (!string.IsNullOrEmpty(accountId))
            {
                row.AccountId = int.Parse(accountId);
            }

            a = b + 1;
            b = _raw.IndexOf('}', a);
            row.RouteName = Substring(_raw, a, b);

            a = b + 3;
            b = _raw.IndexOf(' ', a);
            row.Method = Substring(_raw, a, b);

            a = b + 1;
            b = _raw.IndexOf(' ', a);
            if (b < 0)
            {
                b = _raw.Length;
            }
            row.Uri = Substring(_raw, a, b);

            if (b < _raw.Length)
            {
                a = b + 1;
                b = _raw.IndexOf('"', a);
                row.HttpVersion = Substring(_raw, a, b);
            }

            return row;
        }

        public static DateTime ParseCreationDate(string date)
        {
            return DateTime.ParseExact(date, "dd/MMM/yyyy:HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None);
        }

        private static string Substring(string _raw, int a, int b)
        {
            var result = _raw.Substring(a, b - a);
            return result;
        }

        [TestMethod]
        public void HaProxyLogs()
        {
            using (var input = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("FluentStringParserTests.TestData.txt")))
            {
                var lines = input.ReadToEnd().Split('\n');

                foreach (var line in lines)
                {
                    var row1 = ParseWithRegex(line);
                    var row2 = ParseWithFTemplate(line);
                    var row3 = ParseWithIndexOf(line);

                    Assert.AreEqual(row1.ToString(), row2.ToString(), "For: " + line);
                    Assert.AreEqual(row2.ToString(), row3.ToString(), "For: " + line);
                }
            }
        }

        [TestMethod]
        public void SpeedComparison()
        {
            string[] lines;
            using (var input = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("FluentStringParserTests.TestData.txt")))
            {
                lines = input.ReadToEnd().Split('\n');
            }

            var regex = new Stopwatch();
            Action runRegex =
                delegate
                {
                    // warmup
                    foreach (var line in lines) ParseWithRegex(line);

                    regex.Restart();
                    for (int i = 0; i < 1000; i++)
                    {
                        for (int j = 0; j < lines.Length; j++)
                        {
                            ParseWithRegex(lines[j]);
                        }
                    }
                    regex.Stop();
                };

            var template = new Stopwatch();
            Action runTemplate =
                delegate
                {
                    // warmup
                    foreach (var line in lines) ParseWithFTemplate(line);

                    template.Restart();
                    for (int i = 0; i < 1000; i++)
                    {
                        for (int j = 0; j < lines.Length; j++)
                        {
                            ParseWithFTemplate(lines[j]);
                        }
                    }
                    template.Stop();
                };

            var indexOf = new Stopwatch();
            Action runIndexOf =
                delegate
                {
                    // warmup
                    foreach (var line in lines) ParseWithIndexOf(line);

                    indexOf.Restart();
                    for (int i = 0; i < 1000; i++)
                    {
                        for (int j = 0; j < lines.Length; j++)
                        {
                            ParseWithIndexOf(lines[j]);
                        }
                    }
                    indexOf.Stop();
                };

            // Run in all orders

            runRegex();
            runTemplate();
            runIndexOf();
            Assert.IsTrue(template.ElapsedMilliseconds < regex.ElapsedMilliseconds);
            Assert.IsTrue(template.ElapsedMilliseconds < indexOf.ElapsedMilliseconds);

            runRegex();
            runIndexOf();
            runTemplate();
            Assert.IsTrue(template.ElapsedMilliseconds < regex.ElapsedMilliseconds);
            Assert.IsTrue(template.ElapsedMilliseconds < indexOf.ElapsedMilliseconds);

            runTemplate();
            runRegex();
            runIndexOf();
            Assert.IsTrue(template.ElapsedMilliseconds < regex.ElapsedMilliseconds);
            Assert.IsTrue(template.ElapsedMilliseconds < indexOf.ElapsedMilliseconds);

            runTemplate();
            runIndexOf();
            runRegex();
            Assert.IsTrue(template.ElapsedMilliseconds < regex.ElapsedMilliseconds);
            Assert.IsTrue(template.ElapsedMilliseconds < indexOf.ElapsedMilliseconds);

            runIndexOf();
            runTemplate();
            runRegex();
            Assert.IsTrue(template.ElapsedMilliseconds < regex.ElapsedMilliseconds);
            Assert.IsTrue(template.ElapsedMilliseconds < indexOf.ElapsedMilliseconds);

            runIndexOf();
            runRegex();
            runTemplate();
            Assert.IsTrue(template.ElapsedMilliseconds < regex.ElapsedMilliseconds);
            Assert.IsTrue(template.ElapsedMilliseconds < indexOf.ElapsedMilliseconds);
        }

        class DecimalObject
        {
            public decimal A { get; set; }
            public decimal? B { get; set; }
        }

        [TestMethod]
        public void Decimals()
        {
            var parser =
                FSBuilder
                    .Take<DecimalObject>(",", typeof(DecimalObject).GetProperty("A"))
                    .TakeRest(typeof(DecimalObject).GetProperty("B"))
                    .Seal();

            var obj = new DecimalObject();

            parser("123.45,8675309", obj);

            Assert.AreEqual(123.45m, obj.A);
            Assert.AreEqual(8675309m, obj.B);
        }

        [TestMethod]
        public void StandAloneTake()
        {
            var parser = FSBuilder.Take<DecimalObject>("|", typeof(DecimalObject).GetProperty("A")).Seal();

            var obj = new DecimalObject();

            parser("123|", obj);

            Assert.AreEqual(123m, obj.A);
        }

        [TestMethod]
        public void StandAloneTakeN()
        {
            var parser = FSBuilder.Take<DecimalObject>(4, typeof(DecimalObject).GetProperty("A")).Seal();

            var obj = new DecimalObject();

            parser("12345678", obj);

            Assert.AreEqual(1234m, obj.A);
        }

        class TimeSpanObject
        {
            public TimeSpan A { get; set; }
            public TimeSpan? B;
            public DateTime C;
        }

        [TestMethod]
        public void TimeSpanDateTime()
        {
            var simple = FSBuilder.Take<TimeSpanObject>("|", typeof(TimeSpanObject).GetProperty("A")).TakeRest(typeof(TimeSpanObject).GetField("B")).Seal();

            var span1 = System.TimeSpan.FromDays(1);
            var span2 = System.TimeSpan.FromMilliseconds((new Random()).Next(1000000));

            var obj = new TimeSpanObject();

            simple(span1 + "|" + span2, obj);

            Assert.AreEqual(span1, obj.A);
            Assert.AreEqual(span2, obj.B.Value);

            var complex = FSBuilder.Take<TimeSpanObject>("|", typeof(TimeSpanObject).GetProperty("A"), format: "G").TakeRest(typeof(TimeSpanObject).GetField("B"), format: "g").Seal();

            complex(span2.ToString("G") + "|" + span1.ToString("g"), obj);

            Assert.AreEqual(span2, obj.A);
            Assert.AreEqual(span1, obj.B.Value);

            var date = FSBuilder.Take<TimeSpanObject>("|", "C").Seal();

            var newDate = DateTime.UtcNow;

            date(newDate + "|", obj);
            Assert.AreEqual(newDate.ToString(), obj.C.ToString());
        }

        class EnumObject
        {
            public enum Blah { None = 0, Foo = 1, Bar = 2 };
            public Blah A { get; set; }
            public Blah? B;
        }

        [TestMethod]
        public void NumberAsEnum()
        {
            var simple = FSBuilder.Take<EnumObject>("|", "A").Else((s, o) => { throw new InvalidOperationException(); }).Seal();

            var obj = new EnumObject();

            simple("2|", obj);

            Assert.AreEqual(EnumObject.Blah.Bar, obj.A);
        }

        [TestMethod]
        public void NameAsEnum()
        {
            var simple = FSBuilder.Take<EnumObject>("|", "A").TakeRest("B").Else((s, o) => { throw new InvalidOperationException(); }).Seal();

            var obj = new EnumObject();

            simple("Foo|bar", obj);

            Assert.AreEqual(EnumObject.Blah.Foo, obj.A);
            Assert.AreEqual(EnumObject.Blah.Bar, obj.B);
        }

        class ValueObject
        {
            public sbyte A;
            public short B;
            public int C;
            public long D;
        }

        [TestMethod]
        public void Overflows()
        {
            var parse =
                FSBuilder
                    .Take<ValueObject>(",", "A")
                    .Take(",", "B")
                    .Take(",", "C")
                    .TakeRest("D")
                    .Else((s, o) => { throw new Exception(); })
                    .Seal();

            var obj = new ValueObject();

            parse("1,2,3,4", obj);
            Assert.AreEqual(1, obj.A);
            Assert.AreEqual(2, obj.B);
            Assert.AreEqual(3, obj.C);
            Assert.AreEqual(4, obj.D);

            try
            {
                parse(byte.MaxValue + ",1,2,3", obj);
                Assert.Fail("byte should be exceeded");
            }
            catch (Exception) { }

            try
            {
                parse("1,"+ushort.MaxValue + ",2,3", obj);
                Assert.Fail("short should be exceeded");
            }
            catch (Exception) { }

            try
            {
                parse("1,2," + uint.MaxValue + ",3", obj);
                Assert.Fail("int should be exceeded");
            }
            catch (Exception) { }

            try
            {
                parse("1,2,3," + ulong.MaxValue, obj);
                Assert.Fail("long should be exceeded");
            }
            catch (Exception) { }

            parse(sbyte.MaxValue + "," + short.MaxValue + "," + int.MaxValue + "," + long.MaxValue, obj);
            Assert.AreEqual(sbyte.MaxValue, obj.A);
            Assert.AreEqual(short.MaxValue, obj.B);
            Assert.AreEqual(int.MaxValue, obj.C);
            Assert.AreEqual(long.MaxValue, obj.D);
        }

        class FloatAndDouble
        {
            public float A { get; set; }
            public double? B { get; set; }
        }

        [TestMethod]
        public void FloatsDoubles()
        {
            var parse = FSBuilder.Take<FloatAndDouble>(",", "A").TakeRest("B").Seal();

            var rand = new Random();
            var a = (float)rand.NextDouble();
            var b = rand.NextDouble();

            var obj = new FloatAndDouble();

            parse(a + "," + b, obj);

            Assert.AreEqual(a.ToString(), obj.A.ToString());
            Assert.AreEqual(b.ToString(), obj.B.ToString());
        }

        class UnsignedObject
        {
            public ulong A;
            public uint B;
            public ushort C;
            public byte D;

            public Dictionary<int, int> Bad;

            public int Hidden { get { return 0; } }

            public static int Static { get; set; }

            public static int StaticField;

            public DateTime DT { get; set; }

            public TimeSpan TS { get; set; }

            public override string ToString()
            {
                return base.ToString();
            }
        }

        [TestMethod]
        public void Unsigned()
        {
            var parse =
                FSBuilder
                .Take<UnsignedObject>(",", "A")
                .Take(",", "B")
                .Take(",", "C")
                .TakeRest("D")
                .Seal();

            var obj = new UnsignedObject();

            parse(ulong.MaxValue + "," + uint.MaxValue + "," + ushort.MaxValue + "," + byte.MaxValue, obj);

            Assert.AreEqual(ulong.MaxValue, obj.A);
            Assert.AreEqual(uint.MaxValue, obj.B);
            Assert.AreEqual(ushort.MaxValue, obj.C);
            Assert.AreEqual(byte.MaxValue, obj.D);
        }

        [TestMethod]
        public void FixedSteps()
        {
            var parse =
                FSBuilder
                .Take<UnsignedObject>(4, "A")
                .Back(2)
                .Take(",", "B")
                .Skip(2)
                .TakeRest("C")
                .Seal();

            var obj = new UnsignedObject();

            parse("1234,5678", obj);
            Assert.AreEqual((ulong)1234, obj.A);
            Assert.AreEqual((uint)34, obj.B);
            Assert.AreEqual((ushort)78, obj.C);
        }

        [TestMethod]
        public void Errors()
        {
            var left = FSBuilder.Take<UnsignedObject>("|", "A").Else((s, o) => { });
            var right = FSBuilder.Take<UnsignedObject>("|", "B").Else((s, o) => { });

            try
            {
                left.Append(right);
                Assert.Fail("Shouldn't be legal to append two else directives");
            }
            catch (Exception) {  }

            left = FSBuilder.Skip<UnsignedObject>(1).TakeRest("A");
            right = FSBuilder.Skip<UnsignedObject>(1).TakeRest("B");

            try
            {
                left.Append(right);
                Assert.Fail("Shouldn't be legal to append two take rest directives");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(5, "HelloWorld");
                Assert.Fail("Property does not exist");
            }
            catch (Exception) { }

            // These *shouldn't* throw exceptions
            FSBuilder.Take<UnsignedObject>("|", "A").Seal()("345212", new UnsignedObject());
            FSBuilder.Take<UnsignedObject>(4, "A").Seal()("1", new UnsignedObject());
            FSBuilder.Take<UnsignedObject>(1, "A").Take("|", "B").Seal()("asdf", new UnsignedObject());

            try
            {
                FSBuilder.Skip<UnsignedObject>(1).Back(-4);
                Assert.Fail("Back shouldn't accept negatives");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Skip<UnsignedObject>(-4);
                Assert.Fail("Skip shouldn't accept negatives");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(-4, "A");
                Assert.Fail("Take shouldn't accept negatives");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(4, "Bad");
                Assert.Fail("Bad should not be deserializable");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(4, typeof(string).GetMember("Length")[0]);
                Assert.Fail("Length is not on UnsignedObject");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(4, typeof(UnsignedObject).GetMember("ToString")[0]);
                Assert.Fail("ToString is not a field or property");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(4, "Hidden");
                Assert.Fail("Hidden is not settable");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(4, "Static");
                Assert.Fail("Statis is not an instance property");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(4, "StaticField");
                Assert.Fail("StaticField is not an instance field");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(4, "A", format: "yyyy-mm-dd");
                Assert.Fail("A is not a DateTime or TimeSpan");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(",", "DT", format: "asdf");
                Assert.Fail("DateTime format string is invalid");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>(",", "TS", format: "asdf");
                Assert.Fail("TimeSpan format string is invalid");
            }
            catch (Exception) { }

            try
            {
                FSBuilder.Take<UnsignedObject>("", "TS");
                Assert.Fail("An empty until is invalid");
            }
            catch (Exception) { }
        }

        class StringObject
        {
            public string Raw1 { get; set; }
            public string Raw2 { get; set; }
        }

        [TestMethod]
        public void BackUntil()
        {
            var parse =
                FSBuilder
                    .Skip<StringObject>(6)
                    .Take(1, "Raw1")
                    .Back("hello")
                    .TakeRest("Raw2")
                    .Seal();

            var obj = new StringObject();

            parse("123helloworld", obj);

            Assert.AreEqual("l", obj.Raw1);
            Assert.AreEqual("world", obj.Raw2);
        }

        class IntPerfObject
        {
            public int A;
            public int B;
            public int C;
            public int D;
        }

        [TestMethod]
        public void SimpleIntPerf()
        {
            var regex = new Regex(@"(\d+),(\d+),(\d+),(\d+)", RegexOptions.Compiled);
            var parserTask =
                FSBuilder
                    .Take<IntPerfObject>(",", "A")
                    .Take(",", "B")
                    .Take(",", "C")
                    .TakeRest("D")
                    .Seal();

            Action ghettoGC = () => { GC.Collect(); Thread.Sleep(100); };

            Action<string, IntPerfObject> regexTask =
                delegate(string str, IntPerfObject obj)
                {
                    var matches = regex.Match(str);

                    obj.A = int.Parse(matches.Groups[1].Value);
                    obj.B = int.Parse(matches.Groups[2].Value);
                    obj.C = int.Parse(matches.Groups[3].Value);
                    obj.D = int.Parse(matches.Groups[4].Value);
                };

            Action<string, IntPerfObject> handTask =
                delegate(string str, IntPerfObject obj)
                {
                    int a = str.IndexOf(',');
                    obj.A = int.Parse(str.Substring(0, a));

                    int b = str.IndexOf(',', a + 1);
                    obj.B = int.Parse(str.Substring(a + 1, b - (a + 1)));

                    int c = str.IndexOf(',', b + 1);
                    obj.C = int.Parse(str.Substring(b + 1, c - (b + 1)));

                    obj.D = int.Parse(str.Substring(c + 1, str.Length - (c + 1)));
                };

            var rand = new Random();
            var data = new List<string>();
            for (int i = 0; i < 1000000; i++)
            {
                data.Add(rand.Next() + "," + rand.Next() + "," + rand.Next() + "," + rand.Next());
            }

            // Equivalence check
            foreach (var str in data)
            {
                var o1 = new IntPerfObject();
                var o2 = new IntPerfObject();
                var o3 = new IntPerfObject();

                parserTask(str, o1);
                regexTask(str, o2);
                handTask(str, o3);

                if (o1.A != o2.A || o2.A != o3.A) throw new Exception();
                if (o1.B != o2.B || o2.B != o3.B) throw new Exception();
                if (o1.C != o2.C || o2.C != o3.C) throw new Exception();
                if (o1.D != o2.D || o2.D != o3.D) throw new Exception();
            }

            var results = new Dictionary<int, List<long>>();
            results[0] = new List<long>();   // regex
            results[1] = new List<long>();   // hand
            results[2] = new List<long>();   // parser

            var timer = new Stopwatch();
            var speedObj = new IntPerfObject();

            for (int i = 0; i < 5; i++)
            {
                ghettoGC();

                timer.Restart();
                foreach (var str in data)
                {
                    regexTask(str, speedObj);
                }
                timer.Stop();

                results[0].Add(timer.ElapsedMilliseconds);
                ghettoGC();

                timer.Restart();
                foreach (var str in data)
                {
                    handTask(str, speedObj);
                }
                timer.Stop();

                results[1].Add(timer.ElapsedMilliseconds);
                ghettoGC();

                timer.Restart();
                foreach (var str in data)
                {
                    parserTask(str, speedObj);
                }
                timer.Stop();

                results[2].Add(timer.ElapsedMilliseconds);
            }

            var medianRegex = results[0].OrderBy(o => o).ElementAt(results[0].Count / 2);
            var medianHand = results[1].OrderBy(o => o).ElementAt(results[1].Count / 2);
            var medianParser = results[2].OrderBy(o => o).ElementAt(results[2].Count / 2);

            Assert.IsTrue(medianRegex > medianHand);
            Assert.IsTrue(medianHand > medianParser);
        }

        class StringPerfObject
        {
            public string A { get; set; }
            public string B { get; set; }
            public string C { get; set; }
            public string D { get; set; }
        }

        [TestMethod]
        public void SimpleStringPerf()
        {
            var regex = new Regex(@"([^,]*?),([^,]*?),([^,]*?),([^,]*)", RegexOptions.Compiled);
            var parserTask =
                FSBuilder
                    .Take<StringPerfObject>(",", "A")
                    .Take(",", "B")
                    .Take(",", "C")
                    .TakeRest("D")
                    .Seal();

            Action ghettoGC = () => { GC.Collect(); Thread.Sleep(100); };

            Action<string, StringPerfObject> regexTask =
                delegate(string str, StringPerfObject obj)
                {
                    var matches = regex.Match(str);

                    obj.A = matches.Groups[1].Value;
                    obj.B = matches.Groups[2].Value;
                    obj.C = matches.Groups[3].Value;
                    obj.D = matches.Groups[4].Value;
                };

            Action<string, StringPerfObject> handTask =
                delegate(string str, StringPerfObject obj)
                {
                    int a = str.IndexOf(',');
                    obj.A = str.Substring(0, a);

                    int b = str.IndexOf(',', a + 1);
                    obj.B = str.Substring(a + 1, b - (a + 1));

                    int c = str.IndexOf(',', b + 1);
                    obj.C = str.Substring(b + 1, c - (b + 1));

                    obj.D = str.Substring(c + 1, str.Length - (c + 1));
                };

            var rand = new Random();
            var data = new List<string>();
            for (int i = 0; i < 1000000; i++)
            {
                data.Add(Guid.NewGuid() + "," + Guid.NewGuid() + "," + Guid.NewGuid() + "," + Guid.NewGuid());
            }

            // Equivalence check
            foreach (var str in data)
            {
                var o1 = new StringPerfObject();
                var o2 = new StringPerfObject();
                var o3 = new StringPerfObject();

                parserTask(str, o1);
                regexTask(str, o2);
                handTask(str, o3);

                if (o1.A != o2.A || o2.A != o3.A) throw new Exception();
                if (o1.B != o2.B || o2.B != o3.B) throw new Exception();
                if (o1.C != o2.C || o2.C != o3.C) throw new Exception();
                if (o1.D != o2.D || o2.D != o3.D) throw new Exception();
            }

            var results = new Dictionary<int, List<long>>();
            results[0] = new List<long>();   // regex
            results[1] = new List<long>();   // hand
            results[2] = new List<long>();   // parser

            var timer = new Stopwatch();
            var speedObj = new StringPerfObject();

            for (int i = 0; i < 5; i++)
            {
                ghettoGC();

                timer.Restart();
                foreach (var str in data)
                {
                    regexTask(str, speedObj);
                }
                timer.Stop();

                results[0].Add(timer.ElapsedMilliseconds);
                ghettoGC();

                timer.Restart();
                foreach (var str in data)
                {
                    handTask(str, speedObj);
                }
                timer.Stop();

                results[1].Add(timer.ElapsedMilliseconds);
                ghettoGC();

                timer.Restart();
                foreach (var str in data)
                {
                    parserTask(str, speedObj);
                }
                timer.Stop();

                results[2].Add(timer.ElapsedMilliseconds);
            }

            var medianRegex = results[0].OrderBy(o => o).ElementAt(results[0].Count / 2);
            var medianHand = results[1].OrderBy(o => o).ElementAt(results[1].Count / 2);
            var medianParser = results[2].OrderBy(o => o).ElementAt(results[2].Count / 2);

            // before pointer attempts
            // regex: 9591
            // parser: 1499
            // hand: 410

            Assert.IsTrue(medianRegex > medianHand, "Regex faster than hand rolled; invalid test");
            Assert.IsTrue(medianHand > medianParser, "Hand faster than generated; bad parser");
        }

        class BoolObject
        {
            public bool A { get; set; }
            public bool B;
            public bool? C;
        }

        [TestMethod]
        public void Bools()
        {
            var parser =
                FSBuilder
                    .Take<BoolObject>(",", "A")
                    .Take(",", "B")
                    .TakeRest("C")
                    .Seal();

            var obj = new BoolObject();

            parser("True,1,false", obj);
            Assert.IsTrue(obj.A);
            Assert.IsTrue(obj.B);
            Assert.IsFalse(obj.C.Value);
        }

        [TestMethod]
        public void NullableSkip()
        {
            var parser =
                FSBuilder
                .Take<BoolObject>(",", "A")
                .Take(",", "C")
                .TakeRest("B")
                .Seal();

            var obj = new BoolObject();

            parser("True,,false", obj);

            Assert.IsTrue(obj.A);
            Assert.IsFalse(obj.B);
            Assert.IsNull(obj.C);
        }

        class Temp
        {
            public enum E { A = 1, B = 2 };

            public E A { get; set; }
            public E B { get; set; }
        }

        [TestMethod]
        public void TempTest()
        {
            var p =
                FSBuilder
                    .Take<Temp>("|", "A")
                    .Take<Temp>("|", "B")
                    .Seal();

            var obj = new Temp();
            p("1|B|", obj);

            Assert.AreEqual(Temp.E.A, obj.A);
            Assert.AreEqual(Temp.E.B, obj.B);
        }
    }
}