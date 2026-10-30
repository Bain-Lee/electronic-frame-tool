using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FrameTools;

internal static class PurposeComposerTests
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
            Console.WriteLine("PASS purpose " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL purpose " + name + ": " + (error.InnerException ?? error).Message);
        }
    }

    private static PurposeCommand Command(string key)
    {
        return Enum.GetValues(typeof(ProtocolKind)).Cast<ProtocolKind>()
            .SelectMany(PurposeComposer.GetCommands).Single(command => command.Key == key);
    }

    private static IDictionary<string, string> Values(string key, params string[] changes)
    {
        IDictionary<string, string> values = PurposeComposer.Defaults(Command(key));
        for (int index = 0; index < changes.Length; index += 2)
        {
            values[changes[index]] = changes[index + 1];
        }
        return values;
    }

    private static PurposeComposition Compose(string key, params string[] changes)
    {
        return PurposeComposer.Compose(Command(key), Values(key, changes));
    }

    private static string Data(PurposeComposition result)
    {
        if (!result.IsFrame)
        {
            return Hex.Format(result.Bytes);
        }
        if (result.Protocol == ProtocolKind.Dlt645)
        {
            return Hex.Format(result.Bytes.Skip(10).Take(result.Bytes[9]).Select(value => unchecked((byte)(value - 0x33))).ToArray());
        }
        int start = result.Protocol == ProtocolKind.CsgUplink ? 16 : (result.Bytes[3] & 0x20) != 0 ? 22 : 10;
        return Hex.Format(result.Bytes.Skip(start).Take(result.Bytes.Length - start - 2).ToArray());
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (FormatException)
        {
            return;
        }
        throw new Exception("Invalid protocol input accepted");
    }

    public static int Run()
    {
        Passed = 0;
        failed = 0;
        Run("every listed purpose generates its advertised output type", delegate
        {
            foreach (ProtocolKind protocol in Enum.GetValues(typeof(ProtocolKind)))
            {
                PurposeCommand[] commands = PurposeComposer.GetCommands(protocol);
                Check(commands.Length > 0, "Protocol omitted");
                foreach (PurposeCommand command in commands)
                {
                    IDictionary<string, string> input = PurposeComposer.Defaults(command);
                    input["packets"] = "68 01 16";
                    input["password"] = "01 02 03";
                    input["operator"] = "04 05 06 07";
                    input["checksum"] = "AA BB";
                    input["fileData"] = "01 02";
                    PurposeComposition result = PurposeComposer.Compose(command, input);
                    Check(result.IsFrame != command.ParameterOnly, command.Key + " mislabels plaintext");
                    if (result.IsFrame)
                    {
                        Check(FrameCodec.Parse(protocol, Hex.Format(result.Bytes)).IsFrameValid, command.Key + " has invalid frame structure");
                    }
                }
            }
        });
        Run("645 read matches the independent complete frame", delegate
        {
            Check(Hex.Format(Compose("645.read", "identifiers", "00010000").Bytes)
                == "68 01 00 00 00 00 00 68 11 04 33 33 34 33 B3 16", "645 read wire bytes");
            Reject(delegate { Compose("645.read", "identifiers", "00010000,02010100"); });
        });
        Run("645 address operations enforce point-to-point address and reversed new address", delegate
        {
            PurposeComposition read = Compose("645.address.read", "address", "000000000001");
            Check(Hex.Format(read.Bytes.Skip(1).Take(6).ToArray()) == "AA AA AA AA AA AA" && read.Bytes[8] == 0x13 && read.Bytes[9] == 0, "Read address request");
            PurposeComposition write = Compose("645.address.write", "newAddress", "123456789012");
            Check(write.Bytes[8] == 0x15 && Data(write) == "12 90 78 56 34 12", "Write address order");
            Reject(delegate { Compose("645.address.write", "newAddress", "1234567890AA"); });
        });
        Run("645 date and time include authority password operator and weekday", delegate
        {
            string[] fields = { "time", "2026-10-30 10:52:34", "password", "01 02 03", "operator", "04 05 06 07" };
            Check(Data(Compose("645.date", fields)) == "01 01 00 04 04 01 02 03 04 05 06 07 05 30 10 26", "Date write layout or Friday code");
            Check(Data(Compose("645.time", fields)) == "02 01 00 04 04 01 02 03 04 05 06 07 34 52 10", "Time write layout");
            Reject(delegate { Compose("645.time"); });
        });
        Run("645 broadcast and output use distinct control commands", delegate
        {
            PurposeComposition clock = Compose("645.clock", "time", "2026-10-30 10:52:34");
            Check(clock.Bytes[8] == 8 && Hex.Format(clock.Bytes.Skip(1).Take(6).ToArray()) == "99 99 99 99 99 99" && Data(clock) == "34 52 10 30 10 26", "Broadcast clock");
            PurposeComposition output = Compose("645.output", "output", "2");
            Check(output.Bytes[8] == 0x1D && Data(output) == "02", "Output selection");
        });
        Run("local version matches independent frame and preserves full SEQ", delegate
        {
            Check(Hex.Format(Compose("local.E8000301").Bytes) == "68 0C 00 40 03 01 01 03 00 E8 30 16", "Local version wire bytes");
            Check(Compose("local.E8000301", "sequence", "255").Bytes[5] == 255, "SEQ was truncated to four bits");
        });
        Run("local counted addresses and paging are encoded independently", delegate
        {
            Check(Data(Compose("local.E80204A1", "nodes", "010203040506\n112233445566")) == "02 06 05 04 03 02 01 66 55 44 33 22 11", "Count or node order");
            Check(Data(Compose("local.E8030306", "start", "4660", "count", "5")) == "34 12 05", "Paging layout");
        });
        Run("local single task has ADD addresses and one-byte packet length", delegate
        {
            PurposeComposition task = Compose("local.E8020201", "taskId", "4660", "priority", "2", "packets", "68 01 16");
            Check(task.Bytes[3] == 0x60 && Hex.Format(task.Bytes.Skip(4).Take(12).ToArray()) == "01 00 00 00 00 00 02 00 00 00 00 00", "Required ADD=1 addresses");
            Check(Data(task) == "34 12 82 1E 00 03 68 01 16", "Single task layout");
            Reject(delegate { Compose("local.E8020201", "taskId", "61440", "packets", "68 01 16"); });
        });
        Run("local multicast all nodes omits addresses and prefixes each combined frame", delegate
        {
            PurposeComposition task = Compose("local.E8020207", "combined", "1", "allNodes", "1", "packets", "68 01 16\n68 02 16");
            Check(Data(task) == "01 00 90 FF FF 1E 00 0B 00 02 03 00 68 01 16 03 00 68 02 16", "Combined multicast layout");
        });
        Run("local file metadata uses four-byte size and supplied check bytes", delegate
        {
            Check(Data(Compose("local.E8020701", "fileSize", "305419896", "checksum", "DE AD"))
                == "00 01 01 00 00 00 00 00 01 00 78 56 34 12 DE AD 1E", "File header layout");
            Check(Data(Compose("local.E8020702", "segment", "4660", "fileData", "68 01 16", "checksum", "DE AD"))
                == "34 12 03 00 68 01 16 DE AD", "File segment layout");
        });
        Run("DA groups have exact terminal bitmask upper bound and all-points meanings", delegate
        {
            Check(string.Join(";", PurposeComposer.PointGroups("0,1,3,5-6,9,2032").Select(Hex.Format)) == "00 00;35 01;01 02;80 FE", "DA grouping");
            Check(Hex.Format(PurposeComposer.PointGroups("全部")[0]) == "FF FF", "All points marker");
            Reject(delegate { PurposeComposer.PointGroups("2033"); });
            Reject(delegate { PurposeComposer.PointGroups("-1"); });
        });
        Run("uplink current reads cross product of DA groups and little-endian DI", delegate
        {
            PurposeComposition read = Compose("up.current", "points", "1,2,9", "identifiers", "02010100,00010000");
            Check(read.Bytes[14] == 0x0C && read.Bytes[15] == 0x61, "AFN or sequence flags");
            Check(Data(read) == "03 01 00 01 01 02 03 01 00 00 01 00 01 02 00 01 01 02 01 02 00 00 01 00", "DA/DI order or accidental 33H");
        });
        Run("historical range uses four-digit forward year and minute resolution", delegate
        {
            PurposeComposition history = Compose("up.history", "from", "2026-10-30 10:52:00", "to", "2026-10-30 11:52:00");
            Check(history.Bytes[14] == 0x0D && Data(history) == "01 01 00 01 06 05 20 26 10 30 10 52 20 26 10 30 11 52 06", "History range wire order");
            Reject(delegate { Compose("up.history", "from", "2026-10-30 10:52:01"); });
            Reject(delegate { Compose("up.history", "from", "2026-10-30 10:52:00", "to", "2026-10-30 10:52:00"); });
            Reject(delegate { Compose("up.history", "from", "1969-10-30 10:52:00"); });
        });
        Run("historical density restricts the selected data category", delegate
        {
            Reject(delegate { Compose("up.history", "density", "3"); });
            Check(Compose("up.history", "density", "3", "identifiers", "02010100").IsFrame, "Curve query with current-data DI rejected");
            Reject(delegate { Compose("up.history", "density", "6", "identifiers", "02010100"); });
        });
        Run("recent ten events uses FF range while alarms retain their own AFN", delegate
        {
            PurposeComposition recent = Compose("up.events", "rangeMode", "1");
            Check(recent.Bytes[14] == 0x0E && Data(recent) == "01 01 01 00 01 E2 FF FF FF FF FF FF FF FF FF FF FF FF", "Recent event request");
            PurposeComposition alarm = Compose("up.alarms", "from", "2026-10-30 10:52:00", "to", "2026-10-30 11:52:00");
            Check(alarm.Bytes[14] == 0x13 && Data(alarm) == "01 01 33 00 00 E2 20 26 10 30 10 52 20 26 10 30 11 52", "Alarm range or category");
        });
        Run("ordinary task plaintext matches a hand-written parameter vector", delegate
        {
            PurposeComposition task = Compose("up.task.set", "validity", "2", "reportTime", "2026-10-30 10:52:00", "sampleTime", "2026-10-30 11:00:00",
                "sampleUnit", "1", "samplePeriod", "2", "multiplier", "3", "executions", "4660", "points", "1,2,9", "identifiers", "02010100,00010000");
            Check(!task.IsFrame && Data(task) == "00 00 01 03 00 E0 02 52 10 30 10 26 00 0F 00 00 11 30 10 26 01 02 03 34 12 02 03 01 01 02 02 00 01 01 02 00 00 01 00", "Ordinary task layout");
        });
        Run("meter-side task inserts terminal-query schedule before executions", delegate
        {
            PurposeComposition task = Compose("up.meter.set", "validity", "1", "reportTime", "2026-10-30 10:52:00", "sampleTime", "2026-10-30 11:00:00",
                "queryTime", "2026-10-30 12:05:00", "queryUnit", "2", "queryPeriod", "3");
            Check(!task.IsFrame && Data(task) == "00 00 01 15 00 E0 01 52 10 30 10 26 00 0F 00 00 11 30 10 26 00 0F 01 05 12 30 10 26 02 03 00 00 01 01 01 01 00 01 01 02", "Meter-side task layout");
            Reject(delegate { Compose("up.meter.set", "mode", "2"); });
        });
        Run("ordinary task mode four serializes user type before meter type", delegate
        {
            PurposeComposition task = Compose("up.task.set", "mode", "4", "validity", "254", "typeGroups", "3:7\n0:0");
            Check(Hex.Format(task.Bytes.Skip(25).ToArray()) == "02 07 03 00 00 01 00 01 01 02", "Type groups order");
            Check(task.Bytes[6] == 254 && task.Bytes[14] == 4, "Ownership or TLV mode lost");
        });
        Run("task point-count constraint uses encoded groups and rejects product 256", delegate
        {
            string ids = string.Join(",", Enumerable.Range(1, 128).Select(index => index.ToString("X8", CultureInfo.InvariantCulture)));
            Check(!Compose("up.task.set", "points", "1-8", "identifiers", ids).IsFrame, "Raw point count incorrectly used for n");
            Reject(delegate { Compose("up.task.set", "points", "1,9", "identifiers", ids); });
        });
        Run("task count bitmap depth and parameter reads use distinct identifiers", delegate
        {
            Check(Data(Compose("up.task.count")) == "00 00 00 03 00 E0", "Ordinary task count DI");
            Check(Data(Compose("up.meter.validity")) == "00 00 FF 15 00 E0", "Meter validity bitmap DI");
            Check(Data(Compose("up.meter.depth", "taskId", "254")) == "00 00 FE 15 01 E0", "Meter depth DI");
            Check(Data(Compose("up.task.depthSet", "depth", "4660")) == "00 00 01 03 01 E0 34 12", "Depth plaintext");
        });
        Run("settings are plaintext and encode heartbeat BCD and fixed-width APN", delegate
        {
            PurposeComposition heartbeat = Compose("up.parameter.set", "value", "45");
            Check(!heartbeat.IsFrame && Data(heartbeat) == "00 00 07 01 00 E0 45", "Heartbeat or output type");
            PurposeComposition apn = Compose("up.parameter.set", "parameter", "apn", "value", "CMNET");
            Check(!apn.IsFrame && apn.Bytes.Length == 22 && Hex.Format(apn.Bytes.Skip(6).Take(5).ToArray()) == "43 4D 4E 45 54" && apn.Bytes.Skip(11).All(value => value == 0), "APN padding");
            Reject(delegate { Compose("up.parameter.set", "parameter", "apn", "value", "南网"); });
        });
        Run("XML name search respects AFN and bounded results", delegate
        {
            Type catalog = typeof(FrameCodec).Assembly.GetType("FrameTools.ProtocolCatalog", true);
            MethodInfo search = catalog.GetMethod("Search", BindingFlags.Public | BindingFlags.Static);
            object[] items = ((Array)search.Invoke(null, new object[] { ProtocolKind.CsgUplink, new byte[] { 0x0C }, "A相电压" })).Cast<object>().ToArray();
            Check(items.Any(item => (string)item.GetType().GetField("Id").GetValue(item) == "02010100"), "Current voltage not found by name");
            Array wrong = (Array)search.Invoke(null, new object[] { ProtocolKind.CsgUplink, new byte[] { 0x0E }, "02010100" });
            Check(wrong.Length == 0, "Wrong AFN selectable");
            Check(((Array)search.Invoke(null, new object[] { ProtocolKind.CsgUplink, new byte[] { 0x0C }, "" })).Length <= 200, "Unbounded control items");
            Check(((Array)search.Invoke(null, new object[] { ProtocolKind.CsgLocal, new byte[] { 0x03 }, "E80003B0" })).Length == 0, "Ambiguous original exposed");
        });
        Console.WriteLine("PURPOSE RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
