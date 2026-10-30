using System;
using System.Linq;
using System.Text;
using FrameTools;

internal static class CatalogIntegrationTests
{
    private static int failed;
    public static int Passed { get; private set; }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Passed++;
            Console.WriteLine("PASS catalog integration " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL catalog integration " + name + ": " + error.Message);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static int Run()
    {
        Passed = 0;
        failed = 0;
        Run("local blacklist write uses XML counted layout", delegate
        {
            FrameResult result = FrameCodec.Parse(ProtocolKind.CsgLocal,
                Hex.Format(FrameCodec.BuildLocal(0x40, "", "", 4, 1, Hex.Identifier("E80204A1"), Hex.Parse("01 01 02 03 04 05 06"))));
            Check(result.IsFrameValid, "Frame structure changed");
            Check(result.Fields.Any(field => field.Name == "黑名单节点数量" && field.Meaning.Contains("1")), "XML count missing");
            Check(result.Fields.Any(field => field.Name == "节点1.地址" && field.Offset == 11 && field.Length == 6), "XML address boundary wrong");
        });
        Run("local combined task uses two-byte length from D4", delegate
        {
            FrameResult result = FrameCodec.Parse(ProtocolKind.CsgLocal,
                Hex.Format(FrameCodec.BuildLocal(0x40, "", "", 2, 1, Hex.Identifier("E8020201"), Hex.Parse("34 12 90 1E 00 03 00 68 01 16"))));
            Check(result.IsFrameValid, "Frame structure changed");
            Check(result.Fields.Any(field => field.Name == "报文长度" && field.Offset == 15 && field.Length == 2), "D4 length not selected");
            Check(result.Fields.Any(field => field.Name == "报文内容" && field.Offset == 17 && field.Raw == "68 01 16"), "Task payload shifted");
        });
        Run("uplink APP control uses corrected XML fields and leaves remainder", delegate
        {
            byte[] name = Encoding.ASCII.GetBytes("demo").Concat(new byte[28]).ToArray();
            byte[] data = new byte[] { 1 }.Concat(name).Concat(new byte[] { 2, 0xAA, 0xBB }).ToArray();
            byte[] body = new byte[] { 0, 0 }.Concat(Hex.Identifier("E0001400")).Concat(data).ToArray();
            FrameResult result = FrameCodec.Parse(ProtocolKind.CsgUplink,
                Hex.Format(FrameCodec.BuildUplink(0x88, Hex.Parse("06 01 44 01 00 00 01"), 0x0A, 0x60, body)));
            Check(result.Fields.Any(field => field.Name == "APP1.APP名称" && field.Length == 32 && field.Meaning == "demo"), "APP width not corrected");
            Check(result.Fields.Any(field => field.Name == "APP1.APP控制状态" && field.Meaning.Contains("卸载")), "APP enum not corrected");
            Check(result.Fields.Single(field => field.Name == "首个数据内容").Length == 34, "XML consumed unverified remainder");
        });
        Run("encrypted write still does not expose a guessed DI", delegate
        {
            byte[] body = new byte[] { 0, 0 }.Concat(Hex.Identifier("E0001400")).Concat(new byte[34]).ToArray();
            FrameResult result = FrameCodec.Parse(ProtocolKind.CsgUplink,
                Hex.Format(FrameCodec.BuildUplink(0x4A, Hex.Parse("06 01 44 01 00 00 01"), 4, 0x60, body)));
            Check(!result.Fields.Any(field => field.Name == "首个数据标识 DI" || field.Name.StartsWith("APP1.")), "Encrypted frame decoded as plaintext");
        });
        return failed;
    }
}
