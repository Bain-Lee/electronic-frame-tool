using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using FrameTools;

internal static class ProtocolCatalogTests
{
    private sealed class Outcome
    {
        public bool Handled;
        public FrameResult Result;
        public string Summary;
        public int Consumed;
    }

    private static readonly Type CatalogType = typeof(FrameCodec).Assembly.GetType("FrameTools.ProtocolCatalog", true);
    private static int failed;
    public static int Passed { get; private set; }

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
            Console.WriteLine("PASS catalog " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL catalog " + name + ": " + (error.InnerException ?? error).Message);
        }
    }

    private static object Invoke(string method, params object[] arguments)
    {
        return CatalogType.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, arguments);
    }

    private static string Name(ProtocolKind protocol, byte afn, bool up, string di)
    {
        return (string)Invoke("Name", protocol, afn, up, di);
    }

    private static Outcome Decode(ProtocolKind protocol, byte afn, bool up, string di, byte[] data, bool prefix = false)
    {
        byte[] bytes = new byte[data.Length + 6];
        Array.Copy(data, 0, bytes, 3, data.Length);
        FrameResult result = new FrameResult(bytes);
        object[] arguments = { result, 3, protocol, afn, up, di, data, prefix, null, 0 };
        bool handled = (bool)Invoke("TryDecode", arguments);
        return new Outcome { Handled = handled, Result = result, Summary = (string)arguments[8], Consumed = (int)arguments[9] };
    }

    private static Outcome Local(byte afn, bool up, string di, string data)
    {
        return Decode(ProtocolKind.CsgLocal, afn, up, di, Hex.Parse(data));
    }

    private static FrameField Field(Outcome outcome, string name)
    {
        return outcome.Result.Fields.Single(field => field.Name == name);
    }

    private static XmlDocument Resource(string filename)
    {
        XmlDocument document = new XmlDocument();
        document.XmlResolver = null;
        using (System.IO.Stream stream = typeof(FrameCodec).Assembly.GetManifestResourceStream("FrameTools.Protocols." + filename))
        {
            Check(stream != null, "Catalog resource missing");
            document.Load(stream);
        }
        return document;
    }

    public static int Run()
    {
        Passed = 0;
        failed = 0;
        Run("embedded catalogs have complete standard identifier sets", delegate
        {
            XmlDocument local = Resource("CSG16.xml");
            XmlDocument uplink = Resource("CSG13.xml");
            string[] localIds = local.SelectNodes("/config/dataItem").Cast<XmlElement>().Select(item => item.GetAttribute("id")).Distinct().ToArray();
            Check(localIds.Length == 108, "Local functional-table identifiers missing");
            Check(!localIds.Contains("E80505F1"), "Unverified minute-collection extension entered standard catalog");
            foreach (XmlElement item in uplink.SelectNodes("/config/dataItem"))
            {
                uint id;
                Check(item.GetAttribute("id").Length == 8 && uint.TryParse(item.GetAttribute("id"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id), "Malformed uplink DI");
            }
        });
        Run("embedded catalogs retain source references without bundled original standards", delegate
        {
            foreach (string filename in new[] { "CSG13.xml", "CSG16.xml" })
            {
                XmlDocument catalog = Resource(filename);
                Check(catalog.SelectNodes("/config/sourceTables | /config/sourceText").Count == 0, "Original standard text bundled in " + filename);
                Check(catalog.SelectSingleNode("/config/source") != null, "Standard title missing");
                foreach (XmlElement item in catalog.SelectNodes("/config/dataItem"))
                {
                    Check(item.GetAttribute("source").Length > 0 && item.SelectSingleNode("sourceRef") != null, "Data item lost its source reference");
                }
            }
        });
        Run("same DI is selected by AFN instead of first XML match", delegate
        {
            string authentication = Name(ProtocolKind.CsgUplink, 0x06, false, "E001018F");
            string secureTransfer = Name(ProtocolKind.CsgUplink, 0x16, false, "E001018F");
            Check(authentication != null && authentication.Contains("白名单"), "Authentication context lost");
            Check(secureTransfer != null && secureTransfer.Contains("数据安全传输") && secureTransfer != authentication, "AFN context ignored");
            Check(Name(ProtocolKind.CsgUplink, 0x0C, false, "E001018F") == null, "Wrong AFN fell through to an unrelated definition");
        });
        Run("request and response sharing a DI retain different lengths", delegate
        {
            Outcome request = Local(0x02, false, "E8000206", "");
            Outcome response = Local(0x02, true, "E8000206", "34 12");
            Check(request.Handled && request.Consumed == 0 && request.Result.Fields.Count == 0, "Zero-length request misread");
            Check(response.Handled && response.Consumed == 2 && Field(response, "剩余可分配任务数").Meaning.StartsWith("4660", StringComparison.Ordinal), "Response reused zero-length request");
        });
        Run("wrong AFN and protocol never reuse an item", delegate
        {
            Outcome wrongAfn = Local(0x03, true, "E8000206", "34 12");
            Outcome wrongProtocol = Decode(ProtocolKind.CsgUplink, 0x02, true, "E8000206", Hex.Parse("34 12"));
            Check(!wrongAfn.Handled && !wrongProtocol.Handled, "Definition leaked across selector boundary");
        });
        Run("first scalar consumes its own payload and preserves remainder", delegate
        {
            Outcome value = Decode(ProtocolKind.CsgUplink, 0x0A, true, "E0000107", Hex.Parse("15 AA BB"), true);
            Check(value.Handled && value.Consumed == 1 && value.Result.Fields.Count == 1, "Scalar consumed unverified remainder");
            Check(Field(value, "心跳周期").Meaning == "15 分钟" && Field(value, "心跳周期").Offset == 3, "BCD scale or source offset wrong");
        });
        Run("exact payload rejects trailing bytes atomically", delegate
        {
            Outcome value = Decode(ProtocolKind.CsgUplink, 0x0A, true, "E0000107", Hex.Parse("15 AA"));
            Check(value.Handled && value.Consumed == 0 && value.Result.Fields.Count == 0 && value.Summary.Contains("长度不符"), "Unexpected trailing byte accepted");
            Check(value.Result.IsFrameValid, "Business mismatch changed frame validity");
        });
        Run("invalid BCD produces a note without a fabricated value", delegate
        {
            Outcome value = Decode(ProtocolKind.CsgUplink, 0x0A, true, "E0000107", Hex.Parse("FA"));
            Check(value.Handled && value.Consumed == 0 && value.Result.Fields.Count == 0 && value.Summary.Contains("无效 BCD"), "Invalid BCD became a number");
            Check(value.Result.Notes.Count == 1 && value.Result.IsFrameValid, "Business diagnostic not retained separately");
        });
        Run("BCD all FF is missing while BIN all FF remains a count", delegate
        {
            Outcome missing = Decode(ProtocolKind.CsgUplink, 0x0A, true, "E0000107", Hex.Parse("FF"));
            Outcome count = Local(0x02, true, "E8000206", "FF FF");
            Check(missing.Handled && missing.Result.Fields.Count == 0 && missing.Summary.Contains("全 FF"), "Missing BCD converted");
            Check(count.Handled && Field(count, "剩余可分配任务数").Meaning.StartsWith("65535", StringComparison.Ordinal), "Valid BIN FFFF treated as missing");
        });
        Run("task D4 zero uses one-byte content length", delegate
        {
            Outcome task = Local(0x02, false, "E8020201", "34 12 00 0A 00 03 AA BB CC");
            Check(task.Handled && task.Consumed == 9, "Normal task layout rejected");
            Check(Field(task, "报文长度").Length == 1 && Field(task, "报文内容").Offset == 9, "Normal task length offset wrong");
            Check(Field(task, "报文内容").Raw == "AA BB CC", "Raw payload changed");
        });
        Run("task D4 one selects two-byte content length and bit meaning", delegate
        {
            Outcome task = Local(0x02, false, "E8020201", "34 12 10 0A 00 03 00 AA BB CC");
            Check(task.Handled && task.Consumed == 10, "Combined task layout rejected");
            Check(Field(task, "报文长度").Length == 2 && Field(task, "报文内容").Offset == 10, "D4 branch failed to change field size");
            Check(Field(task, "任务模式字.组合多帧标志").Meaning == "组合多帧", "D4 bit semantics lost");
        });
        Run("task truncation never leaves partially decoded fields", delegate
        {
            Outcome task = Local(0x02, false, "E8020201", "34 12 10 0A 00 03 00 AA BB");
            Check(task.Handled && task.Consumed == 0 && task.Result.Fields.Count == 0 && task.Summary.Contains("不足"), "Partial task fields escaped");
        });
        Run("multicast FFFF count omits addresses as the standard requires", delegate
        {
            Outcome task = Local(0x02, false, "E8020207", "34 12 00 FF FF 0A 00 03 AA BB CC");
            Check(task.Handled && task.Consumed == 11, "FFFF count consumed an address list");
            Check(!task.Result.Fields.Any(field => field.Name.StartsWith("目的节点", StringComparison.Ordinal)), "Omitted addresses were fabricated");
            Check(Field(task, "报文内容").Offset == 11 && Field(task, "报文内容").Raw == "AA BB CC", "Payload boundary moved after omitted addresses");
        });
        Run("blacklist repetition honors count and raw source offsets", delegate
        {
            Outcome nodes = Local(0x04, false, "E80204A1", "02 01 02 03 04 05 06 11 12 13 14 15 16");
            Check(nodes.Handled && nodes.Consumed == 13 && nodes.Result.Fields.Count == 3, "Repeated addresses not decoded");
            Check(Field(nodes, "节点1.地址").Offset == 4 && Field(nodes, "节点2.地址").Offset == 10, "Repeated item offset wrong");
            Check(Field(nodes, "节点2.地址").Raw == "11 12 13 14 15 16", "Address wire bytes lost");
        });
        Run("zero-count list consumes only its explicit count", delegate
        {
            Outcome nodes = Local(0x04, false, "E80204A1", "00");
            Check(nodes.Handled && nodes.Consumed == 1 && nodes.Result.Fields.Count == 1, "Zero count invented an item");
        });
        Run("truncated counted list rejects the whole decoded structure", delegate
        {
            Outcome nodes = Local(0x04, false, "E80204A1", "02 01 02 03 04 05 06");
            Check(nodes.Handled && nodes.Result.Fields.Count == 0 && nodes.Summary.Contains("不足"), "Counted list silently shortened");
        });
        Run("APP control uses 32-byte names and current command values", delegate
        {
            byte[] payload = new byte[] { 1 }.Concat(Encoding.ASCII.GetBytes("TESTAPP".PadRight(32, '\0'))).Concat(new byte[] { 0 }).ToArray();
            Outcome app = Decode(ProtocolKind.CsgUplink, 0x04, false, "E0001400", payload);
            Check(app.Handled && app.Consumed == 34, "APP entry retained old 25-byte record");
            Check(Field(app, "APP1.APP名称").Length == 32 && Field(app, "APP1.APP名称").Meaning == "TESTAPP", "APP name width wrong");
            Check(Field(app, "APP1.APP控制状态").Meaning == "启动", "Current zero command misread as old download command");
        });
        Run("old 24-byte APP name layout is rejected", delegate
        {
            byte[] payload = new byte[] { 1 }.Concat(new byte[24]).Concat(new byte[] { 0 }).ToArray();
            Outcome app = Decode(ProtocolKind.CsgUplink, 0x04, false, "E0001400", payload);
            Check(app.Handled && app.Consumed == 0 && app.Result.Fields.Count == 0 && app.Summary.Contains("不足"), "Old APP record accepted");
        });
        Run("2025 security structure is 106 bytes and preserves opaque fields", delegate
        {
            Outcome security = Decode(ProtocolKind.CsgUplink, 0x06, true, "E00101A1", new byte[106]);
            Check(security.Handled && security.Consumed == 106, "Concatenated source length retained");
            FrameField last = Field(security, "APP验签证书4序列号");
            Check(last.Offset == 101 && last.Length == 8 && last.Raw == "00 00 00 00 00 00 00 00", "Security field boundaries wrong");
        });
        Run("source ambiguities and raw-only definitions are not interpreted", delegate
        {
            Outcome local = Local(0x03, true, "E80003B0", "00 00");
            Outcome time = Decode(ProtocolKind.CsgUplink, 0x0A, true, "E0000134", new byte[22]);
            Outcome raw = Decode(ProtocolKind.CsgUplink, 0x06, false, "E001018F", new byte[4]);
            Check(!local.Handled && !time.Handled && !raw.Handled, "Unresolved source acquired fabricated semantics");
            Check(local.Result.Fields.Count == 0 && time.Result.Fields.Count == 0 && raw.Result.Fields.Count == 0, "Unresolved source exposed fields");
        });
        Run("unresolved source is visible in the dictionary name", delegate
        {
            Check(Name(ProtocolKind.CsgLocal, 0x03, true, "E80003B0").Contains("原文待确认"), "Local ambiguity hidden");
            Check(Name(ProtocolKind.CsgUplink, 0x0A, true, "E0000134").Contains("原文待确认"), "Uplink ambiguity hidden");
        });
        Console.WriteLine("CATALOG RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
