using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using FrameTools;

internal static class Dlt645CatalogTests
{
    private static int failed;
    public static int Passed { get; private set; }
    private static readonly Type Catalog = typeof(FrameCodec).Assembly.GetType("FrameTools.ProtocolCatalog", true);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Passed++;
            Console.WriteLine("PASS 645 catalog " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL 645 catalog " + name + ": " + (error.InnerException ?? error).Message);
        }
    }

    private static FrameResult Meter(string di, string data, byte control = 0x91)
    {
        return FrameCodec.Parse(ProtocolKind.Dlt645, Hex.Format(FrameCodec.Build645("000000000001", control,
            Hex.Identifier(di).Concat(Hex.Parse(data)).ToArray(), 0)));
    }

    private static string Name(byte function, string di)
    {
        return (string)Catalog.GetMethod("Name").Invoke(null, new object[] { ProtocolKind.Dlt645, function, false, di });
    }

    public static int Run()
    {
        failed = 0;
        Passed = 0;
        Run("embedded standard catalog has sources and no bundled PDF text", delegate
        {
            using (System.IO.Stream stream = typeof(FrameCodec).Assembly.GetManifestResourceStream("FrameTools.Protocols.DLT645.xml"))
            {
                Check(stream != null, "DLT645.xml is not embedded");
                XmlDocument document = new XmlDocument();
                document.Load(stream);
                Check(document.SelectNodes("/config/sourceText | /config/sourceTables").Count == 0, "Original PDF transcript bundled");
                Check(document.SelectNodes("/config/dataItem").Count > 20000, "Standard ranges were not populated");
                foreach (XmlElement item in document.SelectNodes("/config/dataItem"))
                {
                    Check(item.SelectSingleNode("sourceRef[@page]") != null && item.HasAttribute("function") && !item.HasAttribute("afn"), "Missing 645 source or wrong AFN selector");
                }
            }
        });
        Run("645 selectors use command functions and preserve protocol separation", delegate
        {
            Check(Name(0x11, "02010100") == "A相电压", "Read function did not find standard voltage");
            Check(Name(0x0A, "02010100") == null, "645 was selected by a Southern Grid AFN");
            Check(Name(0x11, "02010400") == null, "Uplink line-voltage extension leaked into 645");
            Check(Name(0x11, "04000C01") == null && Name(0x14, "04000C01") != null, "Write-only password offered for reading");
            Check(Name(0x11, "04000109") == null && Name(0x14, "04000109") != null, "Cross-page write-only switch time offered for reading");
        });
        Run("combination energy honors the standard sign bit", delegate
        {
            foreach (string di in new[] { "00000000", "00030000", "00040000" })
            {
                FrameResult result = Meter(di, "34 12 00 80");
                Check(result.Fields.Any(field => field.Meaning == "-12.34 " + (di == "00000000" ? "kWh" : "kvarh")), "Combination sign ignored for " + di);
            }
        });
        Run("energy blocks use actual tariffs rather than a fixed 256-byte length", delegate
        {
            FrameResult result = Meter("0001FF00", "78 56 34 12 01 00 00 00 02 00 00 00");
            Check(result.Fields.Any(field => field.Name == "总量.电能" && field.Meaning == "123456.78 kWh"), "Total not decoded");
            Check(result.Fields.Any(field => field.Name == "费率2.电能" && field.Meaning == "0.02 kWh" && field.Offset == 22), "Actual tariff count or offset incorrect");
            Check(!result.Fields.Any(field => field.Name == "费率3.电能"), "Unconfigured tariff fabricated");
            FrameResult shortBlock = Meter("0001FF00", "78 56 34 12 01");
            Check(!shortBlock.Fields.Any(field => field.Name.Contains(".电能")), "Incomplete block submitted fields");
        });
        Run("maximum demand is three value bytes plus five occurrence-time bytes", delegate
        {
            FrameResult result = Meter("01030000", "34 12 80 52 10 30 10 26");
            Check(result.Fields.Any(field => field.Name == "最大需量" && field.Meaning == "-0.1234 kvar" && field.Length == 3), "Demand value/sign incorrect");
            Check(result.Fields.Any(field => field.Name == "发生时间" && field.Meaning == "26-10-30 10:52" && field.Offset == 17), "Demand occurrence time incorrect");
        });
        Run("maximum-demand blocks also follow the actual tariff count", delegate
        {
            FrameResult result = Meter("0101FF00", "34 12 00 52 10 30 10 26 78 56 00 53 10 30 10 26");
            Check(result.Fields.Count(field => field.Name.EndsWith(".最大需量", StringComparison.Ordinal)) == 2, "Demand block assumes configured four tariffs");
            Check(result.Fields.Any(field => field.Name == "费率1.发生时间" && field.Offset == 25), "Demand block stride incorrect");
        });
        Run("harmonic blocks contain the 21 orders listed in the standard", delegate
        {
            FrameResult result = Meter("020A01FF", string.Join(" ", Enumerable.Repeat("00 15", 21)));
            Check(result.Fields.Any(field => field.Name == "A相电压21次谐波含量" && field.Meaning == "15.00 %"), "21st harmonic absent");
            Check(!result.Fields.Any(field => field.Name == "A相电压0次谐波含量"), "Unspecified zeroth order fabricated");
        });
        Run("ASCII parameters and the unset settlement sentinel follow the PDF", delegate
        {
            string asset = Hex.Format(Encoding.ASCII.GetBytes("ASSET-123".PadRight(32, '\0')));
            FrameResult result = Meter("04000403", asset);
            Check(result.Fields.Any(field => field.Name.Contains("资产管理") && field.Meaning == "ASSET-123"), "ASCII metadata not used");
            FrameResult unset = Meter("04000B01", "99 99");
            Check(unset.Fields.Any(field => field.Meaning == "未设置结算日"), "9999 incorrectly reported as an invalid time");
        });
        Run("frozen energy and clocks use their own standard fields", delegate
        {
            FrameResult result = Meter("05060101", "01 00 00 00 02 00 00 00");
            Check(result.Fields.Any(field => field.Name == "费率1.电能" && field.Meaning == "0.02 kWh"), "Daily freeze assumed a fixed tariff count");
            FrameResult clock = Meter("05060001", "52 10 30 10 26");
            Check(clock.Fields.Any(field => field.Meaning == "26-10-30 10:52"), "Frozen clock layout wrong");
        });
        Run("incomplete multi-frame responses are not decoded as complete values", delegate
        {
            FrameResult result = Meter("0001FF00", "01 00 00 00", 0xB1);
            Check(result.IsFrameValid && !result.Fields.Any(field => field.Name.Contains(".电能")), "First fragment was treated as a complete block");
            Check(result.Fields.Single(field => field.Name == "数据域 DATA").Meaning.Contains("后续"), "Fragment boundary unexplained");
            FrameResult continuation = Meter("0001FF00", "01 00 00 00 01", 0x92);
            Check(!continuation.Fields.Any(field => field.Name.Contains(".电能"))
                && continuation.Fields.Single(field => field.Name == "数据域 DATA").Meaning.Contains("后续数据帧"), "Continuation response mislabeled as a request");
        });
        Run("load requests cannot silently omit their required parameters", delegate
        {
            PurposeCommand command = PurposeComposer.GetCommands(ProtocolKind.Dlt645).Single(item => item.Key == "645.read");
            IDictionary<string, string> values = PurposeComposer.Defaults(command);
            values["identifiers"] = "06010000";
            try
            {
                PurposeComposer.Compose(command, values);
            }
            catch (FormatException error)
            {
                Check(error.Message.Contains("附加参数"), "Load request rejection lacks guidance");
                return;
            }
            throw new Exception("Malformed load request was generated");
        });
        Run("XML search exposes supported data beyond the old fourteen-item list", delegate
        {
            Array items = (Array)Catalog.GetMethod("Search").Invoke(null, new object[] { ProtocolKind.Dlt645, new byte[] { 0x11 }, "费率63" });
            Check(items.Length > 0 && items.Length <= 200, "645 XML search missing or unbounded");
            Check(items.Cast<object>().Any(item => (string)item.GetType().GetField("Id").GetValue(item) == "00013F00"), "Standard tariff 63 not selectable");
        });
        Run("parameter ranges retain their own numbering and decimal places", delegate
        {
            Check(Name(0x11, "0405013F") == "第一套费率电价63", "Tariff price name did not advance with DI");
            Check(Name(0x14, "04000C0A") == "9级密码", "Password level is not DI0 minus one");
            FrameResult price = Meter("0405013F", "78 56 34 12");
            Check(price.Fields.Any(field => field.Meaning == "1234.5678 元"), "Price scale must be four decimal places");
            Check(Meter("04000302", "05").Fields.Any(field => field.Meaning == "5 秒"), "Sparse unit column was misaligned");
            Check(Meter("04000409", "00 16 00").Fields.Any(field => field.Meaning == "1600 imp/kWh"), "Meter constant lost its unit");
        });
        Run("short energy families are not expanded into twelve settlement periods", delegate
        {
            Check(Name(0x11, "00900101").Contains("透支电量"), "Explicit overdraft item was overwritten by a settlement template");
            Check(Name(0x11, "00900102") == null && Name(0x11, "000B0002") == null, "Unspecified settlement periods fabricated");
            Check(Name(0x11, "009001FF") == null, "Thirteen-period block fabricated for a two-item family");
        });
        Run("feature and status bytes are interpreted as bits rather than BCD", delegate
        {
            FrameResult feature = Meter("04000603", "F0");
            Check(feature.IsFrameValid && !feature.Notes.Any(note => note.Contains("BCD")), "Bit feature was rejected as BCD");
            Check(feature.Fields.Any(field => field.Name.EndsWith(".IV象限减", StringComparison.Ordinal) && field.Meaning == "减"), "Quadrant combination bit missing");
            FrameResult fault = Meter("04000504", "01 01");
            Check(fault.Fields.Any(field => field.Name.EndsWith(".失压", StringComparison.Ordinal) && field.Meaning == "发生"), "Voltage-loss status bit wrong");
            Check(fault.Fields.Any(field => field.Name.EndsWith(".断流", StringComparison.Ordinal) && field.Meaning == "发生"), "Ninth fault bit missing");
        });
        Run("conflicting source names remain visible without automatic value decoding", delegate
        {
            foreach (string di in new[] { "04000306", "04000307", "00900200", "00900201" })
            {
                string name = Name(0x11, di) ?? Name(0x14, di);
                Check(name != null && name.Contains("原文待确认"), "Source conflict was silently renamed: " + di);
                FrameResult result = Meter(di, di.StartsWith("04", StringComparison.Ordinal) ? "01 00 00" : "01 00 00 00");
                Check(result.Fields.Count == Meter("FFFFFFFF", "01 00 00 00").Fields.Count, "Conflicting item was decoded: " + di);
            }
        });
        Console.WriteLine("645 CATALOG RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
