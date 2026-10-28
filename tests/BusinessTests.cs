using System;
using System.Linq;
using FrameTools;

internal static class BusinessTests
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

    private static FrameField Field(FrameResult result, string name)
    {
        return result.Fields.Single(f => f.Name == name);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Passed++;
            Console.WriteLine("PASS business " + name);
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine("FAIL business " + name + ": " + e.Message);
        }
    }

    private static FrameResult Meter(string di, string data)
    {
        byte[] payload = Hex.Identifier(di).Concat(Hex.Parse(data)).ToArray();
        return FrameCodec.Parse(ProtocolKind.Dlt645, Hex.Format(FrameCodec.Build645("000000000001", 0x91, payload, 0)));
    }

    private static FrameResult Local(string di, string data, byte control = 0x80)
    {
        byte afn = Hex.Parse(di)[2];
        return FrameCodec.Parse(ProtocolKind.CsgLocal, Hex.Format(FrameCodec.BuildLocal(control, "", "", afn, 1, Hex.Identifier(di), Hex.Parse(data))));
    }

    private static FrameResult Uplink(byte afn, string di, string data, byte control = 0x88)
    {
        byte[] payload = new byte[] { 1, 1 }.Concat(Hex.Identifier(di)).Concat(Hex.Parse(data)).ToArray();
        return FrameCodec.Parse(ProtocolKind.CsgUplink, Hex.Format(FrameCodec.BuildUplink(control,
            Hex.Parse("06 01 44 01 00 00 01"), afn, 0x60, payload)));
    }

    private static void MeterValue(string di, string data, string name, string expected)
    {
        Run("645 " + name, delegate
        {
            FrameResult result = Meter(di, data);
            Check(result.IsFrameValid, string.Join(";", result.Errors));
            Check(Field(result, name).Meaning == expected, "Wrong decoded value");
            Check(Field(result, "数据标识 DI").Meaning.Contains(name), "DI name missing");
            Check(Field(result, "数据域 DATA").Meaning.Contains(expected), "DATA explanation lacks decoded value");
        });
    }

    public static int Run()
    {
        Passed = 0;
        failed = 0;
        MeterValue("00020200", "90 78 56 34", "当前反向有功费率2电能", "345678.90 kWh");
        MeterValue("02020100", "56 34 92", "A相电流", "-123.456 A");
        MeterValue("02030000", "34 12 80", "瞬时总有功功率", "-0.1234 kW");
        MeterValue("02040100", "34 12 00", "瞬时A相无功功率", "0.1234 kvar");
        MeterValue("02060000", "95 89", "总功率因数", "-0.995");
        MeterValue("02800002", "00 50", "电网频率", "50.00 Hz");
        MeterValue("02800007", "35 82", "表内温度", "-23.5 ℃");
        MeterValue("020A0103", "00 15", "A相电压3次谐波含量", "15.00 %");
        MeterValue("04000101", "03 28 10 26", "日期及星期", "26-10-28 星期三");
        MeterValue("04000102", "56 34 12", "时间", "12:34:56");
        Run("645 voltage block expands exact phase values", delegate
        {
            FrameResult result = Meter("0201FF00", "01 22 02 22 03 22");
            Check(Field(result, "A相电压").Meaning == "220.1 V" && Field(result, "C相电压").Meaning == "220.3 V", "Wrong block values");
            Check(Field(result, "C相电压").Offset == 18 && Field(result, "C相电压").Raw == "36 55", "Block raw offset changed");
        });
        Run("645 invalid BCD stays raw and is explained inline", delegate
        {
            FrameResult result = Meter("02020100", "FA 00 00");
            Check(result.IsFrameValid && !result.Fields.Any(f => f.Name == "A相电流"), "Invalid value fabricated");
            Check(Field(result, "数据域 DATA").Meaning.Contains("无效 BCD"), "Invalid BCD not explained in DATA");
        });
        Run("645 error bits have standard meanings", delegate
        {
            FrameResult result = FrameCodec.Parse(ProtocolKind.Dlt645,
                Hex.Format(FrameCodec.Build645("000000000001", 0xD1, new byte[] { 6 }, 0)));
            Check(Field(result, "异常信息 ERR").Meaning.Contains("无请求数据")
                && Field(result, "异常信息 ERR").Meaning.Contains("密码错/未授权"), "ERR meanings missing");
            Check(Field(result, "控制码 C").Meaning.Contains("无后续数据帧"), "Control flag meaning missing");
        });
        Run("local DI and control include names and direction", delegate
        {
            FrameResult result = Local("E8000301", "", 0x40);
            Check(Field(result, "数据标识 DI").Meaning.Contains("查询厂商代码和版本信息")
                && Field(result, "数据标识 DI").Meaning.Contains("集中器"), "Local DI meaning missing");
            Check(Field(result, "控制域 C").Meaning.Contains("下行") && Field(result, "控制域 C").Meaning.Contains("启动站"), "Local control meaning missing");
            Check(Field(result, "数据标识内容").Meaning.Contains("无数据内容"), "Read request lacks explanation");
        });
        Run("local acknowledgement decodes BIN waiting seconds", delegate
        {
            FrameResult result = Local("E8010001", "05 00");
            Check(Field(result, "等待时间").Meaning == "5 秒", "Wrong wait time");
            Check(Field(result, "数据标识内容").Meaning.Contains("5 秒"), "Inline acknowledgement meaning missing");
        });
        Run("local negative acknowledgement decodes error reason", delegate
        {
            FrameResult result = Local("E8010002", "04");
            Check(Field(result, "错误状态字").Meaning.Contains("数据标识编码不存在"), "Wrong error reason");
        });
        Run("local node count is little-endian BIN", delegate
        {
            FrameResult result = Local("E8000305", "34 12");
            Check(Field(result, "从节点总数量").Meaning == "4660 个", "BCD incorrectly used for local BIN");
        });
        Run("local vendor version uses ASCII and BCD fields", delegate
        {
            FrameResult result = Local("E8000301", "41 42 43 44 28 10 26 34 12");
            Check(Field(result, "厂商代码").Meaning == "AB" && Field(result, "芯片代码").Meaning == "CD", "ASCII decoding wrong");
            Check(Field(result, "版本日期").Meaning == "26-10-28" && Field(result, "版本号").Meaning == "1234", "Version BCD decoding wrong");
        });
        Run("local collector AFN and DI are distinct from concentrator", delegate
        {
            FrameResult result = Local("EA002301", "41 42 43 44 28 10 26 34 12");
            Check(Field(result, "应用功能 AFN").Meaning.Contains("读参数"), "Collector AFN missing");
            Check(Field(result, "数据标识 DI").Meaning.Contains("采集器") && Field(result, "厂商代码").Meaning == "AB", "Collector DI decoding wrong");
        });
        Run("local timeout list and clock data are decoded", delegate
        {
            FrameResult result = Local("E800030B", "01 00 02 00 03 00 FF FF");
            Check(Field(result, "优先级3建议超时").Meaning == "65535 秒", "Timeout array incorrect");
            FrameResult clock = Local("E80204A3", "30 59 23 28 10 26", 0x40);
            Check(Field(clock, "CCO时间").Meaning == "26-10-28 23:59:30", "Clock field incorrect");
        });
        Run("local status and task data have semantic explanations", delegate
        {
            FrameResult state = Local("E8000307", "01");
            Check(Field(state, "主动注册状态").Meaning.Contains("正在主动注册"), "Registration state missing");
            FrameResult task = Local("E8050505", "34 12 01 02 03 04 05 06 01");
            Check(Field(task, "任务ID").Meaning == "4660" && Field(task, "任务状态").Meaning.Contains("从节点无响应"), "Task status decoding wrong");
        });
        Run("local invalid business length does not change frame validity", delegate
        {
            FrameResult result = Local("E8000305", "12");
            Check(result.IsFrameValid && !result.Fields.Any(f => f.Name == "从节点总数量"), "Short payload fabricated a value");
            Check(Field(result, "数据标识内容").Meaning.Contains("长度"), "Length mismatch not explained");
        });
        Run("local module capability response decodes its exact layout", delegate
        {
            FrameResult result = Local("E8000302", "05 00 02 00 01 02 01 02 03 04 05 06 00 04 0A 00 20 00 01 08 17 41 42 43 44 28 10 26 34 12");
            Check(Field(result, "本地通信方式").Meaning == "宽带+微功率无线", "Module communication mode wrong");
            Check(Field(result, "最大协议报文长度").Meaning == "512 字节" && Field(result, "当前从节点数量").Meaning == "10 个", "Module capability offsets wrong");
            Check(Field(result, "接口协议发布日期").Meaning == "17-08-01" && Field(result, "版本号").Meaning == "1234", "Module version fields wrong");
        });
        Run("local node list respects declared count and source offsets", delegate
        {
            FrameResult result = Local("E8040306", "02 00 02 01 00 00 00 00 00 02 00 00 00 00 00");
            Check(Field(result, "本次应答数量").Meaning == "2 个" && Field(result, "从节点2地址").Offset == 19, "Node list boundary wrong");
            Check(Field(result, "从节点2地址").Raw == "02 00 00 00 00 00", "Node list raw value wrong");
        });
        Run("local mismatched AFN prevents decoding data as a different command", delegate
        {
            FrameResult result = FrameCodec.Parse(ProtocolKind.CsgLocal, Hex.Format(FrameCodec.BuildLocal(0x80, "", "", 4, 1,
                Hex.Identifier("E8000305"), Hex.Parse("34 12"))));
            Check(result.IsFrameValid && Field(result, "数据标识内容").Meaning.Contains("不一致")
                && !result.Fields.Any(f => f.Name == "从节点总数量"), "AFN mismatch silently decoded");
        });
        Run("uplink control distinguishes ACD from FCB", delegate
        {
            FrameResult response = Uplink(0x0C, "00010000", "78 56 34 12", 0xAB);
            string c = Field(response, "控制域 C").Meaning;
            Check(c.Contains("告警数据等待访问") && c.Contains("链路状态") && !c.Contains("FCB="), "Response control interpreted as request");
            FrameResult request = Uplink(0x0C, "00010000", "", 0x7B);
            Check(Field(request, "控制域 C").Meaning.Contains("FCB=1") && Field(request, "控制域 C").Meaning.Contains("请求2级数据"), "Request control wrong");
        });
        Run("uplink current energy shares DI but never subtracts 33H", delegate
        {
            FrameResult result = Uplink(0x0C, "00010000", "78 56 34 12");
            Check(Field(result, "当前正向有功总电能").Meaning == "123456.78 kWh", "Wrong uplink BCD or 33H handling");
            Check(Field(result, "当前正向有功总电能").Offset == 22 && Field(result, "当前正向有功总电能").Raw == "78 56 34 12", "Uplink source offset wrong");
            Check(Field(result, "首个数据标识 DI").Meaning.Contains("正向有功总电能"), "Uplink DI meaning missing");
        });
        Run("uplink read parameter interprets BCD heartbeat period", delegate
        {
            FrameResult result = Uplink(0x0A, "E0000107", "15");
            Check(Field(result, "心跳周期").Meaning == "15 分钟", "BIN incorrectly used instead of BCD");
        });
        Run("uplink extended voltage uses the supplied appendix C names", delegate
        {
            FrameResult result = Uplink(0x0C, "02010400", "01 38");
            Check(Field(result, "AB线电压").Meaning == "380.1 V", "Uplink extension mapped to the wrong phase");
            FrameResult meter = Meter("02010400", "01 38");
            Check(!meter.Fields.Any(f => f.Name == "AB线电压"), "Uplink extension silently applied to 645");
        });
        Run("uplink login and overall acknowledgement are decoded", delegate
        {
            FrameResult login = Uplink(0x02, "E0001000", "24 20 01 30 59 23 28 10 26", 0xC9);
            Check(Field(login, "首个数据标识 DI").Meaning.Contains("终端登录"), "Login DI missing");
            Check(Field(login, "终端时间").Meaning == "26-10-28 23:59:30", "Login clock wrong");
            Check(Field(Uplink(0x00, "E0000000", "01"), "确认结果").Meaning.Contains("全部否定"), "Acknowledgement status wrong");
        });
        Run("uplink ASCII and communication options have bounded payloads", delegate
        {
            FrameResult apn = Uplink(0x0A, "E0000104", "43 4D 4E 45 54 " + string.Join(" ", Enumerable.Repeat("00", 11)) + " AA BB");
            Check(Field(apn, "APN").Meaning == "CMNET" && Field(apn, "首个数据内容").Length == 16, "APN decoding consumed remainder");
            Check(Field(Uplink(0x0A, "E000010A", "01"), "TCP/UDP通信方式").Meaning == "UDP", "TCP/UDP option wrong");
        });
        Run("uplink acknowledgement keeps the original parameter DI name", delegate
        {
            FrameResult ack = Uplink(0x00, "E0000107", "00");
            Check(Field(ack, "首个数据标识 DI").Meaning.Contains("心跳周期")
                && Field(ack, "确认结果").Meaning == "肯定" && !ack.Fields.Any(f => f.Name == "心跳周期"), "Acknowledgement value confused with parameter");
        });
        Run("uplink requests never decode the next DI as a data value", delegate
        {
            FrameResult request = Uplink(0x0C, "00010000", "01 01 00 00 02 00", 0x4B);
            Check(!request.Fields.Any(f => f.Name == "当前正向有功总电能"), "Request treated as response");
        });
        Run("uplink unverified remainder is not guessed as further information bodies", delegate
        {
            FrameResult result = Uplink(0x0C, "00010000", "78 56 34 12 " + string.Join(" ", Enumerable.Repeat("AA", 16)));
            Check(Field(result, "当前正向有功总电能").Meaning == "123456.78 kWh", "Known first data lost");
            Check(result.Fields.Count(f => f.Name == "首个数据标识 DI") == 1 && Field(result, "应用业务区").Length == 26, "Remainder boundaries guessed");
        });
        Run("unknown and missing values remain explicit", delegate
        {
            FrameResult unknown = Meter("FE123456", "01 02 03 04");
            Check(Field(unknown, "数据标识 DI").Meaning.Contains("未收录"), "Unknown DI silently named");
            FrameResult missing = Meter("00010000", "FF FF FF FF");
            Check(missing.IsFrameValid && !missing.Fields.Any(f => f.Name == "当前正向有功总电能"), "Missing value interpreted as a number");
        });
        Run("short business payloads never expose fields outside their frame", delegate
        {
            foreach (string di in new[] { "E8000301", "E8000302", "E8040306", "E8050505" })
            {
                for (int size = 0; size < 31; size++)
                {
                    FrameResult result = Local(di, Hex.Format(new byte[size]));
                    Check(result.IsFrameValid && result.Fields.All(f => f.Offset >= 0 && f.Offset + f.Length <= result.Bytes.Length), "Local business field overflow");
                }
            }
            for (int size = 0; size < 4; size++)
            {
                FrameResult result = Uplink(0x0C, "00010000", Hex.Format(new byte[size]));
                Check(result.IsFrameValid && !result.Fields.Any(f => f.Name == "当前正向有功总电能")
                    && Field(result, "首个数据内容").Meaning.Contains("长度"), "Short uplink data fabricated a value");
            }
        });
        Console.WriteLine("BUSINESS RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
