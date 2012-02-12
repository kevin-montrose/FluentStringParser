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
            FTemplate =
                FStringParser
                .Until<LogRow>(" ")
                .Take(":", LogProp("ClientIp"))
                .Until("[")
                .Take("]", LogProp("CreationDate"), format: "dd/MMM/yyyy:HH:mm:ss.fff")
                .Until(" ")
                .Take(" ", LogProp("FrontEnd"))
                .Take("/", LogProp("BackEnd"))
                .Take(" ", LogProp("Server"))
                .Take("/", LogProp("Tq"))
                .Take("/", LogProp("Tw"))
                .Take("/", LogProp("Tc"))
                .Take("/", LogProp("Tr"))
                .Take(" ", LogProp("Tt"))
                .Take(" ", LogProp("ResponseCode"))
                .Take(" ", LogProp("Bytes"))
                .Until("- ")
                .Until("- ")
                .Take(4, LogProp("TermState"))
                .Until(" ")
                .Take("/", LogProp("ActConn"))
                .Take("/", LogProp("FeConn"))
                .Take("/", LogProp("BeConn"))
                .Take("/", LogProp("SrvConn"))
                .Take(" ", LogProp("Retries"))
                .Take("/", LogProp("SrvQueue"))
                .Take(" ", LogProp("BackEndQueue"))
                .Until("{")
                .Take("|", LogProp("Referer"))
                .Take("|", LogProp("UserAgent"))
                .Take("|", LogProp("Host"))
                .Take("|", LogProp("ForwardFor"))
                .Take("}", LogProp("AcceptEncoding"))
                .Until("{")
                .Take("|", LogProp("ContentEncoding"))
                .Take("|", LogProp("IsPageView"))
                .Take("|", LogProp("SqlDurationMs"))
                .Take("|", LogProp("AccountId"))
                .Take("}", LogProp("RouteName"))
                .Until("\"")
                .Take(" ", LogProp("Method"))
                .Take(" ", LogProp("Uri"))
                .Take("\"", LogProp("HttpVersion"))
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
        }

        [TestMethod]
        public void TimeSpan()
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
        }
    }
}
