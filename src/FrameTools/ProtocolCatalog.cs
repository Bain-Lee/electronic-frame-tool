using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;

namespace FrameTools
{
    internal sealed class ProtocolDataItem
    {
        public string Id;
        public string Name;

        public override string ToString()
        {
            return Id + "  " + Name;
        }
    }

    internal static class ProtocolCatalog
    {
        private sealed class Definition
        {
            public XmlElement Node;
            public string Name;
            public byte[] Functions;
            public bool[] Directions;
        }

        private sealed class PendingField
        {
            public int Offset;
            public int Length;
            public string Name;
            public string Meaning;
        }

        private static readonly Dictionary<ProtocolKind, Dictionary<string, List<Definition>>> Catalogs = Load();

        private static Dictionary<ProtocolKind, Dictionary<string, List<Definition>>> Load()
        {
            Dictionary<ProtocolKind, Dictionary<string, List<Definition>>> catalogs = new Dictionary<ProtocolKind, Dictionary<string, List<Definition>>>();
            catalogs.Add(ProtocolKind.CsgUplink, LoadResource("CSG13.xml"));
            catalogs.Add(ProtocolKind.CsgLocal, LoadResource("CSG16.xml"));
            catalogs.Add(ProtocolKind.Dlt645, LoadResource("DLT645.xml"));
            return catalogs;
        }

        private static Dictionary<string, List<Definition>> LoadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FrameTools.Protocols." + name))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("缺少内嵌规约字典 " + name);
                }
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.Load(stream);
                Dictionary<string, List<Definition>> definitions = new Dictionary<string, List<Definition>>(StringComparer.OrdinalIgnoreCase);
                foreach (XmlElement element in document.SelectNodes("/config/dataItem"))
                {
                    string id = element.GetAttribute("id");
                    List<Definition> entries;
                    if (!definitions.TryGetValue(id, out entries))
                    {
                        entries = new List<Definition>();
                        definitions.Add(id, entries);
                    }
                    entries.Add(new Definition
                    {
                        Node = element,
                        Name = Text(element, "name"),
                        Functions = element.GetAttribute(element.HasAttribute("afn") ? "afn" : "function").Split(',').Where(value => value.Trim().Length > 0)
                            .Select(value => byte.Parse(value.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray(),
                        Directions = element.GetAttribute("dir").Split(',').Where(value => value.Trim().Length > 0)
                            .Select(value => value.Trim() == "1").ToArray()
                    });
                }
                return definitions;
            }
        }

        private static List<Definition> Find(ProtocolKind protocol, byte afn, bool up, string di)
        {
            Dictionary<string, List<Definition>> catalog;
            List<Definition> entries;
            if (!Catalogs.TryGetValue(protocol, out catalog) || !catalog.TryGetValue(di, out entries))
            {
                return new List<Definition>();
            }
            return entries.Where(entry => entry.Functions.Contains(afn) && entry.Directions.Contains(up)).ToList();
        }

        public static string Name(ProtocolKind protocol, byte afn, bool up, string di)
        {
            List<Definition> entries = Find(protocol, afn, up, di);
            return entries.Count == 0 ? null : string.Join(" / ", entries.Select(entry =>
                entry.Name + (entry.Node.GetAttribute("status") == "ambiguous" ? "（原文待确认）" : "")).Distinct());
        }

        public static ProtocolDataItem[] Search(ProtocolKind protocol, byte[] afns, string keyword)
        {
            Dictionary<string, List<Definition>> catalog;
            if (!Catalogs.TryGetValue(protocol, out catalog))
            {
                return new ProtocolDataItem[0];
            }
            string filter = keyword.Trim();
            return catalog.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new
                {
                    Id = pair.Key,
                    Entries = pair.Value.Where(entry => entry.Functions.Any(afn => afns.Contains(afn))
                        && entry.Directions.Contains(false) && entry.Node.GetAttribute("status") != "ambiguous").ToArray()
                })
                .Where(item => item.Entries.Length > 0 && (item.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || item.Entries.Any(entry => entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)))
                .Take(200)
                .Select(item => new ProtocolDataItem { Id = item.Id, Name = string.Join(" / ", item.Entries.Select(entry => entry.Name).Distinct()) })
                .ToArray();
        }

        public static bool MeterRequestNeedsParameters(string di)
        {
            return Find(ProtocolKind.Dlt645, 0x11, false, di).Any(entry => entry.Node["request"] != null);
        }

        public static bool TryDecode(FrameResult result, int offset, ProtocolKind protocol, byte afn, bool up,
            string di, byte[] data, bool prefix, out string summary, out int consumed)
        {
            summary = null;
            consumed = 0;
            List<Definition> entries = Find(protocol, afn, up, di);
            if (entries.Count != 1 || entries[0].Node.GetAttribute("status") != "ready")
            {
                return false;
            }
            XmlElement definition = entries[0].Node;
            List<PendingField> fields = new List<PendingField>();
            Dictionary<string, decimal> values = new Dictionary<string, decimal>(StringComparer.Ordinal);
            int position = 0;
            try
            {
                if (offset < 0 || data.Length > result.Bytes.Length - offset)
                {
                    throw new FormatException("数据区超出原始报文范围");
                }
                if (!Read(definition, data, ref position, values, fields, ""))
                {
                    return false;
                }
                if (!prefix && position != data.Length)
                {
                    throw new FormatException("数据长度不符：字典结构为 " + position + " 字节，实际 " + data.Length + " 字节");
                }
            }
            catch (FormatException error)
            {
                summary = error.Message + "，原始值保留";
                result.Notes.Add("DI " + di + "：" + summary + "。");
                return true;
            }
            foreach (PendingField field in fields)
            {
                result.Add(offset + field.Offset, field.Length, field.Name, field.Meaning);
            }
            consumed = position;
            summary = entries[0].Name + "；" + (position == 0 ? "无数据内容" : string.Join("；", fields.Select(field => field.Name + "：" + field.Meaning)));
            return true;
        }

        private static bool Read(XmlElement node, byte[] data, ref int position, Dictionary<string, decimal> values,
            List<PendingField> fields, string prefix)
        {
            List<XmlElement> children = Elements(node).Where(child => child.Name == "splitByLength" || child.Name == "repeat").ToList();
            int start = position;
            if (children.Count > 0)
            {
                foreach (XmlElement child in children)
                {
                    if (child.Name == "repeat")
                    {
                        decimal number;
                        bool remaining = child.GetAttribute("count") == "remaining";
                        if (remaining)
                        {
                            int stride = int.Parse(child.GetAttribute("itemLength"), CultureInfo.InvariantCulture);
                            int minimum = int.Parse(child.GetAttribute("minimum"), CultureInfo.InvariantCulture);
                            int maximum = int.Parse(child.GetAttribute("maximum"), CultureInfo.InvariantCulture);
                            int bytesLeft = data.Length - position;
                            if (bytesLeft % stride != 0 || bytesLeft / stride < minimum || bytesLeft / stride > maximum)
                            {
                                throw new FormatException("实际数据长度与总量及已配置费率数不符");
                            }
                            number = bytesLeft / stride;
                        }
                        else if (!values.TryGetValue(child.GetAttribute("count"), out number) || number < 0 || number > int.MaxValue || number != decimal.Truncate(number))
                        {
                            return false;
                        }
                        if (child.HasAttribute("skipValue")
                            && decimal.Parse(child.GetAttribute("skipValue"), CultureInfo.InvariantCulture) == number)
                        {
                            continue;
                        }
                        for (int index = 0; index < (int)number; index++)
                        {
                            int previous = position;
                            Dictionary<string, decimal> recordValues = new Dictionary<string, decimal>(values, StringComparer.Ordinal);
                            string label = child.HasAttribute("first") ? index == 0 ? child.GetAttribute("first") : Text(child, "name") + index
                                : Text(child, "name") + (index + 1);
                            if (!Read(child, data, ref position, recordValues, fields, prefix + label + ".") || position == previous)
                            {
                                return false;
                            }
                        }
                    }
                    else if (!Read(child, data, ref position, values, fields, prefix))
                    {
                        return false;
                    }
                }
                int expected;
                if (int.TryParse(Text(node, "length"), NumberStyles.None, CultureInfo.InvariantCulture, out expected) && expected != position - start)
                {
                    throw new FormatException("字典字段长度与总长度不一致");
                }
                return true;
            }

            int length;
            if (!Length(node, values, out length))
            {
                return false;
            }
            if (length > data.Length - position)
            {
                throw new FormatException("数据长度不足：" + Text(node, "name") + " 需要 " + length + " 字节，剩余 " + (data.Length - position) + " 字节");
            }
            if (length == 0)
            {
                return true;
            }
            byte[] bytes = data.Skip(position).Take(length).ToArray();
            string name = Text(node, "name");
            string type = Text(node, "type").ToUpperInvariant();
            string time = Text(node, "time");
            string meaning;
            decimal numberValue;
            bool numeric = false;
            if (time.Length > 0)
            {
                meaning = null;
                if (node["value"] != null && Number(bytes, true, false, out numberValue))
                {
                    meaning = EnumValue(node, numberValue);
                }
                if (meaning == null && !Clock(bytes, time, out meaning))
                {
                    return false;
                }
            }
            else if (type == "BIN" || type == "BCD")
            {
                if (!Number(bytes, type == "BCD", Text(node, "sign") == "yes", out numberValue))
                {
                    return false;
                }
                numeric = true;
                values[name] = numberValue;
                meaning = EnumValue(node, numberValue);
                if (meaning == null)
                {
                    int decimals;
                    int.TryParse(Text(node, "decimal"), NumberStyles.None, CultureInfo.InvariantCulture, out decimals);
                    if (decimals < 0 || decimals > 12)
                    {
                        return false;
                    }
                    decimal scaled = numberValue;
                    for (int index = 0; index < decimals; index++)
                    {
                        scaled /= 10;
                    }
                    meaning = scaled.ToString("F" + decimals, CultureInfo.InvariantCulture);
                    string unit = Text(node, "unit");
                    if (unit.Length > 0)
                    {
                        meaning += " " + unit;
                    }
                }
            }
            else if (type == "ASCII")
            {
                meaning = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            }
            else if (type == "" || type == "RAW")
            {
                meaning = Hex.Format(bytes);
            }
            else
            {
                return false;
            }
            fields.Add(new PendingField { Offset = position, Length = length, Name = prefix + name, Meaning = meaning });
            XmlElement splitbit = node["splitbit"];
            if (splitbit != null)
            {
                decimal bitValue;
                if (numeric)
                {
                    bitValue = values[name];
                }
                else if (!Number(bytes, false, false, out bitValue))
                {
                    return false;
                }
                if (bitValue < 0 || bitValue > ulong.MaxValue)
                {
                    return false;
                }
                foreach (XmlElement bit in Elements(splitbit).Where(child => child.Name == "bit"))
                {
                    string[] range = bit.GetAttribute("id").Split('-');
                    int low;
                    int high;
                    if (!int.TryParse(range[0], out low) || !int.TryParse(range[range.Length - 1], out high)
                        || low < 0 || high < low || high >= length * 8 || high > 63)
                    {
                        return false;
                    }
                    int width = high - low + 1;
                    ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
                    decimal selected = ((ulong)bitValue >> low) & mask;
                    string label = EnumValue(bit, selected) ?? selected.ToString(CultureInfo.InvariantCulture);
                    fields.Add(new PendingField { Offset = position, Length = length, Name = prefix + name + "." + Text(bit, "name"), Meaning = label });
                }
            }
            position += length;
            return true;
        }

        private static bool Length(XmlElement node, Dictionary<string, decimal> values, out int length)
        {
            string literal = Text(node, "length");
            foreach (XmlElement condition in Elements(node).Where(child => child.Name == "lengthIf"))
            {
                decimal value;
                if (!values.TryGetValue(condition.GetAttribute("field"), out value) || value < 0 || value > ulong.MaxValue)
                {
                    length = 0;
                    return false;
                }
                ulong mask = ulong.Parse(condition.GetAttribute("mask"), CultureInfo.InvariantCulture);
                ulong equal = ulong.Parse(condition.GetAttribute("equals"), CultureInfo.InvariantCulture);
                if (((ulong)value & mask) == equal)
                {
                    literal = condition.InnerText.Trim();
                }
            }
            if (int.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out length))
            {
                return true;
            }
            string rule = Text(node, "lengthrule");
            string[] parts = rule.Split('*');
            int multiplier = 1;
            string key = rule;
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out multiplier))
                {
                    return false;
                }
                key = parts[1].Trim();
            }
            else if (parts.Length != 1)
            {
                return false;
            }
            decimal count;
            if (!values.TryGetValue(key, out count) || count < 0 || count != decimal.Truncate(count) || (multiplier > 0 && count > int.MaxValue / multiplier))
            {
                return false;
            }
            length = (int)(count * multiplier);
            return true;
        }

        private static bool Number(byte[] bytes, bool bcd, bool signed, out decimal value)
        {
            value = 0;
            if (bytes.Length > (bcd ? 12 : 8))
            {
                return false;
            }
            if (bcd && bytes.All(item => item == 0xFF))
            {
                throw new FormatException("数据缺失（全 FF），不换算数值");
            }
            bool negative = signed && (bytes[bytes.Length - 1] & 0x80) != 0;
            for (int index = bytes.Length - 1; index >= 0; index--)
            {
                int current = signed && index == bytes.Length - 1 ? bytes[index] & 0x7F : bytes[index];
                if (bcd && ((current & 15) > 9 || (current >> 4) > 9))
                {
                    throw new FormatException("含无效 BCD，未换算数值");
                }
                value = value * (bcd ? 100 : 256) + (bcd ? (current >> 4) * 10 + (current & 15) : current);
            }
            if (negative)
            {
                value = -value;
            }
            return true;
        }

        private static bool Clock(byte[] bytes, string format, out string value)
        {
            value = null;
            string normalized = format.Replace("dd", "DD");
            if (normalized != "ssmmhhDDMMYY" && normalized != "mmhhDDMMYY" && normalized != "DDMMYY"
                && normalized != "ssmmhh" && normalized != "mmhh" && normalized != "WWDDMMYY"
                && normalized != "mmhhDDMM" && normalized != "hhDD")
            {
                return false;
            }
            if (bytes.Length * 2 != normalized.Length)
            {
                throw new FormatException("日期时间字段长度不符");
            }
            Dictionary<string, int> parts = new Dictionary<string, int>();
            for (int index = 0; index < bytes.Length; index++)
            {
                decimal number;
                Number(new byte[] { bytes[index] }, true, false, out number);
                parts[normalized.Substring(index * 2, 2)] = (int)number;
            }
            int year = parts.ContainsKey("YY") ? 2000 + parts["YY"] : 2000;
            int month = parts.ContainsKey("MM") ? parts["MM"] : 1;
            int day = parts.ContainsKey("DD") ? parts["DD"] : 1;
            int hour = parts.ContainsKey("hh") ? parts["hh"] : 0;
            int minute = parts.ContainsKey("mm") ? parts["mm"] : 0;
            int second = parts.ContainsKey("ss") ? parts["ss"] : 0;
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
                || hour > 23 || minute > 59 || second > 59 || parts.ContainsKey("WW") && parts["WW"] > 6)
            {
                throw new FormatException("无效 BCD 日期时间");
            }
            DateTime date = new DateTime(year, month, day, hour, minute, second);
            string pattern = parts.ContainsKey("YY") ? "yy-MM-dd" : "";
            if (normalized == "mmhhDDMM")
            {
                pattern = "MM-dd";
            }
            if (normalized == "hhDD")
            {
                value = day + "日 " + hour.ToString("D2", CultureInfo.InvariantCulture) + "时";
                return true;
            }
            if (parts.ContainsKey("hh"))
            {
                pattern += (pattern.Length > 0 ? " " : "") + (parts.ContainsKey("ss") ? "HH:mm:ss" : "HH:mm");
            }
            value = date.ToString(pattern, CultureInfo.InvariantCulture);
            if (parts.ContainsKey("WW"))
            {
                value += " 星期" + "日一二三四五六"[parts["WW"]];
            }
            return true;
        }

        private static string EnumValue(XmlElement node, decimal value)
        {
            string other = null;
            foreach (XmlElement option in Elements(node).Where(child => child.Name == "value"))
            {
                decimal key;
                if (decimal.TryParse(option.GetAttribute("key"), NumberStyles.Integer, CultureInfo.InvariantCulture, out key) && key == value)
                {
                    return option.InnerText;
                }
                if (option.GetAttribute("key") == "other")
                {
                    other = option.InnerText + "（" + value.ToString(CultureInfo.InvariantCulture) + "）";
                }
            }
            return other;
        }

        private static string Text(XmlElement node, string name)
        {
            XmlElement child = node[name];
            return child == null ? "" : child.InnerText.Trim();
        }

        private static IEnumerable<XmlElement> Elements(XmlElement node)
        {
            return node.ChildNodes.OfType<XmlElement>();
        }
    }
}
