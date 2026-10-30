using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FrameTools
{
    internal static class BusinessDecoder
    {
        private enum ValueFormat
        {
            Number,
            Date,
            Time,
            Clock
        }

        private sealed class MeterItem
        {
            public readonly string Name;
            public readonly int Length;
            public readonly int Decimals;
            public readonly string Unit;
            public readonly bool Signed;
            public readonly ValueFormat Format;

            public MeterItem(string name, int length, int decimals, string unit, bool signed = false, ValueFormat format = ValueFormat.Number)
            {
                Name = name;
                Length = length;
                Decimals = decimals;
                Unit = unit;
                Signed = signed;
                Format = format;
            }
        }

        private static readonly string[] EnergyNames =
        {
            "组合有功", "正向有功", "反向有功", "组合无功1", "组合无功2", "第一象限无功",
            "第二象限无功", "第三象限无功", "第四象限无功", "正向视在", "反向视在"
        };

        private static MeterItem MeterDefinition(string di, bool uplink)
        {
            uint id = uint.Parse(di, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int category = (int)(id >> 24);
            int group = (int)((id >> 16) & 255);
            int part = (int)((id >> 8) & 255);
            int period = (int)(id & 255);
            if (category == 0 && group < EnergyNames.Length && part <= 63 && period <= 12)
            {
                string prefix = period == 0 ? "当前" : "上" + period + "结算日";
                string tariff = part == 0 ? "总电能" : "费率" + part + "电能";
                return new MeterItem(prefix + EnergyNames[group] + tariff, 4, 2, group < 3 ? "kWh" : group < 9 ? "kvarh" : "kVAh");
            }
            if (category == 2 && period == 0)
            {
                if (group == 1 && part >= 1 && part <= (uplink ? 9 : 3))
                {
                    string[] names = { "", "A相电压", "B相电压", "C相电压", "AB线电压", "BC线电压", "CA线电压", "A/AB相基波电压", "B相基波电压", "C/CA相基波电压" };
                    return new MeterItem(names[part], 2, 1, "V");
                }
                if (group == 2 && part >= 1 && part <= (uplink ? 6 : 3))
                {
                    string[] names = { "", "A相电流", "B相电流", "C相电流", "A相基波电流", "B相基波电流", "C相基波电流" };
                    return new MeterItem(names[part], 3, 3, "A", true);
                }
                if (group >= 3 && group <= 6 && part <= 3)
                {
                    string phase = new[] { "总", "A相", "B相", "C相" }[part];
                    string name = group == 6 ? phase + "功率因数" : "瞬时" + phase + new[] { "有功功率", "无功功率", "视在功率" }[group - 3];
                    return new MeterItem(name, group == 6 ? 2 : 3, group == 6 ? 3 : 4,
                        group == 3 ? "kW" : group == 4 ? "kvar" : group == 5 ? "kVA" : "", true);
                }
                if (group == 7 && part >= 1 && part <= (uplink ? 6 : 3))
                {
                    string name = uplink ? new[] { "", "Ua/Uab相位角", "Ub相位角", "Uc相位角", "Ia相位角", "Ib相位角", "Ic相位角" }[part]
                        : new[] { "", "A相相角", "B相相角", "C相相角" }[part];
                    return new MeterItem(name, 2, 1, "°");
                }
                if ((group == 8 || group == 9) && part >= 1 && part <= 3)
                {
                    return new MeterItem(new[] { "", "A相", "B相", "C相" }[part] + (group == 8 ? "电压" : "电流") + "波形失真度", 2, 2, "%");
                }
            }
            if (category == 2 && (group == 10 || group == 11) && part >= 1 && part <= 3
                && (uplink ? period == 0 || period >= 2 && period <= 21 : period >= 1 && period <= 21))
            {
                string name = new[] { "", "A相", "B相", "C相" }[part] + (group == 10 ? "电压" : "电流")
                    + (period == 0 ? "总谐波含有率" : period + "次谐波" + (uplink ? "含有率" : "含量"));
                return new MeterItem(name, 2, 2, "%");
            }
            if (category == 2 && group == 0x80 && part == 0 && period >= 1 && period <= 11)
            {
                string[] names = { "", "零线电流", "电网频率", "一分钟有功平均功率", "当前有功需量", "当前无功需量",
                    "当前视在需量", "表内温度", "时钟电池电压", "备用电池电压", "内部电池工作时间", "当前阶梯电价" };
                int[] lengths = { 0, 3, 2, 3, 3, 3, 3, 2, 2, 2, 4, 4 };
                int[] decimals = { 0, 3, 2, 4, 4, 4, 4, 1, 2, 2, 0, 4 };
                string[] units = { "", "A", "Hz", "kW", "kW", "kvar", "kVA", "℃", "V", "V", "分钟", "元/kWh" };
                return new MeterItem(names[period], lengths[period], decimals[period], units[period], period == 1 || period >= 3 && period <= 7);
            }
            if (di == "04000101")
            {
                return new MeterItem("日期及星期", 4, 0, "", false, ValueFormat.Date);
            }
            if (di == "04000102")
            {
                return new MeterItem("时间", 3, 0, "", false, ValueFormat.Time);
            }
            return null;
        }

        private static string[] MeterBlock(string di)
        {
            byte[] id = Hex.Parse(di);
            if (id[0] == 0 && id[1] < EnergyNames.Length && id[2] == 255 && id[3] <= 12)
            {
                return Enumerable.Range(0, 64).Select(n => "00" + id[1].ToString("X2") + n.ToString("X2") + id[3].ToString("X2")).ToArray();
            }
            if (id[0] == 2 && id[1] >= 1 && id[1] <= 9 && id[2] == 255 && id[3] == 0)
            {
                int first = id[1] >= 3 && id[1] <= 6 ? 0 : 1;
                return Enumerable.Range(first, first == 0 ? 4 : 3).Select(n => "02" + id[1].ToString("X2") + n.ToString("X2") + "00").ToArray();
            }
            return null;
        }

        private static string MeterName(string di, bool uplink)
        {
            MeterItem item = MeterDefinition(di, uplink);
            if (item != null)
            {
                return item.Name;
            }
            string[] block = MeterBlock(di);
            return block == null ? "未收录的数据项" : MeterDefinition(block[0], uplink).Name + "等数据块";
        }

        public static string DescribeMeterDi(string di, bool uplink)
        {
            return di + " — " + MeterName(di, uplink);
        }

        public static string DecodeMeter(FrameResult result, int offset, string di, byte[] data, bool uplink)
        {
            MeterItem item = MeterDefinition(di, uplink);
            if (item != null)
            {
                return DecodeValue(result, offset, di, data, item);
            }
            string[] block = uplink ? null : MeterBlock(di);
            if (block != null)
            {
                int size = MeterDefinition(block[0], false).Length;
                bool variable = di.StartsWith("00", StringComparison.Ordinal);
                int count = data.Length / size;
                if (data.Length % size != 0 || count == 0 || (variable ? count > block.Length : count != block.Length))
                {
                    return Issue(result, di, "数据块长度不符，原始值保留");
                }
                List<string> values = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    values.Add(DecodeValue(result, offset + i * size, block[i], data.Skip(i * size).Take(size).ToArray(), MeterDefinition(block[i], false)));
                }
                return string.Join("；", values);
            }
            return "该数据项或数据块布局未实现解码，原始值保留";
        }

        private static string DecodeValue(FrameResult result, int offset, string di, byte[] data, MeterItem item)
        {
            if (data.Length != item.Length)
            {
                return Issue(result, di, "数据长度不符：期望 " + item.Length + " 字节，实际 " + data.Length + " 字节");
            }
            if (data.All(b => b == 0xFF))
            {
                return Issue(result, di, "数据缺失（全 FF），不换算数值");
            }
            string value;
            if (item.Format == ValueFormat.Date)
            {
                string date;
                if (!TryClock(data.Skip(1).ToArray(), out date) || data[0] > 6)
                {
                    return Issue(result, di, "无效 BCD 日期或星期");
                }
                value = date + " 星期" + new[] { "日", "一", "二", "三", "四", "五", "六" }[data[0]];
            }
            else if (item.Format == ValueFormat.Time)
            {
                if (!TryTime(data, out value))
                {
                    return Issue(result, di, "无效 BCD 时间");
                }
            }
            else if (item.Format == ValueFormat.Clock)
            {
                if (!TryClock(data, out value))
                {
                    return Issue(result, di, "无效 BCD 日期时间");
                }
            }
            else
            {
                decimal number;
                if (!TryBcd(data, item.Signed, out number))
                {
                    return Issue(result, di, "含无效 BCD，未换算数值");
                }
                for (int i = 0; i < item.Decimals; i++)
                {
                    number /= 10;
                }
                value = number.ToString("F" + item.Decimals, CultureInfo.InvariantCulture) + (item.Unit.Length > 0 ? " " + item.Unit : "");
            }
            result.Add(offset, data.Length, item.Name, value);
            return item.Name + "：" + value;
        }

        private static bool TryBcd(byte[] data, bool signed, out decimal number)
        {
            number = 0;
            bool negative = signed && (data[data.Length - 1] & 0x80) != 0;
            for (int i = data.Length - 1; i >= 0; i--)
            {
                int value = signed && i == data.Length - 1 ? data[i] & 0x7F : data[i];
                if ((value & 15) > 9 || (value >> 4) > 9)
                {
                    return false;
                }
                number = number * 100 + (value >> 4) * 10 + (value & 15);
            }
            if (negative)
            {
                number = -number;
            }
            return true;
        }

        private static bool TryClock(byte[] data, out string value)
        {
            value = "";
            decimal number;
            if ((data.Length != 3 && data.Length != 5 && data.Length != 6) || !TryBcd(data, false, out number))
            {
                return false;
            }
            string digits = Hex.ReverseDisplay(data);
            int year = int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture);
            int month = int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture);
            int day = int.Parse(digits.Substring(4, 2), CultureInfo.InvariantCulture);
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2000 + year, month))
            {
                return false;
            }
            value = digits.Substring(0, 2) + "-" + digits.Substring(2, 2) + "-" + digits.Substring(4, 2);
            if (data.Length > 3)
            {
                string time;
                byte[] clock = data.Length == 6 ? data.Take(3).ToArray() : new byte[] { 0, data[0], data[1] };
                if (!TryTime(clock, out time))
                {
                    return false;
                }
                value += " " + (data.Length == 6 ? time : time.Substring(0, 5));
            }
            return true;
        }

        private static bool TryTime(byte[] data, out string value)
        {
            value = "";
            decimal number;
            if (data.Length != 3 || !TryBcd(data, false, out number))
            {
                return false;
            }
            string digits = Hex.ReverseDisplay(data);
            if (int.Parse(digits.Substring(0, 2), CultureInfo.InvariantCulture) > 23
                || int.Parse(digits.Substring(2, 2), CultureInfo.InvariantCulture) > 59
                || int.Parse(digits.Substring(4, 2), CultureInfo.InvariantCulture) > 59)
            {
                return false;
            }
            value = digits.Substring(0, 2) + ":" + digits.Substring(2, 2) + ":" + digits.Substring(4, 2);
            return true;
        }

        public static string Describe645Error(byte error)
        {
            string[] names = { "其他错误", "无请求数据", "密码错/未授权", "通信速率不能更改", "年时区数超", "日时段数超", "费率数超", "保留位" };
            List<string> set = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                if ((error & (1 << i)) != 0)
                {
                    set.Add("Bit" + i + "：" + names[i]);
                }
            }
            return "减33H后 " + error.ToString("X2") + "；" + (set.Count == 0 ? "未置错误标志" : string.Join("；", set));
        }

        public static string Decode645Command(FrameResult result, int offset, byte control, byte[] data)
        {
            int function = control & 31;
            if (function == 8 && data.Length == 6)
            {
                return "广播校时：" + AddClock(result, offset, data, "广播校时时间");
            }
            if (function == 0x15 && data.Length == 6)
            {
                string address = Hex.ReverseDisplay(data);
                result.Add(offset, 6, "写入通信地址", address);
                return "写入通信地址 " + address;
            }
            if (function == 0x1D && data.Length == 1)
            {
                string[] modes = { "时钟秒脉冲", "需量周期", "时段投切" };
                string mode = data[0] < modes.Length ? modes[data[0]] : "保留输出类别 " + data[0];
                result.Add(offset, 1, "多功能端子输出类别", mode);
                return mode;
            }
            return "按控制功能组织的数据，业务布局未解码，原始值保留";
        }

        public static string DescribeLocalControl(byte control)
        {
            return "DIR=" + ((control >> 7) & 1) + ((control & 0x80) == 0 ? "（下行）" : "（上行）")
                + "；PRM=" + ((control >> 6) & 1) + ((control & 0x40) != 0 ? "（启动站）" : "（从动站）")
                + "；ADD=" + ((control >> 5) & 1) + ((control & 0x20) != 0 ? "（有源/目的地址）" : "（无地址域）")
                + "；VER=" + ((control >> 3) & 3) + "（本标准为0）；低3位保留=" + (control & 7);
        }

        public static string DescribeUplinkControl(byte control)
        {
            bool primary = (control & 0x40) != 0;
            bool down = (control & 0x80) == 0;
            int function = control & 15;
            string service;
            if (primary)
            {
                service = function == 1 ? "复位命令（发送/确认）" : function == 4 ? "用户数据（发送/无回答）"
                    : function == 9 ? "链路测试" : function == 10 ? "请求1级数据" : function == 11 ? "请求2级数据" : "备用功能";
            }
            else
            {
                service = function == 0 ? "认可（确认）" : function == 8 ? "用户数据（响应）"
                    : function == 9 ? "否定：无所召唤的数据" : function == 11 ? "链路状态" : "备用功能";
            }
            string flags = down ? "FCB=" + ((control >> 5) & 1) + "（帧计数位）；FCV=" + ((control >> 4) & 1)
                + ((control & 0x10) != 0 ? "（FCB有效）" : "（FCB无效）")
                : "ACD=" + ((control >> 5) & 1) + ((control & 0x20) != 0 ? "（有告警数据等待访问）" : "（无告警数据等待访问）")
                    + "；D4保留=" + ((control >> 4) & 1);
            return "DIR=" + ((control >> 7) & 1) + (down ? "（主站下行）" : "（终端上行）")
                + "；PRM=" + ((control >> 6) & 1) + (primary ? "（启动站）" : "（从动站）")
                + "；" + flags + "；链路功能=" + function + "（" + service + "）";
        }

        public static string DescribeSequence(byte control, byte seq)
        {
            string[] frames = { "多帧中间帧", "多帧结束帧", "多帧首帧，有后续帧", "单帧" };
            return "TpV=" + ((seq >> 7) & 1) + ((seq & 0x80) == 0 ? "（无时间标签）" : "（有时间标签）")
                + "；FIR=" + ((seq >> 6) & 1) + "；FIN=" + ((seq >> 5) & 1) + "（" + frames[(seq >> 5) & 3] + "）"
                + "；CON=" + ((seq >> 4) & 1) + ((seq & 0x10) == 0 ? "（无需确认）" : "（需要确认）")
                + "；" + ((control & 0x40) != 0 ? "PSEQ启动帧序号=" : "RSEQ响应帧序号=") + (seq & 15);
        }

        public static string DescribeTimeTag(byte[] data)
        {
            string time;
            decimal day;
            if (!TryTime(data.Take(3).ToArray(), out time) || !TryBcd(data.Skip(3).Take(1).ToArray(), false, out day) || day < 1 || day > 31)
            {
                return "无效 BCD 日时分秒，原始时间标签保留";
            }
            return day + "日 " + time + "；允许传输延时 " + data[4] + " 分钟"
                + (data[4] == 0 ? "（不判传输延时）" : "") + "；未验证时效性";
        }

        private static string Issue(FrameResult result, string di, string reason)
        {
            result.Notes.Add("DI " + di + "：" + reason + "，原始值保留。");
            return reason;
        }

        public static string DescribeLocalDi(string di)
        {
            byte[] id = Hex.Parse(di);
            string name = ProtocolCatalog.Name(ProtocolKind.CsgLocal, id[2], true, di)
                ?? ProtocolCatalog.Name(ProtocolKind.CsgLocal, id[2], false, di);
            string peer = id[0] == 0xE8 ? "集中器与本地模块" : id[0] == 0xEA ? "采集器与本地模块" : "未定义通信双方类型";
            string[] types = { "下行无数据，上行有数据", "双向数据格式一致", "仅下行，回复确认/否认",
                "下行请求，对应04上行", "上行返回，对应03下行", "仅上行，下行确认/否认", "上行无数据，下行有数据" };
            return di + " — " + (name ?? "未收录的数据项") + "；" + peer + "；"
                + (id[1] < types.Length ? types[id[1]] : "未定义数据方向类型");
        }

        public static string DecodeLocal(FrameResult result, int offset, byte control, byte afn, string di, byte[] data)
        {
            byte[] id = Hex.Parse(di);
            if (id[2] != afn)
            {
                return "AFN 与 DI1 不一致，保留数据而不解码";
            }
            bool up = (control & 0x80) != 0;
            string name = ProtocolCatalog.Name(ProtocolKind.CsgLocal, afn, up, di);
            if (name == null)
            {
                return "当前规约未定义此数据项或方向，原始数据保留";
            }
            bool noData = id[1] == 0 && !up || id[1] == 6 && up
                || di == "E8020101" || di == "E8020102" || di == "E8020103" || di == "E8020208" || di == "E8020209";
            if (noData)
            {
                return data.Length == 0 ? name + "；无数据内容（请求/命令）" : Issue(result, di, "该请求/命令应无数据内容");
            }
            if ((id[1] == 2 || id[1] == 3) && up || (id[1] == 4 || id[1] == 5) && !up)
            {
                return Issue(result, di, "DI 的上下行类型与控制域方向不符，未解码");
            }
            switch (di)
            {
                case "E8010001":
                case "EA010001":
                    return Exact(result, di, data, 2) ? name + "；等待时间：" + AddBin(result, offset, data, 0, 2, "等待时间", "秒") : LengthMessage(data, 2);
                case "E8010002":
                case "EA010002":
                {
                    if (!Exact(result, di, data, 1))
                    {
                        return LengthMessage(data, 1);
                    }
                    string[] errors = { "通信超时", "无效数据标识内容", "长度错误", "校验错误", "数据标识编码不存在", "格式错误",
                        "表号重复", "表号不存在", "电表应用层无应答", "主节点忙", "主节点不支持此命令", "从节点不应答",
                        "从节点不在网内", "剩余可分配任务数不足", "上报任务不存在", "任务ID重复", "查询任务不存在", "任务ID不存在" };
                    string reason = data[0] < errors.Length ? errors[data[0]] : data[0] == 255 ? "其他错误" : "保留错误码";
                    result.Add(offset, 1, "错误状态字", data[0] + " — " + reason);
                    return "否认：" + reason;
                }
                case "E8000203":
                case "E8000206":
                case "E8000305":
                case "E8000309":
                {
                    if (!Exact(result, di, data, 2))
                    {
                        return LengthMessage(data, 2);
                    }
                    string label = di == "E8000203" ? "未完成任务数" : di == "E8000206" ? "剩余可分配任务数"
                        : di == "E8000305" ? "从节点总数量" : "映射表从节点数量";
                    return label + "：" + AddBin(result, offset, data, 0, 2, label, "个");
                }
                case "E8000301":
                case "EA002301":
                    return Exact(result, di, data, 9) ? DecodeVersion(result, offset, data) : LengthMessage(data, 9);
                case "E8000302":
                    return Exact(result, di, data, 30) ? DecodeModuleMode(result, offset, data) : LengthMessage(data, 30);
                case "E8000303":
                case "E8020401":
                case "E8030308":
                    if (!Exact(result, di, data, 6))
                    {
                        return LengthMessage(data, 6);
                    }
                    return AddAddress(result, offset, data, 0, di == "E8030308" ? "从节点地址" : "主节点地址");
                case "E8030306":
                case "E8030204":
                    if (!Exact(result, di, data, 3))
                    {
                        return LengthMessage(data, 3);
                    }
                    return "起始序号：" + AddBin(result, offset, data, 0, 2, "起始序号", "")
                        + "；查询数量：" + AddBin(result, offset, data, 2, 1, "查询数量", "个");
                case "E8040306":
                {
                    if (data.Length < 3)
                    {
                        return Issue(result, di, "节点列表长度不足");
                    }
                    int expected = 3 + data[2] * 6;
                    if (!Exact(result, di, data, expected))
                    {
                        return LengthMessage(data, expected);
                    }
                    string total = AddBin(result, offset, data, 0, 2, "从节点总数量", "个");
                    AddBin(result, offset, data, 2, 1, "本次应答数量", "个");
                    for (int i = 0; i < data[2]; i++)
                    {
                        AddAddress(result, offset, data, 3 + i * 6, "从节点" + (i + 1) + "地址");
                    }
                    return "从节点总数 " + total + "；本次返回 " + data[2] + " 个地址";
                }
                case "E8000307":
                case "E80403A0":
                case "E80204A0":
                case "E8020404":
                case "EA062105":
                case "EA063105":
                case "EA062501":
                {
                    if (!Exact(result, di, data, 1))
                    {
                        return LengthMessage(data, 1);
                    }
                    string label = di == "E8000307" ? "主动注册状态" : di == "E8020404" ? "事件上报状态"
                        : di == "EA062501" ? "设备类型" : di.StartsWith("EA", StringComparison.Ordinal) ? "探测状态" : "即插即用开关";
                    string[] states = di == "E8000307" ? new[] { "停止主动注册", "正在主动注册" }
                        : di == "E8020404" ? new[] { "禁止上报", "允许上报" }
                        : di == "EA062501" ? new[] { "采集器", "转换器" }
                        : di.StartsWith("EA", StringComparison.Ordinal) ? new[] { "完成探测", "正在探测" } : new[] { "关闭", "开启" };
                    string state = data[0] < states.Length ? states[data[0]] : "保留值 " + data[0];
                    result.Add(offset, 1, label, state);
                    return label + "：" + state;
                }
                case "E800030B":
                {
                    if (!Exact(result, di, data, 8))
                    {
                        return LengthMessage(data, 8);
                    }
                    List<string> values = new List<string>();
                    for (int i = 0; i < 4; i++)
                    {
                        values.Add("优先级" + i + "：" + AddBin(result, offset, data, i * 2, 2, "优先级" + i + "建议超时", "秒"));
                    }
                    return string.Join("；", values);
                }
                case "E8040308":
                    if (!Exact(result, di, data, 13))
                    {
                        return LengthMessage(data, 13);
                    }
                    AddAddress(result, offset, data, 0, "从节点地址");
                    AddAddress(result, offset, data, 6, "父节点地址");
                    result.Add(offset + 12, 1, "链路质量", data[12] + "（0最差，31最佳）");
                    return "父节点：" + Hex.ReverseDisplay(data.Skip(6).Take(6).ToArray()) + "；链路质量 " + data[12];
                case "E8030304":
                case "E8040304":
                {
                    int expected = di == "E8030304" ? 7 : 9;
                    if (!Exact(result, di, data, expected))
                    {
                        return LengthMessage(data, expected);
                    }
                    AddAddress(result, offset, data, 0, "通信目的地址");
                    string delay = expected == 9 ? AddBin(result, offset, data, 6, 2, "通信延时时长", "秒") : "查询";
                    AddBin(result, offset, data, expected - 1, 1, "报文长度", "字节");
                    return "通信延时：" + delay + "；报文长度 " + data[expected - 1] + " 字节";
                }
                case "E8060601":
                case "E80204A3":
                    if (!Exact(result, di, data, 6))
                    {
                        return LengthMessage(data, 6);
                    }
                    return AddClock(result, offset, data, di == "E80204A3" ? "CCO时间" : "集中器时间");
                case "E8020202":
                case "E8030205":
                    return Exact(result, di, data, 2) ? "任务ID：" + AddBin(result, offset, data, 0, 2, "任务ID", "") : LengthMessage(data, 2);
                case "E8050505":
                {
                    if (!Exact(result, di, data, 9))
                    {
                        return LengthMessage(data, 9);
                    }
                    string task = AddBin(result, offset, data, 0, 2, "任务ID", "");
                    AddAddress(result, offset, data, 2, "从节点地址");
                    string state = data[8] == 0 ? "成功" : data[8] == 1 ? "从节点无响应" : data[8] == 2 ? "数据不合法"
                        : data[8] == 255 ? "其他错误" : "保留状态 " + data[8];
                    result.Add(offset + 8, 1, "任务状态", state);
                    return "任务ID " + task + "；" + state;
                }
                case "EA062101":
                case "EA063101":
                    return Exact(result, di, data, 1) ? "地址数量：" + AddBin(result, offset, data, 0, 1, "地址数量", "个") : LengthMessage(data, 1);
            }
            string xmlMeaning;
            int consumed;
            if (ProtocolCatalog.TryDecode(result, offset, ProtocolKind.CsgLocal, afn, up, di, data, false, out xmlMeaning, out consumed))
            {
                return xmlMeaning;
            }
            return name + "；" + (data.Length == 0 ? "无数据内容" : "复杂业务布局未解码，原始数据保留");
        }

        private static bool Exact(FrameResult result, string di, byte[] data, int expected)
        {
            if (data.Length == expected)
            {
                return true;
            }
            Issue(result, di, LengthMessage(data, expected));
            return false;
        }

        private static string LengthMessage(byte[] data, int expected)
        {
            return "数据长度不符：期望 " + expected + " 字节，实际 " + data.Length + " 字节；原始值保留";
        }

        private static string AddBin(FrameResult result, int offset, byte[] data, int start, int count, string name, string unit)
        {
            int number = 0;
            for (int i = count - 1; i >= 0; i--)
            {
                number = number * 256 + data[start + i];
            }
            string value = number.ToString(CultureInfo.InvariantCulture) + (unit.Length > 0 ? " " + unit : "");
            result.Add(offset + start, count, name, value);
            return value;
        }

        private static string AddAddress(FrameResult result, int offset, byte[] data, int start, string name)
        {
            string value = Hex.ReverseDisplay(data.Skip(start).Take(6).ToArray());
            result.Add(offset + start, 6, name, value + "（BIN地址，低字节先传）");
            return value;
        }

        private static string AddClock(FrameResult result, int offset, byte[] data, string name)
        {
            string value;
            if (!TryClock(data, out value))
            {
                value = "无效 BCD 日期时间，原始值保留";
            }
            result.Add(offset, data.Length, name, value);
            return value;
        }

        private static string DecodeVersion(FrameResult result, int offset, byte[] data)
        {
            string vendor = Ascii(data.Take(2).ToArray());
            string chip = Ascii(data.Skip(2).Take(2).ToArray());
            result.Add(offset, 2, "厂商代码", vendor);
            result.Add(offset + 2, 2, "芯片代码", chip);
            string date = AddClock(result, offset + 4, data.Skip(4).Take(3).ToArray(), "版本日期");
            byte[] bytes = data.Skip(7).Take(2).ToArray();
            decimal ignored;
            string version = TryBcd(bytes, false, out ignored) ? Hex.ReverseDisplay(bytes) : "无效 BCD，原始值保留";
            result.Add(offset + 7, 2, "版本号", version);
            return "厂商 " + vendor + "；芯片 " + chip + "；版本日期 " + date + "；版本 " + version;
        }

        private static string DecodeModuleMode(FrameResult result, int offset, byte[] data)
        {
            int mode = data[0];
            string[] modes = { "保留", "窄带电力线载波", "宽带电力线载波", "微功率无线", "窄带+微功率无线", "宽带+微功率无线" };
            string name = mode < modes.Length ? modes[mode] : "保留通信方式 " + mode;
            result.Add(offset, 1, "本地通信方式", name);
            AddBin(result, offset, data, 1, 2, "最大协议报文长度", "字节");
            AddBin(result, offset, data, 3, 2, "最大文件分包长度", "字节");
            AddBin(result, offset, data, 5, 1, "升级等待时间", "分钟");
            AddAddress(result, offset, data, 6, "主节点地址");
            AddBin(result, offset, data, 12, 2, "最大从节点数量", "个");
            string count = AddBin(result, offset, data, 14, 2, "当前从节点数量", "个");
            AddBin(result, offset, data, 16, 2, "单次读写最大数量", "个");
            AddClock(result, offset + 18, data.Skip(18).Take(3).ToArray(), "接口协议发布日期");
            DecodeVersion(result, offset + 21, data.Skip(21).ToArray());
            return name + "；当前从节点 " + count + "；其余能力参数见展开字段";
        }

        private static string Ascii(byte[] bytes)
        {
            return bytes.Any(b => b != 0 && (b < 32 || b > 126)) ? "非 ASCII 字节：" + Hex.Format(bytes)
                : Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        private static MeterItem UplinkDefinition(byte afn, string di)
        {
            if (afn == 0x0C)
            {
                MeterItem meter = MeterDefinition(di, true);
                if (meter != null)
                {
                    return meter;
                }
                switch (di)
                {
                    case "E1800002": return new MeterItem("最大脉冲量路数", 1, 0, "路");
                    case "E1800003": return new MeterItem("串行通信端口数量", 1, 0, "个");
                    case "E1800004": return new MeterItem("支持最大测量点数", 2, 0, "个");
                    case "E1800005": return new MeterItem("测量点总数", 2, 0, "个");
                    case "E1800006": return new MeterItem("终端普通用户表总数", 2, 0, "个");
                    case "E1800007": return new MeterItem("终端多功能表总数", 2, 0, "个");
                    case "E1800008": return new MeterItem("终端重点户电能表总数", 2, 0, "个");
                }
            }
            if (afn == 0x0A)
            {
                switch (di)
                {
                    case "E0000107": return new MeterItem("心跳周期", 1, 0, "分钟");
                    case "E0000108": return new MeterItem("掉线重拨间隔", 1, 0, "分钟");
                    case "E0000109": return new MeterItem("掉线后重拨次数", 1, 0, "次");
                    case "E0000130": return new MeterItem("终端时间", 6, 0, "", false, ValueFormat.Clock);
                }
            }
            return null;
        }

        private static string UplinkName(byte afn, string di)
        {
            MeterItem definition = UplinkDefinition(afn, di) ?? UplinkDefinition(0x0A, di) ?? UplinkDefinition(0x0C, di);
            if (definition != null)
            {
                return definition.Name;
            }
            switch (di)
            {
                case "E0000000": return "全部确认/否定";
                case "E0001000": return "终端登录";
                case "E0001001": return "终端心跳";
                case "E0001002": return "登录退出";
                case "E0001003": return "心跳时标设置";
                case "E0000100": return "主站通信地址";
                case "E0000101": return "备用主站通信地址1";
                case "E0000102": return "备用主站通信地址2";
                case "E0000103": return "网关或代理服务器地址";
                case "E0000104": return "APN";
                case "E0000105": return "APN用户名";
                case "E0000106": return "APN密码";
                case "E000010A": return "TCP/UDP通信方式";
                case "E000010B": return "通信压缩方式";
            }
            return ProtocolCatalog.Name(ProtocolKind.CsgUplink, afn, true, di)
                ?? ProtocolCatalog.Name(ProtocolKind.CsgUplink, afn, false, di) ?? MeterName(di, true);
        }

        public static string DecodeUplinkFirst(FrameResult result, int offset, int length, byte control, byte afn)
        {
            if (length < 6 || (afn != 0 && afn != 2 && afn != 0x0A && afn != 0x0C))
            {
                return "复合或安全业务布局，原始字节保留";
            }
            byte[] bytes = result.Bytes;
            string di = Hex.ReverseDisplay(bytes.Skip(offset + 2).Take(4).ToArray());
            string name = UplinkName(afn, di);
            result.Add(offset, 2, "首个信息点 DA", FrameCodec.DescribeDa(bytes[offset], bytes[offset + 1]));
            result.Add(offset + 2, 4, "首个数据标识 DI", di + " — " + name + "（DI0先传，不做±33H）");
            if ((afn == 0x0A || afn == 0x0C) && (control & 0x80) == 0)
            {
                return "读取请求：" + name + "；请求中的后续 DA/DI 保留原值，不当作应答数据";
            }
            byte[] data = bytes.Skip(offset + 6).Take(length - 6).ToArray();
            result.Add(offset + 6, data.Length, "首个数据内容", "原始值保留");
            FrameField content = result.Fields.Last();
            string meaning;
            MeterItem item = UplinkDefinition(afn, di);
            if (afn == 0 && data.Length >= 1)
            {
                meaning = data[0] == 0 ? (di == "E0000000" ? "全部肯定" : "肯定") : data[0] == 1
                    ? (di == "E0000000" ? "全部否定" : "否定") : "保留确认码 " + data[0];
                result.Add(offset + 6, 1, "确认结果", meaning);
                content.Length = 1;
                content.Raw = Hex.Format(data.Take(1).ToArray());
            }
            else if (afn == 2 && di == "E0001000")
            {
                if (!Exact(result, di, data, 9))
                {
                    meaning = LengthMessage(data, 9);
                }
                else
                {
                    decimal ignored;
                    result.Add(offset + 6, 2, "规约版本号", TryBcd(data.Take(2).ToArray(), false, out ignored) ? Hex.ReverseDisplay(data.Take(2).ToArray()) : "无效 BCD");
                    result.Add(offset + 8, 1, "安全模块标志", data[2] == 0 ? "不具备安全模块" : data[2] == 1 ? "具备安全模块，需身份认证" : "保留值 " + data[2]);
                    meaning = "终端登录；时间 " + AddClock(result, offset + 9, data.Skip(3).ToArray(), "终端时间");
                }
            }
            else if (afn == 2 && (di == "E0001001" || di == "E0001002"))
            {
                meaning = di == "E0001001" && data.Length == 6 ? "终端心跳；时间 " + AddClock(result, offset + 6, data, "终端时间")
                    : data.Length == 0 ? name + "；无数据内容" : "可变长度链路数据不匹配，原始值保留";
            }
            else if (item != null && data.Length >= item.Length)
            {
                meaning = DecodeValue(result, offset + 6, di, data.Take(item.Length).ToArray(), item);
                content.Length = item.Length;
                content.Raw = Hex.Format(data.Take(item.Length).ToArray());
                if (data.Length > item.Length)
                {
                    meaning += "；其余 " + (data.Length - item.Length) + " 字节保留，不推断后续信息体或认证域";
                }
            }
            else if (afn == 0x0A && (di == "E0000104" || di == "E0000105" || di == "E0000106"))
            {
                int size = di == "E0000104" ? 16 : 32;
                meaning = data.Length < size ? Issue(result, di, "ASCII 数据长度不足") : Ascii(data.Take(size).ToArray());
                if (data.Length >= size)
                {
                    result.Add(offset + 6, size, name, meaning);
                    content.Length = size;
                    content.Raw = Hex.Format(data.Take(size).ToArray());
                    meaning = name + "：" + meaning + (data.Length > size ? "；其余字节保留，不推断认证域" : "");
                }
            }
            else if (afn == 0x0A && data.Length >= 1 && (di == "E000010A" || di == "E000010B" || di == "E0001003"))
            {
                string[] states = di == "E000010A" ? new[] { "TCP", "UDP" } : di == "E000010B"
                    ? new[] { "非压缩", "压缩" } : new[] { "心跳不带时标", "心跳带时标" };
                string state = data[0] < states.Length ? states[data[0]] : "保留值 " + data[0];
                result.Add(offset + 6, 1, name, state);
                content.Length = 1;
                content.Raw = Hex.Format(data.Take(1).ToArray());
                meaning = name + "：" + state;
                if (data.Length > 1)
                {
                    meaning += "；其余字节保留，不推断认证域";
                }
            }
            else
            {
                string xmlMeaning;
                int consumed;
                if (item != null)
                {
                    meaning = Issue(result, di, LengthMessage(data, item.Length));
                }
                else if ((afn == 0x0A || afn == 0x0C) && ProtocolCatalog.TryDecode(result, offset + 6,
                    ProtocolKind.CsgUplink, afn, (control & 0x80) != 0, di, data, true, out xmlMeaning, out consumed))
                {
                    meaning = xmlMeaning;
                    if (consumed > 0)
                    {
                        content.Length = consumed;
                        content.Raw = Hex.Format(data.Take(consumed).ToArray());
                        if (data.Length > consumed)
                        {
                            meaning += "；其余 " + (data.Length - consumed) + " 字节保留，不推断后续信息体或认证域";
                        }
                    }
                }
                else
                {
                    meaning = name + "；业务布局未解码，原始值保留";
                }
            }
            content.Meaning = meaning;
            return "首个信息体：" + meaning;
        }
    }
}
