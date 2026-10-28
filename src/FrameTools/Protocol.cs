using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FrameTools
{
    public enum ProtocolKind
    {
        Dlt645,
        CsgLocal,
        CsgUplink
    }

    public sealed class FrameField
    {
        public int Offset { get; set; }
        public int Length { get; set; }
        public string Name { get; set; }
        public string Raw { get; set; }
        public string Meaning { get; set; }
    }

    public sealed class FrameResult
    {
        public FrameResult(byte[] bytes)
        {
            Bytes = bytes;
            Fields = new List<FrameField>();
            Errors = new List<string>();
            Notes = new List<string>();
        }

        public byte[] Bytes { get; private set; }
        public List<FrameField> Fields { get; private set; }
        public List<string> Errors { get; private set; }
        public List<string> Notes { get; private set; }
        public bool IsFrameValid { get { return Errors.Count == 0; } }

        public void Add(int offset, int length, string name, string meaning)
        {
            Fields.Add(new FrameField
            {
                Offset = offset,
                Length = length,
                Name = name,
                Raw = Hex.Format(Bytes.Skip(offset).Take(length).ToArray()),
                Meaning = meaning
            });
        }
    }

    public static class Hex
    {
        public static byte[] Parse(string text)
        {
            string value = Regex.Replace(text ?? "", @"\b0[xX]", "");
            value = Regex.Replace(value, @"[\s,:\-]", "");
            if (!Regex.IsMatch(value, @"\A[0-9a-fA-F]*\z"))
            {
                throw new FormatException("请输入十六进制字节；可使用空白、逗号、冒号或短横线分隔。日志时间戳需先移除。");
            }
            if (value.Length % 2 != 0)
            {
                throw new FormatException("十六进制字符数为奇数，存在不完整的字节。");
            }
            byte[] bytes = new byte[value.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(value.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return bytes;
        }

        public static string Format(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace('-', ' ');
        }

        public static string ReverseDisplay(byte[] bytes)
        {
            return string.Concat(bytes.Reverse().Select(b => b.ToString("X2")));
        }

        public static byte Sum(byte[] bytes, int offset, int length)
        {
            int sum = 0;
            for (int i = offset; i < offset + length; i++)
            {
                sum += bytes[i];
            }
            return (byte)(sum & 0xFF);
        }

        public static byte[] Identifier(string text)
        {
            byte[] bytes = Parse(text);
            if (bytes.Length != 4)
            {
                throw new FormatException("DI 必须为 4 字节，按 DI3…DI0 输入，例如 00010000 或 E8000301。");
            }
            Array.Reverse(bytes);
            return bytes;
        }
    }

    public static class FrameCodec
    {
        public static FrameResult Parse(ProtocolKind protocol, string text)
        {
            byte[] bytes = Hex.Parse(text);
            FrameResult result = new FrameResult(bytes);
            int start = 0;
            if (protocol != ProtocolKind.CsgLocal)
            {
                while (start < bytes.Length && bytes[start] == 0xFE)
                {
                    start++;
                }
                if (start > 0)
                {
                    result.Add(0, start, "前导字节", "不计入帧长度和校验和");
                    if (start > 4)
                    {
                        result.Notes.Add("前导 FE 超过 4 字节，请核对采集方式。");
                    }
                }
            }
            int minimum = protocol == ProtocolKind.Dlt645 ? 12 : protocol == ProtocolKind.CsgLocal ? 12 : 18;
            if (bytes.Length - start < minimum)
            {
                result.Errors.Add("帧不完整：当前协议至少需要 " + minimum + " 字节（不含前导 FE）。");
                return result;
            }
            if (bytes[start] != 0x68)
            {
                result.Errors.Add("起始字节应为 68。");
            }
            if (bytes[bytes.Length - 1] != 0x16)
            {
                result.Errors.Add("结束字节应为 16。");
            }
            result.Add(start, 1, "起始符", "68H");
            if (protocol == ProtocolKind.Dlt645)
            {
                Parse645(result, start);
            }
            else if (protocol == ProtocolKind.CsgLocal)
            {
                ParseLocal(result);
            }
            else
            {
                ParseUplink(result, start);
            }
            result.Add(bytes.Length - 1, 1, "结束符", "16H");
            return result;
        }

        private static void Checksum(FrameResult result, int start)
        {
            int position = result.Bytes.Length - 2;
            byte expected = Hex.Sum(result.Bytes, start, position - start);
            bool matches = expected == result.Bytes[position];
            result.Add(position, 1, "校验和 CS", (matches ? "通过" : "失败") + "；计算值 " + expected.ToString("X2"));
            if (!matches)
            {
                result.Errors.Add("校验和错误：报文 " + result.Bytes[position].ToString("X2") + "，计算值 " + expected.ToString("X2") + "。");
            }
        }

        private static void Parse645(FrameResult r, int s)
        {
            byte[] b = r.Bytes;
            byte control = b[s + 8];
            int function = control & 0x1F;
            int dataLength = b[s + 9];
            int actualLength = b.Length - s;
            string address = Hex.ReverseDisplay(b.Skip(s + 1).Take(6).ToArray());
            r.Add(s + 1, 6, "通信地址 A", address + "（低字节先传）");
            if (b.Skip(s + 1).Take(6).Any(v => v != 0xAA && ((v & 0x0F) > 9 || (v >> 4) > 9)))
            {
                r.Notes.Add("地址含非 BCD、非 AA 的字节。");
            }
            r.Add(s + 7, 1, "第二起始符", "68H");
            if (b[s + 7] != 0x68)
            {
                r.Errors.Add("地址后的第二起始符应为 68。");
            }
            r.Add(s + 8, 1, "控制码 C", Describe645(control));
            r.Add(s + 9, 1, "数据长度 L", dataLength + " 字节；帧应为 " + (dataLength + 12) + " 字节");
            if (actualLength != dataLength + 12)
            {
                r.Errors.Add("长度不符：L=" + dataLength + "，期望帧长 " + (dataLength + 12) + "，实际 " + actualLength + "。请一次输入一帧。");
            }
            Check645Limit(function, dataLength, r.Errors);
            int available = Math.Max(0, Math.Min(dataLength, b.Length - s - 12));
            byte[] plain = b.Skip(s + 10).Take(available).Select(v => unchecked((byte)(v - 0x33))).ToArray();
            if (available > 0)
            {
                r.Add(s + 10, available, "数据域 DATA", "减 33H 后：" + Hex.Format(plain));
            }
            bool abnormal = (control & 0xC0) == 0xC0;
            if (abnormal && plain.Length > 0)
            {
                r.Add(s + 10, 1, "异常信息 ERR", "减 33H 后 " + plain[0].ToString("X2") + "；位值 " + Convert.ToString(plain[0], 2).PadLeft(8, '0'));
            }
            else if (plain.Length >= 4 && (function == 0x11 || function == 0x12 || (function == 0x14 && (control & 0x80) == 0)))
            {
                string di = Hex.ReverseDisplay(plain.Take(4).ToArray());
                r.Add(s + 10, 4, "数据标识 DI", di + "（减 33H，DI0 先传）");
                // Only decode an ordinary read response with an exact known data length.
                if ((control & 0x80) != 0 && function == 0x11)
                {
                    Decode645Value(r, s + 14, di, plain.Skip(4).ToArray());
                }
            }
            else if ((control == 0x93) && plain.Length == 6)
            {
                r.Add(s + 10, 6, "返回通信地址", Hex.ReverseDisplay(plain));
            }
            Checksum(r, s);
            r.Notes.Add("只核验帧结构及已实现的数据项；密码、MAC、业务执行条件和全部功能数据格式未验证。");
        }

        private static void Check645Limit(int function, int length, List<string> errors)
        {
            if ((function == 0x11 || function == 0x12) && length > 200)
            {
                errors.Add("DL/T 645-2007 读数据长度不得超过 200 字节。");
            }
            if (function == 0x14 && length > 50)
            {
                errors.Add("DL/T 645-2007 写数据长度不得超过 50 字节。");
            }
        }

        public static string Describe645(byte control)
        {
            Dictionary<int, string> names = new Dictionary<int, string>
            {
                { 0x08, "广播校时" }, { 0x11, "读数据" }, { 0x12, "读后续数据" },
                { 0x13, "读通信地址" }, { 0x14, "写数据" }, { 0x15, "写通信地址" },
                { 0x16, "冻结" }, { 0x17, "更改速率" }, { 0x18, "修改密码" },
                { 0x19, "最大需量清零" }, { 0x1A, "电表清零" }, { 0x1B, "事件清零" }
            };
            int function = control & 0x1F;
            string name = names.ContainsKey(function) ? names[function] : "保留/未实现功能 " + function.ToString("X2");
            return ((control & 0x80) == 0 ? "主站命令" : (control & 0x40) == 0 ? "从站正常应答" : "从站异常应答")
                + "；" + name + "；后续帧=" + ((control >> 5) & 1);
        }

        private static void Decode645Value(FrameResult r, int offset, string di, byte[] data)
        {
            int size;
            int decimals;
            string unit;
            string name;
            if (di == "00010000")
            {
                size = 4; decimals = 2; unit = "kWh"; name = "当前正向有功总电能";
            }
            else if (di == "02010100" || di == "02010200" || di == "02010300")
            {
                size = 2; decimals = 1; unit = "V"; name = "相电压";
            }
            else
            {
                r.Notes.Add("DI " + di + " 的业务数值暂未实现解码，数据原样保留。");
                return;
            }
            if (data.Length != size)
            {
                r.Notes.Add("DI " + di + " 的数据不是预期的 " + size + " 字节，未做数值解码。");
                return;
            }
            if (data.Any(v => (v & 15) > 9 || (v >> 4) > 9))
            {
                r.Notes.Add("DI " + di + " 含无效 BCD 或缺失标记，未做数值解码。");
                return;
            }
            decimal number = decimal.Parse(Hex.ReverseDisplay(data), CultureInfo.InvariantCulture);
            for (int i = 0; i < decimals; i++)
            {
                number /= 10;
            }
            r.Add(offset, size, name, number.ToString("F" + decimals, CultureInfo.InvariantCulture) + " " + unit);
        }

        private static void ParseLocal(FrameResult r)
        {
            byte[] b = r.Bytes;
            int length = b[1] | b[2] << 8;
            byte control = b[3];
            r.Add(1, 2, "总长度 L", length + " 字节（含 68、L、C、CS、16）");
            if (length != b.Length)
            {
                r.Errors.Add("总长度不符：L=" + length + "，实际 " + b.Length + "。请一次输入一帧。");
            }
            r.Add(3, 1, "控制域 C", "DIR=" + ((control >> 7) & 1) + "；PRM=" + ((control >> 6) & 1)
                + "；ADD=" + ((control >> 5) & 1) + "；VER=" + ((control >> 3) & 3));
            if ((control & 0x18) != 0)
            {
                r.Notes.Add("本地协议标准规定 VER=0；当前版本字段非零。");
            }
            int p = 4;
            if ((control & 0x20) != 0)
            {
                if (b.Length < 24)
                {
                    r.Errors.Add("ADD=1 时缺少完整的 12 字节地址域或应用层。");
                    Checksum(r, 3);
                    return;
                }
                r.Add(p, 6, "源地址 ASRC", Hex.ReverseDisplay(b.Skip(p).Take(6).ToArray()) + "（BIN，低字节先传）");
                r.Add(p + 6, 6, "目的地址 ADST", Hex.ReverseDisplay(b.Skip(p + 6).Take(6).ToArray()) + "（BIN，低字节先传）");
                p += 12;
            }
            r.Add(p, 1, "应用功能 AFN", DescribeAfn(ProtocolKind.CsgLocal, b[p]));
            r.Add(p + 1, 1, "帧序列 SEQ", b[p + 1] + "（0…255）");
            byte[] di = b.Skip(p + 2).Take(4).ToArray();
            r.Add(p + 2, 4, "数据标识 DI", Hex.ReverseDisplay(di) + "（DI0 先传）");
            if (di[1] != b[p])
            {
                r.Notes.Add("DI1 与 AFN 不一致，请核对所选本地协议及版本。");
            }
            int count = b.Length - p - 8;
            if (count > 0)
            {
                r.Add(p + 6, count, "数据标识内容", "原始业务数据，未做完整业务解码");
            }
            Checksum(r, 3);
            r.Notes.Add("依据本机《本地通信模块接口协议（试行）》；SEQ 为完整 8 位序号。业务数据无 ±33H 变换。");
        }

        private static void ParseUplink(FrameResult r, int s)
        {
            byte[] b = r.Bytes;
            int length = b[s + 1] | b[s + 2] << 8;
            int repeated = b[s + 3] | b[s + 4] << 8;
            r.Add(s + 1, 2, "用户数据长度 L", length + " 字节；帧长应为 L+8=" + (length + 8) + "；直接 BIN 编码");
            r.Add(s + 3, 2, "重复长度 L", repeated + " 字节");
            if (length != repeated)
            {
                r.Errors.Add("两个长度域不一致。");
            }
            if (length + 8 != b.Length - s)
            {
                r.Errors.Add("长度不符：L+8=" + (length + 8) + "，实际 " + (b.Length - s) + "。本标准不使用国网的长度左移两位编码。");
            }
            if (length > 16383)
            {
                r.Errors.Add("用户数据长度超过标准规定的网络传输上限 16383。");
            }
            r.Add(s + 5, 1, "第二起始符", "68H");
            if (b[s + 5] != 0x68)
            {
                r.Errors.Add("重复长度后的第二起始符应为 68。");
            }
            byte c = b[s + 6];
            r.Add(s + 6, 1, "控制域 C", "DIR=" + ((c >> 7) & 1) + "；PRM=" + ((c >> 6) & 1)
                + "；D5(FCB/ACD)=" + ((c >> 5) & 1) + "；D4(FCV/保留)=" + ((c >> 4) & 1) + "；链路功能=" + (c & 15));
            r.Add(s + 7, 3, "省地市区县码 A1", Hex.ReverseDisplay(b.Skip(s + 7).Take(3).ToArray()) + "（BCD，低字节先传）");
            r.Add(s + 10, 3, "终端地址 A2", Hex.ReverseDisplay(b.Skip(s + 10).Take(3).ToArray()) + "（BIN）");
            r.Add(s + 13, 1, "主站地址 A3", b[s + 13].ToString());
            byte afn = b[s + 14];
            r.Add(s + 14, 1, "应用功能 AFN", DescribeAfn(ProtocolKind.CsgUplink, afn));
            byte seq = b[s + 15];
            r.Add(s + 15, 1, "帧序列 SEQ", "TpV=" + ((seq >> 7) & 1) + "；FIR=" + ((seq >> 6) & 1)
                + "；FIN=" + ((seq >> 5) & 1) + "；CON=" + ((seq >> 4) & 1) + "；PSEQ/RSEQ=" + (seq & 15));
            int end = b.Length - 2;
            if ((seq & 0x80) != 0)
            {
                if (end - s - 16 < 5)
                {
                    r.Errors.Add("TpV=1，但应用区不足以容纳 5 字节时间标签 Tp。");
                }
                else
                {
                    end -= 5;
                    r.Add(end, 5, "时间标签 Tp", "传输顺序原值；DDhhmmss + 允许延时，不验证时效性");
                }
            }
            int dataLength = end - s - 16;
            if (dataLength > 0)
            {
                r.Add(s + 16, dataLength, "应用业务区", "原始字节；可能包含多个信息体及 16 字节 PW");
            }
            // Restrict the first DA/DI view to known plaintext layouts in the supplied version.
            if (dataLength >= 6 && (afn == 0x00 || afn == 0x02 || afn == 0x0A || afn == 0x0C))
            {
                r.Add(s + 16, 2, "首个信息点 DA", DescribeDa(b[s + 16], b[s + 17]));
                r.Add(s + 18, 4, "首个数据标识 DI", Hex.ReverseDisplay(b.Skip(s + 18).Take(4).ToArray()) + "（DI0 先传）");
            }
            Checksum(r, s + 6);
            r.Notes.Add("依据上行通信规约 2024版V2.0（封面 2026年8月）。仅解读帧头和部分首信息体；不推断多信息体边界，不计算或验证 PW/MAC，不解密。");
        }

        public static string DescribeDa(byte element, byte group)
        {
            if (element == 0 && group == 0)
            {
                return "p0（终端信息点）";
            }
            if (element == 255 && group == 255)
            {
                return "全部测量点（不含 p0）";
            }
            if (element == 0 || group == 0 || group == 255)
            {
                return "未定义组合，请核对标准 6.1.3";
            }
            List<string> points = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                if ((element & (1 << i)) != 0)
                {
                    points.Add("p" + ((group - 1) * 8 + i + 1));
                }
            }
            return string.Join(", ", points);
        }

        public static string DescribeAfn(ProtocolKind protocol, byte afn)
        {
            Dictionary<int, string> names = protocol == ProtocolKind.CsgLocal
                ? new Dictionary<int, string>
                {
                    { 0x00, "确认/否认" }, { 0x01, "初始化模块" }, { 0x02, "管理任务" },
                    { 0x03, "读参数" }, { 0x04, "写参数" }, { 0x05, "上报信息" },
                    { 0x06, "请求信息" }, { 0x07, "传输文件" }, { 0x21, "管理电表" },
                    { 0x22, "转发数据" }, { 0x31, "管理映射表表计" }, { 0xF0, "维护模块" }
                }
                : new Dictionary<int, string>
                {
                    { 0x00, "确认/否定" }, { 0x02, "链路接口检测" }, { 0x04, "写参数" },
                    { 0x06, "安全认证" }, { 0x0A, "读参数" }, { 0x0C, "读当前数据" },
                    { 0x0D, "读历史数据" }, { 0x0E, "读事件记录" }, { 0x0F, "文件传输" },
                    { 0x10, "中继转发" }, { 0x12, "读任务数据" }, { 0x13, "读告警数据" },
                    { 0x14, "级联命令" }, { 0x15, "用户自定义数据" }, { 0x16, "数据安全传输" }, { 0x17, "数据转加密" }
                };
            return afn.ToString("X2") + " — " + (names.ContainsKey(afn) ? names[afn] : "保留/未实现功能");
        }

        public static byte[] Build645(string address, byte control, byte[] plain, int preamble)
        {
            byte[] a = Hex.Parse(address);
            if (a.Length != 6 || a.Any(v => v != 0xAA && ((v & 15) > 9 || (v >> 4) > 9)))
            {
                throw new FormatException("645 地址必须为 6 字节 BCD 或 AA 通配字节，按高字节在前输入，例如 000000000001。");
            }
            if (plain.Length > 255 || preamble < 0 || preamble > 4)
            {
                throw new FormatException("数据最多 255 字节，前导 FE 数量应为 0…4。");
            }
            List<string> errors = new List<string>();
            Check645Limit(control & 31, plain.Length, errors);
            if (errors.Count > 0)
            {
                throw new FormatException(errors[0]);
            }
            Array.Reverse(a);
            List<byte> frame = new List<byte> { 0x68 };
            frame.AddRange(a);
            frame.AddRange(new byte[] { 0x68, control, (byte)plain.Length });
            frame.AddRange(plain.Select(v => unchecked((byte)(v + 0x33))));
            frame.Add(Hex.Sum(frame.ToArray(), 0, frame.Count));
            frame.Add(0x16);
            frame.InsertRange(0, Enumerable.Repeat((byte)0xFE, preamble));
            return frame.ToArray();
        }

        public static byte[] BuildLocal(byte control, string source, string destination, byte afn, byte seq, byte[] di, byte[] data)
        {
            if (di.Length != 4)
            {
                throw new FormatException("本地协议 DI 需要 4 字节，采用 DI0…DI3 传输顺序。");
            }
            List<byte> frame = new List<byte> { 0x68, 0, 0, control };
            if ((control & 0x20) != 0)
            {
                byte[] src = Hex.Parse(source);
                byte[] dst = Hex.Parse(destination);
                if (src.Length != 6 || dst.Length != 6)
                {
                    throw new FormatException("ADD=1 时源地址和目的地址均需 6 字节，按高字节在前输入。");
                }
                frame.AddRange(src.Reverse());
                frame.AddRange(dst.Reverse());
            }
            frame.Add(afn);
            frame.Add(seq);
            frame.AddRange(di);
            frame.AddRange(data);
            int length = frame.Count + 2;
            if (length > 65535)
            {
                throw new FormatException("本地协议帧长超出 2 字节长度域。");
            }
            frame[1] = (byte)(length & 255);
            frame[2] = (byte)(length >> 8);
            frame.Add(Hex.Sum(frame.ToArray(), 3, frame.Count - 3));
            frame.Add(0x16);
            return frame.ToArray();
        }

        public static byte[] BuildUplink(byte control, byte[] address, byte afn, byte seq, byte[] application)
        {
            if (address.Length != 7)
            {
                throw new FormatException("上行地址域 A 必须为 7 字节，按实际传输顺序 A1(3) + A2(3) + A3(1) 输入。");
            }
            if ((seq & 0x80) != 0 && application.Length < 5)
            {
                throw new FormatException("TpV=1 时业务区末尾必须包含 5 字节 Tp。");
            }
            int length = 10 + application.Length;
            if (length > 16383)
            {
                throw new FormatException("用户数据区超过网络传输上限 16383 字节。");
            }
            List<byte> frame = new List<byte>
            {
                0x68, (byte)(length & 255), (byte)(length >> 8), (byte)(length & 255), (byte)(length >> 8), 0x68, control
            };
            frame.AddRange(address);
            frame.Add(afn);
            frame.Add(seq);
            frame.AddRange(application);
            frame.Add(Hex.Sum(frame.ToArray(), 6, length));
            frame.Add(0x16);
            return frame.ToArray();
        }
    }
}
