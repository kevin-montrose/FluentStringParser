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
            public Int16? SqlCount { get; set; }
            public Int16? SqlDurationMs { get; set; }
            public Int16? AspNetDurationMs { get; set; }
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
            var step1 =
                FStringParser
                .Until<LogRow>(" ");

            var step2 = 
                FStringParser
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
                FStringParser
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
                        case "SqlCount": row.SqlCount = short.Parse(match.Groups[name].Value); break;
                        case "SqlDurationMs": row.SqlDurationMs = short.Parse(match.Groups[name].Value); break;
                        case "AspNetDurationMs": row.AspNetDurationMs = short.Parse(match.Groups[name].Value); break;
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

                    Assert.AreEqual(row1.ToString(), row2.ToString(), "For: " + line);
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

            // Run both orders

            runRegex();
            runTemplate();
            Assert.IsTrue(template.ElapsedMilliseconds < regex.ElapsedMilliseconds);

            runTemplate();
            runRegex();
            Assert.IsTrue(template.ElapsedMilliseconds < regex.ElapsedMilliseconds);
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
                FStringParser
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
            var parser = FStringParser.Take<DecimalObject>("|", typeof(DecimalObject).GetProperty("A")).Seal();

            var obj = new DecimalObject();

            parser("123|", obj);

            Assert.AreEqual(123m, obj.A);
        }

        [TestMethod]
        public void StandAloneTakeN()
        {
            var parser = FStringParser.Take<DecimalObject>(4, typeof(DecimalObject).GetProperty("A")).Seal();

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
            var simple = FStringParser.Take<TimeSpanObject>("|", typeof(TimeSpanObject).GetProperty("A")).TakeRest(typeof(TimeSpanObject).GetField("B")).Seal();

            var span1 = System.TimeSpan.FromDays(1);
            var span2 = System.TimeSpan.FromMilliseconds((new Random()).Next(1000000));

            var obj = new TimeSpanObject();

            simple(span1 + "|" + span2, obj);

            Assert.AreEqual(span1, obj.A);
            Assert.AreEqual(span2, obj.B.Value);

            var complex = FStringParser.Take<TimeSpanObject>("|", typeof(TimeSpanObject).GetProperty("A"), format: "G").TakeRest(typeof(TimeSpanObject).GetField("B"), format: "g").Seal();

            complex(span2.ToString("G") + "|" + span1.ToString("g"), obj);

            Assert.AreEqual(span2, obj.A);
            Assert.AreEqual(span1, obj.B.Value);

            var date = FStringParser.Take<TimeSpanObject>("|", "C").Seal();

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
            var simple = FStringParser.Take<EnumObject>("|", "A").Else((s, o) => { throw new InvalidOperationException(); }).Seal();

            var obj = new EnumObject();

            simple("2|", obj);

            Assert.AreEqual(EnumObject.Blah.Bar, obj.A);
        }

        [TestMethod]
        public void NameAsEnum()
        {
            var simple = FStringParser.Take<EnumObject>("|", "A").TakeRest("B").Else((s, o) => { throw new InvalidOperationException(); }).Seal();

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
                FStringParser
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
            var parse = FStringParser.Take<FloatAndDouble>(",", "A").TakeRest("B").Seal();

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
                FStringParser
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
                FStringParser
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
            var left = FStringParser.Take<UnsignedObject>("|", "A").Else((s, o) => { });
            var right = FStringParser.Take<UnsignedObject>("|", "B").Else((s, o) => { });

            try
            {
                left.Append(right);
                Assert.Fail("Shouldn't be legal to append two else directives");
            }
            catch (Exception) {  }

            left = FStringParser.Skip<UnsignedObject>(1).TakeRest("A");
            right = FStringParser.Skip<UnsignedObject>(1).TakeRest("B");

            try
            {
                left.Append(right);
                Assert.Fail("Shouldn't be legal to append two take rest directives");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(5, "HelloWorld");
                Assert.Fail("Property does not exist");
            }
            catch (Exception) { }

            // These *shouldn't* throw exceptions
            FStringParser.Take<UnsignedObject>("|", "A").Seal()("345212", new UnsignedObject());
            FStringParser.Take<UnsignedObject>(4, "A").Seal()("1", new UnsignedObject());
            FStringParser.Take<UnsignedObject>(1, "A").Take("|", "B").Seal()("asdf", new UnsignedObject());

            try
            {
                FStringParser.Back<UnsignedObject>(-4);
                Assert.Fail("Back shouldn't accept negatives");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Skip<UnsignedObject>(-4);
                Assert.Fail("Skip shouldn't accept negatives");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(-4, "A");
                Assert.Fail("Take shouldn't accept negatives");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(4, "Bad");
                Assert.Fail("Bad should not be deserializable");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(4, typeof(string).GetMember("Length")[0]);
                Assert.Fail("Length is not on UnsignedObject");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(4, typeof(UnsignedObject).GetMember("ToString")[0]);
                Assert.Fail("ToString is not a field or property");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(4, "Hidden");
                Assert.Fail("Hidden is not settable");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(4, "Static");
                Assert.Fail("Statis is not an instance property");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(4, "StaticField");
                Assert.Fail("StaticField is not an instance field");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(4, "A", format: "yyyy-mm-dd");
                Assert.Fail("A is not a DateTime or TimeSpan");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(",", "DT", format: "asdf");
                Assert.Fail("DateTime format string is invalid");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>(",", "TS", format: "asdf");
                Assert.Fail("TimeSpan format string is invalid");
            }
            catch (Exception) { }

            try
            {
                FStringParser.Take<UnsignedObject>("", "TS");
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
                FStringParser
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
    }
}
