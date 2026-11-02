using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FrameTools;

internal static class StandardsReviewTests
{
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
            Console.WriteLine("PASS standards " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL standards " + name + ": " + (error.InnerException ?? error).Message);
        }
    }

    private static FrameResult Meter(byte control, string data)
    {
        return FrameCodec.Parse(ProtocolKind.Dlt645, Hex.Format(FrameCodec.Build645("000000000001", control, Hex.Parse(data), 0)));
    }

    private static FrameResult Uplink(byte afn, string di, string data)
    {
        byte[] body = new byte[] { 0, 0 }.Concat(Hex.Identifier(di)).Concat(Hex.Parse(data)).ToArray();
        return FrameCodec.Parse(ProtocolKind.CsgUplink,
            Hex.Format(FrameCodec.BuildUplink(0x88, Hex.Parse("00 00 00 01 00 00 10"), afn, 0x60, body)));
    }

    public static int Run()
    {
        failed = 0;
        Passed = 0;
        Run("uplink combination energy inherits the referenced 645 sign rule", delegate
        {
            foreach (string di in new[] { "00000000", "00030000", "00040000" })
            {
                FrameResult result = Uplink(0x0C, di, "34 12 00 80");
                string unit = di == "00000000" ? "kWh" : "kvarh";
                Check(result.Fields.Any(field => field.Meaning == "-12.34 " + unit), "Combination sign ignored for " + di);
            }
        });
        Run("uplink floating coefficients use a sign bit and seven binary magnitude bits", delegate
        {
            foreach (string di in new[] { "E0000C6B", "E0000C6C" })
            {
                Check(Uplink(0x0A, di, "85").Fields.Any(field => field.Meaning == "-5 %"), "Downward coefficient decoded as an unsigned number");
                Check(Uplink(0x0A, di, "05").Fields.Any(field => field.Meaning == "5 %"), "Upward coefficient incorrect");
            }
        });
        Run("switch collections are not consumed as one scalar switch", delegate
        {
            string data = string.Join(" ", Enumerable.Repeat("01", 15));
            foreach (string di in new[] { "E0000C0F", "E0000C1F", "E0000C2F", "E0000C3F" })
            {
                FrameField content = Uplink(0x0A, di, data).Fields.Single(field => field.Name == "首个数据内容");
                Check(content.Length == 15 && content.Raw == data, "Collection truncated to one byte for " + di);
                Check(content.Meaning.Contains("原始值保留"), "Unconfirmed collection layout was interpreted as a scalar");
            }
        });
        Run("645 security failures use the two-byte SERR definition", delegate
        {
            FrameResult result = Meter(0xC3, "04 01");
            FrameField error = result.Fields.SingleOrDefault(field => field.Name == "异常信息 SERR");
            Check(error != null && error.Length == 2, "Two-byte SERR missing");
            Check(error.Meaning.Contains("ESAM验证失败") && error.Meaning.Contains("Bit8"), "Security errors were decoded as ordinary ERR bits");
            Check(!result.Fields.Any(field => field.Name == "异常信息 ERR"), "Ordinary ERR fabricated for C3H");
        });
        Run("truncated SERR is retained without partial error decoding", delegate
        {
            FrameResult result = Meter(0xC3, "04");
            Check(!result.Fields.Any(field => field.Name == "异常信息 ERR" || field.Name == "异常信息 SERR"), "Truncated SERR submitted a partial error field");
            Check(result.Notes.Any(note => note.Contains("SERR") && note.Contains("2")), "SERR length mismatch unexplained");
        });
        Run("security data identifiers belong to function 03H and retain their source", delegate
        {
            Type catalog = typeof(FrameCodec).Assembly.GetType("FrameTools.ProtocolCatalog", true);
            MethodInfo name = catalog.GetMethod("Name");
            const string di = "07800101";
            Check(name.Invoke(null, new object[] { ProtocolKind.Dlt645, (byte)0x03, false, di }) != null, "Security DI was not routed to 03H");
            Check(name.Invoke(null, new object[] { ProtocolKind.Dlt645, (byte)0x11, false, di }) == null, "Security DI still exposed as an ordinary read");
            FrameResult result = Meter(0x03, "01 01 80 07 00 00 00 00 01 00 00 00 00 00 00 00");
            Check(result.Fields.Single(field => field.Name == "控制码 C").Meaning.Contains("安全认证"), "03H command mislabeled as reserved");
            Check(result.Fields.Any(field => field.Name == "数据标识 DI" && field.Meaning.Contains(di)), "Security DI missing from the 03H request");
        });
        Run("purpose reads reject identifiers requiring the security command", delegate
        {
            PurposeCommand command = PurposeComposer.GetCommands(ProtocolKind.Dlt645).Single(item => item.Key == "645.read");
            IDictionary<string, string> input = PurposeComposer.Defaults(command);
            input["identifiers"] = "07800101";
            try
            {
                PurposeComposer.Compose(command, input);
            }
            catch (FormatException error)
            {
                Check(error.Message.Contains("03H"), "Security command constraint unexplained");
                return;
            }
            throw new Exception("An invalid ordinary read was generated for a security command");
        });
        Run("automatic identification includes the supplied security and output commands", delegate
        {
            foreach (byte control in new byte[] { 0x03, 0x1C, 0x1D, 0x9C, 0x9D })
            {
                string data = control == 0x03 ? "01 01 80 07 00 00 00 00 01 00 00 00 00 00 00 00"
                    : control == 0x1C ? "02 00 00 00 00 00 00 00 1A 00 00 00 12 02 11 26"
                    : control == 0x9C ? "" : "00";
                FrameResult frame = Meter(control, data);
                StreamParseResult parsed = FrameStream.Parse(Hex.Format(frame.Bytes));
                Check(parsed.Parts.Count == 1 && parsed.Parts[0].Kind == StreamPartKind.Frame
                    && parsed.Parts[0].Protocol == ProtocolKind.Dlt645, "Command excluded from automatic identification: " + control.ToString("X2"));
            }
        });
        Run("power-loss records contain separate occurrence and ending timestamps", delegate
        {
            for (int record = 1; record <= 10; record++)
            {
                string data = Hex.Format(Hex.Identifier("031100" + record.ToString("X2")))
                    + " 00 30 08 02 11 26 59 45 09 02 11 26";
                FrameResult result = Meter(0x91, data);
                Check(result.Fields.Any(field => field.Name == "掉电发生时刻" && field.Meaning == "26-11-02 08:30:00" && field.Offset == 14), "Occurrence time not decoded");
                Check(result.Fields.Any(field => field.Name == "掉电结束时刻" && field.Meaning == "26-11-02 09:45:59" && field.Offset == 20), "Ending time not decoded");
            }
        });
        Console.WriteLine("STANDARDS RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
