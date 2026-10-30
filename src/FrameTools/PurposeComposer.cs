using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FrameTools
{
    public enum PurposeEditor
    {
        Text, Number, Choice, DateTime, Lines, Identifiers, TypeGroups
    }

    public sealed class PurposeField
    {
        public string Key;
        public string Label;
        public PurposeEditor Editor;
        public string Default;
        public string Help;
        public long Maximum;
        public long Minimum;
        public string[] Choices;
        public bool IncludeSeconds;
    }

    public sealed class PurposeCommand
    {
        public ProtocolKind Protocol;
        public string Key;
        public string Category;
        public string Name;
        public string Note;
        public bool ParameterOnly;
        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class PurposeComposition
    {
        public ProtocolKind Protocol;
        public byte[] Bytes;
        public bool IsFrame;
        public string Summary;
    }

    public static class PurposeComposer
    {
        private static readonly List<PurposeCommand> Commands = CreateCommands();

        private static List<PurposeCommand> CreateCommands()
        {
            List<PurposeCommand> commands = new List<PurposeCommand>();
            Add(commands, ProtocolKind.Dlt645, "数据读取", "645.read", "读取数据项", "按名称选择或填写一个DI；自动处理DI字节序及数据域加33H。");
            Add(commands, ProtocolKind.Dlt645, "地址与时钟", "645.address.read", "读取通信地址", "点对点请求，地址域自动使用AA，不需要填写表地址。");
            Add(commands, ProtocolKind.Dlt645, "地址与时钟", "645.address.write", "设置通信地址", "点对点命令，需电表编程键配合；此处只生成报文。");
            Add(commands, ProtocolKind.Dlt645, "地址与时钟", "645.clock", "广播校时", "广播地址自动填写；电表实际执行还受规约时差和次数条件约束。");
            Add(commands, ProtocolKind.Dlt645, "参数设置", "645.date", "设置日期及星期", "明文密码方式：权限02或04，需要密码验证及编程键配合。");
            Add(commands, ProtocolKind.Dlt645, "参数设置", "645.time", "设置时间", "明文密码方式：权限02或04；不生成98/99密文或MAC。");
            Add(commands, ProtocolKind.Dlt645, "输出控制", "645.output", "多功能端子输出", "选择时钟秒脉冲、需量周期或时段投切输出。");
            string[,] local =
            {
                { "模块管理", "E8000301", "查询厂商与版本" }, { "模块管理", "E8000302", "查询模块运行模式" },
                { "模块管理", "E8000303", "查询主节点地址" }, { "模块管理", "E8020401", "设置主节点地址" },
                { "模块管理", "E8020101", "复位硬件" }, { "模块管理", "E8020102", "初始化档案" },
                { "模块管理", "E8020103", "初始化任务" }, { "模块管理", "E80204A3", "设置CCO时钟" },
                { "节点与网络", "E8000305", "查询从节点数量" }, { "节点与网络", "E8030306", "查询从节点列表" },
                { "节点与网络", "E8020402", "添加从节点" }, { "节点与网络", "E8020403", "删除从节点" },
                { "节点与网络", "E8030308", "查询从节点父节点" }, { "节点与网络", "E803030C", "查询指定节点相位" },
                { "节点与网络", "E803030D", "批量查询节点相位" }, { "节点与网络", "E803030E", "查询台区识别结果" },
                { "节点与网络", "E8030315", "批量查询模块特征" }, { "节点与网络", "E8020404", "设置节点事件上报" },
                { "节点与网络", "E8020405", "激活节点主动注册" }, { "节点与网络", "E8020406", "终止节点主动注册" },
                { "节点与网络", "E80204A0", "设置即插即用开关" }, { "节点与网络", "E80303A0", "查询即插即用开关" },
                { "节点与网络", "E80204A1", "添加黑名单节点" }, { "节点与网络", "E80204A2", "删除黑名单节点" },
                { "节点与网络", "E80303A1", "查询黑名单节点" },
                { "任务管理", "E8020201", "添加单节点任务" }, { "任务管理", "E8020207", "添加多播任务" },
                { "任务管理", "E8020202", "删除任务" }, { "任务管理", "E8000203", "查询未完成任务数量" },
                { "任务管理", "E8030204", "查询未完成任务列表" }, { "任务管理", "E8030205", "查询任务详情" },
                { "任务管理", "E8000206", "查询剩余可分配任务数" }, { "任务管理", "E800030B", "查询建议超时时间" },
                { "任务管理", "E8020208", "启动任务" }, { "任务管理", "E8020209", "暂停任务" },
                { "文件传输", "E8020701", "启动文件传输" }, { "文件传输", "E8020702", "传输文件段" },
                { "文件传输", "E8000703", "查询文件信息" }, { "文件传输", "E8000704", "查询文件处理进度" },
                { "采集器", "EA002301", "查询采集器厂商与版本" },
                { "应答模拟", "E8010001", "确认应答" }, { "应答模拟", "E8010002", "否认应答" }
            };
            for (int i = 0; i < local.GetLength(0); i++)
            {
                Add(commands, ProtocolKind.CsgLocal, local[i, 0], "local." + local[i, 1], local[i, 2], "按本地接口原文编码；SEQ为完整8位序号。生成后可送到解析页核对。");
            }
            Add(commands, ProtocolKind.CsgUplink, "参数类", "up.parameters", "读取参数", "测量点0表示终端；可按名称勾选多个数据项。");
            Add(commands, ProtocolKind.CsgUplink, "参数类", "up.parameter.set", "编辑设置参数", "生成DA、DI和参数明文；正式下发还需要整体加密及PW/MAC，不输出伪造认证报文。", true);
            Add(commands, ProtocolKind.CsgUplink, "当前数据类", "up.current", "读取当前数据", "自动把测量点组合为DA，数据项按DI0先传；不做33H处理。");
            Add(commands, ProtocolKind.CsgUplink, "历史数据类", "up.history", "按时间范围读取历史数据", "时间区间为开始包含、结束不包含。每个DA/DI后携带独立的时间范围和数据密度。");
            Add(commands, ProtocolKind.CsgUplink, "事件告警类", "up.events", "读取事件记录", "可按时间范围读取，或用规约规定的全FF时间请求最近10条事件。");
            Add(commands, ProtocolKind.CsgUplink, "事件告警类", "up.alarms", "读取告警数据", "告警AFN=13与事件记录AFN=0E分开，按时间范围读取。");
            foreach (string family in new[] { "task", "meter" })
            {
                string category = family == "task" ? "普通任务类" : "表端任务类";
                Add(commands, ProtocolKind.CsgUplink, category, "up." + family + ".get", "读取指定任务参数", "任务号1～254；0为任务数量，FF为有效性位图，均有独立用途。");
                Add(commands, ProtocolKind.CsgUplink, category, "up." + family + ".set", "编辑任务设置参数", "输出待安全封装的参数明文；任务时间与历史查询时间使用不同编码。", true);
                Add(commands, ProtocolKind.CsgUplink, category, "up." + family + ".count", "读取有效任务数量", "读取终端中有效的任务总数。");
                Add(commands, ProtocolKind.CsgUplink, category, "up." + family + ".validity", "读取任务有效性位图", "FF表示32字节任务有效性位图，不作为第255号任务。");
                Add(commands, ProtocolKind.CsgUplink, category, "up." + family + ".depth", "读取任务存储深度", "存储深度使用独立数据标识。");
                Add(commands, ProtocolKind.CsgUplink, category, "up." + family + ".depthSet", "编辑任务存储深度", "生成2字节BIN存储深度参数明文，正式下发需要安全封装。", true);
            }
            return commands;
        }

        private static void Add(List<PurposeCommand> list, ProtocolKind protocol, string category, string key, string name, string note, bool parameterOnly = false)
        {
            list.Add(new PurposeCommand { Protocol = protocol, Category = category, Key = key, Name = name, Note = note, ParameterOnly = parameterOnly });
        }

        public static PurposeCommand[] GetCommands(ProtocolKind protocol)
        {
            return Commands.Where(command => command.Protocol == protocol).ToArray();
        }

        private static PurposeField Text(string key, string label, string value, string help = "", PurposeEditor editor = PurposeEditor.Text)
        {
            return new PurposeField { Key = key, Label = label, Default = value, Editor = editor, Help = help };
        }

        private static PurposeField Number(string key, string label, long value, long maximum, long minimum = 0, string help = "")
        {
            return new PurposeField { Key = key, Label = label, Default = value.ToString(CultureInfo.InvariantCulture), Editor = PurposeEditor.Number, Minimum = minimum, Maximum = maximum, Help = help };
        }

        private static PurposeField Choice(string key, string label, params string[] choices)
        {
            return new PurposeField { Key = key, Label = label, Default = choices[0].Split('|')[0], Editor = PurposeEditor.Choice, Choices = choices, Help = "" };
        }

        private static PurposeField Date(string key, string label, bool seconds = true)
        {
            PurposeField field = Text(key, label, DateTime.Now.ToString(seconds ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:00", CultureInfo.InvariantCulture), "", PurposeEditor.DateTime);
            field.IncludeSeconds = seconds;
            return field;
        }

        private static string Value(IDictionary<string, string> input, string key, string fallback = "")
        {
            string value;
            return input.TryGetValue(key, out value) ? value : fallback;
        }

        public static PurposeField[] GetFields(PurposeCommand command, IDictionary<string, string> input)
        {
            List<PurposeField> fields = new List<PurposeField>();
            string key = command.Key;
            if (command.Protocol == ProtocolKind.Dlt645)
            {
                if (key != "645.address.read" && key != "645.address.write" && key != "645.clock")
                {
                    fields.Add(Text("address", "电表通信地址", "000000000001", "12位地址，高位在前；允许读表缩位地址AA。"));
                }
                fields.Add(Number("preamble", "前导FE数量", 0, 4));
                if (key == "645.read")
                {
                    fields.Add(Text("identifiers", "数据项", "02010100", "一次读取一个DI。", PurposeEditor.Identifiers));
                }
                if (key == "645.address.write")
                {
                    fields.Add(Text("newAddress", "新通信地址", "000000000002", "12位十进制表地址。"));
                }
                if (key == "645.clock" || key == "645.date" || key == "645.time")
                {
                    fields.Add(Date("time", key == "645.date" ? "日期 / 星期" : key == "645.time" ? "时间" : "日期时间"));
                }
                if (key == "645.date" || key == "645.time")
                {
                    fields.Add(Choice("authority", "密码权限", "4|04级", "2|02级"));
                    fields.Add(Text("password", "密码", "", "3字节HEX，按P0 P1 P2传输顺序输入；不保存密码。"));
                    fields.Add(Text("operator", "操作者代码", "", "4字节HEX，按C0 C1 C2 C3传输顺序输入。"));
                }
                if (key == "645.output")
                {
                    fields.Add(Choice("output", "端子输出类型", "0|时钟秒脉冲", "1|需量周期", "2|时段投切"));
                }
                return fields.ToArray();
            }
            if (!command.ParameterOnly)
            {
                if (command.Protocol == ProtocolKind.CsgUplink)
                {
                    fields.Add(Text("address", "终端通信地址", "06 01 44 01 00 00 01", "7字节HEX，按传输顺序填写A1(3)+A2(3)+A3(1)。"));
                }
                fields.Add(Number("sequence", "帧序号", 1, command.Protocol == ProtocolKind.CsgLocal ? 255 : 15));
            }
            if (command.Protocol == ProtocolKind.CsgLocal)
            {
                string di = key.Substring(6);
                if (di == "E8020201")
                {
                    fields.Add(Text("source", "源地址", "000000000001", "本命令要求外层ADD=1，6字节地址，高位在前。"));
                    fields.Add(Text("destination", "目的地址", "000000000002", "6字节地址，高位在前。"));
                }
                if (new[] { "E8030306", "E803030D", "E803030E", "E8030315", "E80303A1", "E8030204" }.Contains(di))
                {
                    fields.Add(Number("start", "起始序号", 0, 65535));
                    fields.Add(Number("count", "查询数量", 1, 255, 1));
                }
                if (new[] { "E8020402", "E8020403", "E80204A1", "E80204A2", "E803030C" }.Contains(di))
                {
                    fields.Add(Text("nodes", "节点地址列表", "000000000001", "每行一个6字节地址，高位在前。", PurposeEditor.Lines));
                }
                if (di == "E8030308" || di == "E8020401")
                {
                    fields.Add(Text("node", "节点地址", "000000000001", "6字节地址，高位在前。"));
                }
                if (di == "E8020404" || di == "E80204A0")
                {
                    fields.Add(Choice("enabled", "功能开关", "0|关闭 / 禁止", "1|开启 / 允许"));
                }
                if (di == "E80204A3")
                {
                    fields.Add(Date("time", "CCO时间"));
                }
                if (new[] { "E8020201", "E8020207", "E8020202", "E8030205" }.Contains(di))
                {
                    fields.Add(Number("taskId", "任务ID", 1, 0xEFFF));
                }
                if (di == "E8020201" || di == "E8020207")
                {
                    fields.Add(Choice("priority", "任务优先级", "0|0（最高）", "1|1", "2|2", "3|3（最低）"));
                    fields.Add(Choice("response", "任务响应", "1|需要数据返回", "0|不需要数据返回"));
                    fields.Add(Choice("combined", "报文形式", "0|单条原始报文", "1|组合多帧"));
                    if (di == "E8020207")
                    {
                        fields.Add(Choice("allNodes", "多播目标", "0|指定节点列表", "1|全部节点（FFFF）"));
                        if (Value(input, "allNodes", "0") == "0")
                        {
                            fields.Add(Text("nodes", "目标节点列表", "000000000001", "每行一个6字节地址，高位在前。", PurposeEditor.Lines));
                        }
                    }
                    fields.Add(Number("timeout", "超时时间（秒）", 30, 65535));
                    fields.Add(Text("packets", "内层报文", "", Value(input, "combined", "0") == "1" ? "组合多帧：每行一条完整HEX报文，程序添加条数和子报文长度。" : "单条HEX报文；保持原始内容，不额外加33H。", PurposeEditor.Lines));
                }
                if (di == "E8020701")
                {
                    fields.Add(Number("nature", "文件性质", 0, 255));
                    fields.Add(Number("fileId", "文件ID", 1, 255));
                    fields.Add(Text("node", "文件目的地址", "000000000001", "6字节地址，高位在前。"));
                    fields.Add(Number("segments", "文件总段数", 1, 65535, 1));
                    fields.Add(Number("fileSize", "文件大小（字节）", 0, uint.MaxValue));
                    fields.Add(Text("checksum", "文件总校验", "", "2字节HEX，按传输顺序输入，不推测校验算法。"));
                    fields.Add(Number("timeout", "传输超时时间", 30, 255));
                }
                if (di == "E8020702")
                {
                    fields.Add(Number("segment", "文件段号", 0, 65535));
                    fields.Add(Text("fileData", "文件段内容", "", "HEX内容，程序填写段长度。", PurposeEditor.Lines));
                    fields.Add(Text("checksum", "文件段校验", "", "2字节HEX，按传输顺序输入。"));
                }
                if (di == "E8010001")
                {
                    fields.Add(Number("wait", "等待时间（秒）", 0, 65535));
                }
                if (di == "E8010002")
                {
                    fields.Add(Choice("error", "否认原因", "0|通信超时", "1|无效数据内容", "2|长度错误", "3|校验错误", "4|DI不存在", "5|格式错误", "6|表号重复", "7|表号不存在", "8|电表无应答", "9|主节点忙", "10|命令不支持", "11|从节点不应答", "12|从节点不在网内", "13|任务数不足", "14|上报任务不存在", "15|任务ID重复", "16|查询任务不存在", "17|任务ID不存在", "255|其他"));
                }
                return fields.ToArray();
            }
            bool task = key.StartsWith("up.task.", StringComparison.Ordinal) || key.StartsWith("up.meter.", StringComparison.Ordinal);
            if (task)
            {
                if (!key.EndsWith(".count", StringComparison.Ordinal) && !key.EndsWith(".validity", StringComparison.Ordinal))
                {
                    fields.Add(Number("taskId", "任务号", 1, 254, 1));
                }
                if (key.EndsWith(".depthSet", StringComparison.Ordinal))
                {
                    fields.Add(Number("depth", "存储深度", 1, 65535));
                }
                if (key.EndsWith(".set", StringComparison.Ordinal))
                {
                    bool meter = key.StartsWith("up.meter.", StringComparison.Ordinal);
                    fields.Add(meter ? Choice("validity", "有效性", "0|无效", "1|有效") : Number("validity", "有效性 / 中间件归属", 1, 254, 0, "0为无效；1～254为有效，归属特定中间件。"));
                    fields.Add(Date("reportTime", "上报基准时间", false));
                    fields.Add(Choice("reportUnit", "上报周期单位", "0|分", "1|时", "2|日", "3|月"));
                    fields.Add(Number("reportPeriod", "上报周期", 15, 255, 0, "0表示不上报。"));
                    fields.Add(meter ? Choice("mode", "数据结构方式", "0|自描述", "1|任务定义格式") : Choice("mode", "数据结构方式", "0|自描述", "1|任务定义格式", "2|自描述 / 类型筛选", "3|TLV", "4|TLV / 类型筛选"));
                    fields.Add(Date("sampleTime", "采样基准时间", false));
                    fields.Add(Choice("sampleUnit", "采样周期单位", "0|分", "1|时", "2|日", "3|月"));
                    fields.Add(Number("samplePeriod", "采样周期", 15, 255));
                    fields.Add(Number("multiplier", "数据抽取倍率", 1, 255, 1));
                    if (meter)
                    {
                        fields.Add(Date("queryTime", "终端查询基准时间", false));
                        fields.Add(Choice("queryUnit", "终端查询周期单位", "0|分", "1|时", "2|日", "3|月"));
                        fields.Add(Number("queryPeriod", "终端查询周期", 15, 255));
                    }
                    fields.Add(Number("executions", "执行次数", 0, 65535, 0, "0表示一直执行。"));
                    if (Value(input, "mode", "0") == "2" || Value(input, "mode", "0") == "4")
                    {
                        fields.Add(Text("typeGroups", "电表类型 / 用户类型", "0:0", "各0～255；0表示不判断该类型。可在表格末行新增一组。", PurposeEditor.TypeGroups));
                    }
                    else
                    {
                        fields.Add(Text("points", "测量点", "1", "例如1,3,5-6；0为终端，全部表示所有测量点。"));
                    }
                    fields.Add(Text("identifiers", "采集数据项", "02010100", "可按名称选择多个DI。", PurposeEditor.Identifiers));
                }
                return fields.ToArray();
            }
            if (key == "up.parameter.set")
            {
                fields.Add(Choice("parameter", "设置内容", "heartbeat|心跳周期", "clock|终端时间", "apn|APN", "transport|TCP / UDP", "compression|通信压缩"));
                string parameter = Value(input, "parameter", "heartbeat");
                if (parameter == "heartbeat")
                {
                    fields.Add(Number("value", "心跳周期（分钟）", 15, 99));
                }
                if (parameter == "clock")
                {
                    fields.Add(Date("time", "终端时间"));
                }
                if (parameter == "apn")
                {
                    fields.Add(Text("value", "APN", "CMNET", "最多16个ASCII字符。"));
                }
                if (parameter == "transport")
                {
                    fields.Add(Choice("value", "传输方式", "0|TCP", "1|UDP"));
                }
                if (parameter == "compression")
                {
                    fields.Add(Choice("value", "压缩方式", "0|非压缩", "1|压缩"));
                }
                return fields.ToArray();
            }
            fields.Add(Text("points", "测量点", key == "up.parameters" ? "0" : "1", "例如1,3,5-6；0为终端，全部表示所有测量点。"));
            string defaultDi = key == "up.parameters" ? "E0000107" : key == "up.history" ? "05060100" : key == "up.events" ? "E2010001" : key == "up.alarms" ? "E2000033" : "02010100";
            fields.Add(Text("identifiers", "数据项", defaultDi, "可填写逗号分隔的DI，或按名称选择。", PurposeEditor.Identifiers));
            if (key == "up.events")
            {
                fields.Add(Choice("rangeMode", "读取范围", "0|指定时间范围", "1|最近10条事件"));
            }
            if (key == "up.history" || key == "up.alarms" || (key == "up.events" && Value(input, "rangeMode", "0") == "0"))
            {
                PurposeField start = Date("from", "开始时间（包含）", false);
                start.Default = (key == "up.history" ? DateTime.Today.AddDays(-1) : DateTime.Now.AddDays(-1)).ToString("yyyy-MM-dd HH:mm:00", CultureInfo.InvariantCulture);
                fields.Add(start);
                PurposeField end = Date("to", "结束时间（不包含）", false);
                end.Default = (key == "up.history" ? DateTime.Today : DateTime.Now).ToString("yyyy-MM-dd HH:mm:00", CultureInfo.InvariantCulture);
                fields.Add(end);
            }
            if (key == "up.history")
            {
                fields.Add(Choice("density", "数据密度", "6|日", "7|月", "1|1分钟", "2|5分钟", "3|15分钟", "4|30分钟", "5|60分钟", "0|终端存储密度", "8|结算日"));
            }
            return fields.ToArray();
        }

        public static IDictionary<string, string> Defaults(PurposeCommand command)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (PurposeField field in GetFields(command, values))
            {
                values[field.Key] = field.Default;
            }
            return values;
        }

        internal static byte[] IdentifierAfns(PurposeCommand command, IDictionary<string, string> input)
        {
            if (command.Key == "up.parameters")
            {
                return new byte[] { 0x0A };
            }
            if (command.Key == "up.current")
            {
                return new byte[] { 0x0C };
            }
            if (command.Key == "up.events")
            {
                return new byte[] { 0x0E };
            }
            if (command.Key == "up.alarms")
            {
                return new byte[] { 0x13 };
            }
            if (command.Key == "up.history")
            {
                int density = int.Parse(Value(input, "density", "6"), CultureInfo.InvariantCulture);
                if (density >= 1 && density <= 5)
                {
                    return new byte[] { 0x0C };
                }
                if (density == 6 || density == 7)
                {
                    return new byte[] { 0x0D };
                }
            }
            return new byte[] { 0x0C, 0x0D };
        }

        public static PurposeComposition Compose(PurposeCommand command, IDictionary<string, string> supplied)
        {
            if (!Commands.Contains(command))
            {
                throw new ArgumentException("请选择有效的用途。");
            }
            Dictionary<string, string> input = new Dictionary<string, string>(supplied);
            foreach (PurposeField field in GetFields(command, input))
            {
                if (!input.ContainsKey(field.Key))
                {
                    input[field.Key] = field.Default;
                }
                if (field.Editor == PurposeEditor.Number)
                {
                    long number;
                    if (!long.TryParse(input[field.Key], NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < field.Minimum || number > field.Maximum)
                    {
                        throw new FormatException(field.Label + "需为" + field.Minimum + "～" + field.Maximum + "的整数。");
                    }
                }
                if (field.Editor == PurposeEditor.Choice && !field.Choices.Any(option => option.Split('|')[0] == input[field.Key]))
                {
                    throw new FormatException("请选择有效的" + field.Label + "。");
                }
            }
            if (command.Key == "up.history")
            {
                byte[] afns = IdentifierAfns(command, input);
                foreach (string di in Identifiers(input["identifiers"]))
                {
                    if (!afns.Any(afn => ProtocolCatalog.Name(command.Protocol, afn, false, di) != null))
                    {
                        throw new FormatException("DI " + di + "不属于当前密度对应的数据类别，请重新选择数据项；扩展DI可使用原始HEX入口。");
                    }
                }
            }
            return command.Protocol == ProtocolKind.Dlt645 ? Compose645(command, input)
                : command.Protocol == ProtocolKind.CsgLocal ? ComposeLocal(command, input) : ComposeUplink(command, input);
        }

        private static int Int(IDictionary<string, string> input, string key)
        {
            return int.Parse(input[key], CultureInfo.InvariantCulture);
        }

        private static byte[] FixedHex(string value, int length, string label)
        {
            byte[] bytes = Hex.Parse(value);
            if (bytes.Length != length)
            {
                throw new FormatException(label + "需为" + length + "字节HEX。");
            }
            return bytes;
        }

        private static byte[] Address(string value)
        {
            return FixedHex(value, 6, "地址").Reverse().ToArray();
        }

        private static DateTime Time(IDictionary<string, string> input, string key)
        {
            DateTime value;
            if (!DateTime.TryParseExact(input[key], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                throw new FormatException("请填写有效日期时间。");
            }
            return value;
        }

        private static byte Bcd(int number)
        {
            if (number < 0 || number > 99)
            {
                throw new FormatException("BCD字段超出两位十进制范围。");
            }
            return (byte)((number / 10 << 4) | number % 10);
        }

        private static byte[] Clock(DateTime value, bool seconds)
        {
            if (value.Year < 2000 || value.Year > 2099)
            {
                throw new FormatException("两位年份字段仅支持2000～2099年。");
            }
            if (!seconds && value.Second != 0)
            {
                throw new FormatException("任务时间精确到分钟，请将秒设为00。");
            }
            List<byte> bytes = new List<byte>();
            if (seconds)
            {
                bytes.Add(Bcd(value.Second));
            }
            bytes.AddRange(new[] { Bcd(value.Minute), Bcd(value.Hour), Bcd(value.Day), Bcd(value.Month), Bcd(value.Year % 100) });
            return bytes.ToArray();
        }

        private static byte[] RangeTime(DateTime value)
        {
            // 上行6.1.6的查询时间按四位年份、月、日、时、分正序传输。
            if (value.Year < 1970 || value.Year > 2099)
            {
                throw new FormatException("查询时间年份需为1970～2099年。");
            }
            return new[] { Bcd(value.Year / 100), Bcd(value.Year % 100), Bcd(value.Month), Bcd(value.Day), Bcd(value.Hour), Bcd(value.Minute) };
        }

        private static void Bin(List<byte> bytes, long value, int size)
        {
            if (value < 0 || value >= (1L << (size * 8)))
            {
                throw new FormatException("数值超出" + size + "字节BIN范围。");
            }
            for (int i = 0; i < size; i++)
            {
                bytes.Add((byte)(value >> (8 * i)));
            }
        }

        private static PurposeComposition Result(PurposeCommand command, byte[] bytes, bool frame, string summary)
        {
            return new PurposeComposition { Protocol = command.Protocol, Bytes = bytes, IsFrame = frame, Summary = command.Name + "；" + summary };
        }

        private static PurposeComposition Compose645(PurposeCommand command, IDictionary<string, string> input)
        {
            string address = Value(input, "address", "AAAAAAAAAAAA");
            byte control = 0x11;
            byte[] data = new byte[0];
            switch (command.Key)
            {
                case "645.read":
                    string[] ids = Identifiers(input["identifiers"]);
                    if (ids.Length != 1)
                    {
                        throw new FormatException("645一次读取一个数据项。");
                    }
                    data = Hex.Identifier(ids[0]);
                    break;
                case "645.address.read": address = "AAAAAAAAAAAA"; control = 0x13; break;
                case "645.address.write":
                    address = "AAAAAAAAAAAA";
                    control = 0x15;
                    if (input["newAddress"].Length != 12 || input["newAddress"].Any(character => character < '0' || character > '9'))
                    {
                        throw new FormatException("新通信地址需为12位十进制数字。");
                    }
                    data = Address(input["newAddress"]);
                    break;
                case "645.clock": address = "999999999999"; control = 8; data = Clock(Time(input, "time"), true); break;
                case "645.date":
                case "645.time":
                    control = 0x14;
                    DateTime time = Time(input, "time");
                    byte[] clock = Clock(time, true);
                    byte[] value = command.Key == "645.date" ? new[] { (byte)time.DayOfWeek, Bcd(time.Day), Bcd(time.Month), Bcd(time.Year % 100) } : clock.Take(3).ToArray();
                    string di = command.Key == "645.date" ? "04000101" : "04000102";
                    data = Hex.Identifier(di).Concat(new[] { (byte)Int(input, "authority") })
                        .Concat(FixedHex(input["password"], 3, "密码")).Concat(FixedHex(input["operator"], 4, "操作者代码")).Concat(value).ToArray();
                    break;
                case "645.output": control = 0x1D; data = new[] { (byte)Int(input, "output") }; break;
            }
            return Result(command, FrameCodec.Build645(address, control, data, Int(input, "preamble")), true, "完整帧；已生成L、CS及33H编码。");
        }

        public static string[] Identifiers(string text)
        {
            string[] ids = text.Split(new[] { ',', '，', ';', '；', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.ToUpperInvariant()).Distinct().ToArray();
            if (ids.Length == 0)
            {
                throw new FormatException("至少选择一个数据项。");
            }
            foreach (string id in ids)
            {
                if (id.Length != 8 || Hex.Parse(id).Length != 4)
                {
                    throw new FormatException("数据标识需为8位HEX，如02010100。");
                }
            }
            return ids;
        }

        public static byte[][] PointGroups(string text)
        {
            if (text.Trim() == "全部" || text.Trim() == "*")
            {
                return new[] { new byte[] { 0xFF, 0xFF } };
            }
            SortedSet<int> points = new SortedSet<int>();
            foreach (string token in text.Split(new[] { ',', '，', ';', '；', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] range = token.Split('-');
                int first;
                int last;
                if (range.Length > 2 || !int.TryParse(range[0], out first) || !int.TryParse(range[range.Length - 1], out last)
                    || first < 0 || last > 2032 || first > last)
                {
                    throw new FormatException("测量点需为0～2032，可用1,3,5-6或全部。");
                }
                for (int point = first; point <= last; point++)
                {
                    points.Add(point);
                }
            }
            if (points.Count == 0)
            {
                throw new FormatException("至少填写一个测量点。");
            }
            List<byte[]> groups = new List<byte[]>();
            if (points.Contains(0))
            {
                groups.Add(new byte[] { 0, 0 });
            }
            foreach (IGrouping<int, int> group in points.Where(point => point > 0).GroupBy(point => (point - 1) / 8 + 1))
            {
                byte mask = 0;
                foreach (int point in group)
                {
                    mask |= (byte)(1 << ((point - 1) % 8));
                }
                groups.Add(new[] { mask, (byte)group.Key });
            }
            return groups.ToArray();
        }

        private static byte[][] Nodes(string text)
        {
            string[] rows = text.Split(new[] { '\r', '\n', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length == 0)
            {
                throw new FormatException("至少填写一个节点地址。");
            }
            return rows.Select(row => Address(row.Trim())).ToArray();
        }

        private static byte[] Packet(IDictionary<string, string> input)
        {
            if (Int(input, "combined") == 0)
            {
                byte[] packet = Hex.Parse(input["packets"]);
                if (packet.Length == 0 || packet.Length > 255)
                {
                    throw new FormatException("单条原始报文需为1～255字节。");
                }
                return packet;
            }
            string[] lines = input["packets"].Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 || lines.Length > 255)
            {
                throw new FormatException("组合报文需为1～255行，每行一条HEX报文。");
            }
            List<byte> bytes = new List<byte> { (byte)lines.Length };
            foreach (string line in lines)
            {
                byte[] packet = Hex.Parse(line);
                if (packet.Length == 0 || packet.Length > 4095)
                {
                    throw new FormatException("每条组合子报文需为1～4095字节。");
                }
                Bin(bytes, packet.Length, 2);
                bytes.AddRange(packet);
            }
            return bytes.ToArray();
        }

        private static PurposeComposition ComposeLocal(PurposeCommand command, IDictionary<string, string> input)
        {
            string di = command.Key.Substring(6);
            List<byte> data = new List<byte>();
            switch (di)
            {
                case "E8030306": case "E803030D": case "E803030E": case "E8030315": case "E80303A1": case "E8030204":
                    Bin(data, Int(input, "start"), 2); Bin(data, Int(input, "count"), 1); break;
                case "E8020402": case "E8020403": case "E80204A1": case "E80204A2": case "E803030C":
                    byte[][] nodes = Nodes(input["nodes"]);
                    Bin(data, nodes.Length, 1);
                    foreach (byte[] node in nodes)
                    {
                        data.AddRange(node);
                    }
                    break;
                case "E8030308": case "E8020401": data.AddRange(Address(input["node"])); break;
                case "E8020404": case "E80204A0": data.Add((byte)Int(input, "enabled")); break;
                case "E80204A3": data.AddRange(Clock(Time(input, "time"), true)); break;
                case "E8020202": case "E8030205": Bin(data, Int(input, "taskId"), 2); break;
                case "E8020201": case "E8020207":
                    Bin(data, Int(input, "taskId"), 2);
                    data.Add((byte)(Int(input, "priority") | Int(input, "combined") * 16 | Int(input, "response") * 128));
                    if (di == "E8020207")
                    {
                        if (Int(input, "allNodes") == 1)
                        {
                            Bin(data, 65535, 2);
                        }
                        else
                        {
                            byte[][] addresses = Nodes(input["nodes"]);
                            if (addresses.Length >= 65535)
                            {
                                throw new FormatException("指定节点数量需小于65535。");
                            }
                            Bin(data, addresses.Length, 2);
                            foreach (byte[] node in addresses)
                            {
                                data.AddRange(node);
                            }
                        }
                    }
                    Bin(data, Int(input, "timeout"), 2);
                    byte[] packet = Packet(input);
                    Bin(data, packet.Length, Int(input, "combined") == 1 ? 2 : 1);
                    data.AddRange(packet);
                    break;
                case "E8020701":
                    data.Add((byte)Int(input, "nature")); data.Add((byte)Int(input, "fileId")); data.AddRange(Address(input["node"]));
                    Bin(data, Int(input, "segments"), 2); Bin(data, long.Parse(input["fileSize"], CultureInfo.InvariantCulture), 4);
                    data.AddRange(FixedHex(input["checksum"], 2, "文件总校验")); data.Add((byte)Int(input, "timeout")); break;
                case "E8020702":
                    byte[] file = Hex.Parse(input["fileData"]);
                    Bin(data, Int(input, "segment"), 2); Bin(data, file.Length, 2); data.AddRange(file);
                    data.AddRange(FixedHex(input["checksum"], 2, "文件段校验")); break;
                case "E8010001": Bin(data, Int(input, "wait"), 2); break;
                case "E8010002": data.Add((byte)Int(input, "error")); break;
            }
            bool addressed = di == "E8020201";
            byte control = di.StartsWith("E801", StringComparison.Ordinal) ? (byte)0x80 : addressed ? (byte)0x60 : (byte)0x40;
            byte afn = Hex.Parse(di)[2];
            return Result(command, FrameCodec.BuildLocal(control, Value(input, "source"), Value(input, "destination"), afn,
                (byte)Int(input, "sequence"), Hex.Identifier(di), data.ToArray()), true, "DI=" + di + "；完整本地帧，已填写长度与CS。");
        }

        private static byte[] TaskParameters(bool meter, IDictionary<string, string> input)
        {
            List<byte> bytes = new List<byte> { (byte)Int(input, "validity") };
            bytes.AddRange(Clock(Time(input, "reportTime"), false));
            bytes.Add((byte)Int(input, "reportUnit")); bytes.Add((byte)Int(input, "reportPeriod")); bytes.Add((byte)Int(input, "mode"));
            bytes.AddRange(Clock(Time(input, "sampleTime"), false));
            bytes.Add((byte)Int(input, "sampleUnit")); bytes.Add((byte)Int(input, "samplePeriod")); bytes.Add((byte)Int(input, "multiplier"));
            if (meter)
            {
                bytes.AddRange(Clock(Time(input, "queryTime"), false));
                bytes.Add((byte)Int(input, "queryUnit")); bytes.Add((byte)Int(input, "queryPeriod"));
            }
            Bin(bytes, Int(input, "executions"), 2);
            byte[][] groups;
            if (Int(input, "mode") == 2 || Int(input, "mode") == 4)
            {
                groups = input["typeGroups"].Split(new[] { '\r', '\n', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries).Select(row =>
                {
                    string[] parts = row.Trim().Split(':');
                    byte meterType;
                    byte userType;
                    if (parts.Length != 2 || !byte.TryParse(parts[0], out meterType) || !byte.TryParse(parts[1], out userType))
                    {
                        throw new FormatException("类型组需为电表类型:用户类型，各0～255。");
                    }
                    return new[] { userType, meterType };
                }).ToArray();
            }
            else
            {
                groups = PointGroups(input["points"]);
            }
            string[] ids = Identifiers(input["identifiers"]);
            if (groups.Length == 0 || groups.Length * (long)ids.Length >= 256)
            {
                throw new FormatException("编码后的信息点组数×数据项数必须小于256。");
            }
            Bin(bytes, groups.Length, 1);
            foreach (byte[] group in groups)
            {
                bytes.AddRange(group);
            }
            Bin(bytes, ids.Length, 1);
            foreach (string id in ids)
            {
                bytes.AddRange(Hex.Identifier(id));
            }
            return bytes.ToArray();
        }

        private static PurposeComposition ComposeUplink(PurposeCommand command, IDictionary<string, string> input)
        {
            string key = command.Key;
            byte afn = 0x0A;
            List<byte> body = new List<byte>();
            if (key.StartsWith("up.task.", StringComparison.Ordinal) || key.StartsWith("up.meter.", StringComparison.Ordinal))
            {
                bool meter = key.StartsWith("up.meter.", StringComparison.Ordinal);
                string prefix = meter ? "E00015" : "E00003";
                string di = prefix + (key.EndsWith(".count", StringComparison.Ordinal) ? "00" : key.EndsWith(".validity", StringComparison.Ordinal) ? "FF" : Int(input, "taskId").ToString("X2"));
                if (key.EndsWith(".depth", StringComparison.Ordinal) || key.EndsWith(".depthSet", StringComparison.Ordinal))
                {
                    di = (meter ? "E00115" : "E00103") + Int(input, "taskId").ToString("X2");
                }
                body.AddRange(new byte[] { 0, 0 }); body.AddRange(Hex.Identifier(di));
                if (key.EndsWith(".set", StringComparison.Ordinal))
                {
                    body.AddRange(TaskParameters(meter, input));
                }
                if (key.EndsWith(".depthSet", StringComparison.Ordinal))
                {
                    Bin(body, Int(input, "depth"), 2);
                }
            }
            else if (key == "up.parameter.set")
            {
                string parameter = input["parameter"];
                string di = parameter == "heartbeat" ? "E0000107" : parameter == "clock" ? "E0000130" : parameter == "apn" ? "E0000104" : parameter == "transport" ? "E000010A" : "E000010B";
                body.AddRange(new byte[] { 0, 0 }); body.AddRange(Hex.Identifier(di));
                if (parameter == "clock")
                {
                    body.AddRange(Clock(Time(input, "time"), true));
                }
                else if (parameter == "apn")
                {
                    if (input["value"].Length > 16 || input["value"].Any(character => character > 127))
                    {
                        throw new FormatException("APN需为最多16个ASCII字符。");
                    }
                    body.AddRange(Encoding.ASCII.GetBytes(input["value"].PadRight(16, '\0')));
                }
                else
                {
                    body.Add(parameter == "heartbeat" ? Bcd(Int(input, "value")) : (byte)Int(input, "value"));
                }
            }
            else
            {
                afn = key == "up.current" ? (byte)0x0C : key == "up.history" ? (byte)0x0D : key == "up.events" ? (byte)0x0E : key == "up.alarms" ? (byte)0x13 : (byte)0x0A;
                byte[][] groups = PointGroups(input["points"]);
                string[] ids = Identifiers(input["identifiers"]);
                bool timed = key == "up.history" || key == "up.events" || key == "up.alarms";
                byte[] range = new byte[0];
                if (timed)
                {
                    if (key == "up.events" && Int(input, "rangeMode") == 1)
                    {
                        range = Enumerable.Repeat((byte)255, 12).ToArray();
                    }
                    else
                    {
                        DateTime start = Time(input, "from");
                        DateTime end = Time(input, "to");
                        if (start.Second != 0 || end.Second != 0)
                        {
                            throw new FormatException("查询时间精确到分钟，请将秒设为00。");
                        }
                        if (start >= end)
                        {
                            throw new FormatException("结束时间需晚于开始时间；结束时刻不包含在范围内。");
                        }
                        range = RangeTime(start).Concat(RangeTime(end)).ToArray();
                    }
                }
                foreach (byte[] group in groups)
                {
                    foreach (string di in ids)
                    {
                        body.AddRange(group); body.AddRange(Hex.Identifier(di)); body.AddRange(range);
                        if (key == "up.history")
                        {
                            body.Add((byte)Int(input, "density"));
                        }
                    }
                }
            }
            if (command.ParameterOnly)
            {
                return Result(command, body.ToArray(), false, "参数明文（DA/DI/内容）；需整体加密并生成认证域后才能组成正式下发报文。");
            }
            return Result(command, FrameCodec.BuildUplink(0x4B, FixedHex(input["address"], 7, "终端通信地址"), afn,
                (byte)(0x60 | Int(input, "sequence")), body.ToArray()), true, "AFN=" + afn.ToString("X2") + "；读取请求，未添加可选PW/Tp。");
        }
    }
}
