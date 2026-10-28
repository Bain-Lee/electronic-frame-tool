using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameTools
{
    public enum StreamPartKind
    {
        Frame,
        Incomplete,
        Ambiguous,
        Unrecognized
    }

    public sealed class StreamPart
    {
        public int Offset { get; set; }
        public byte[] Bytes { get; set; }
        public ProtocolKind? Protocol { get; set; }
        public FrameResult Result { get; set; }
        public StreamPartKind Kind { get; set; }
        public string Message { get; set; }
    }

    public sealed class StreamParseResult
    {
        public StreamParseResult()
        {
            Parts = new List<StreamPart>();
            PendingNibble = "";
        }

        public List<StreamPart> Parts { get; private set; }
        public string PendingNibble { get; set; }

        public string FormattedText
        {
            get
            {
                IEnumerable<string> lines = Parts.Select(p => Hex.Format(p.Bytes));
                if (PendingNibble.Length > 0)
                {
                    lines = lines.Concat(new[] { PendingNibble });
                }
                return string.Join(Environment.NewLine, lines);
            }
        }
    }

    public static class FrameStream
    {
        private sealed class Candidate
        {
            public ProtocolKind Protocol;
            public int PrefixLength;
            public int CoreLength;
            public bool Complete;
            public bool EndMatches;
            public bool ChecksumMatches;

            public int ConsumedLength { get { return PrefixLength + CoreLength; } }
            public bool Valid { get { return Complete && EndMatches && ChecksumMatches; } }
        }

        public static StreamParseResult Parse(string text, ProtocolKind? forcedProtocol = null)
        {
            string compact = Hex.Compact(text);
            StreamParseResult result = new StreamParseResult();
            if (compact.Length % 2 != 0)
            {
                result.PendingNibble = compact.Substring(compact.Length - 1);
                compact = compact.Substring(0, compact.Length - 1);
            }
            byte[] bytes = Hex.Parse(compact);
            // Prefix sums evaluate each candidate's required protocol CS range without rescanning its payload.
            int[] sums = new int[bytes.Length + 1];
            for (int i = 0; i < bytes.Length; i++)
            {
                sums[i + 1] = (sums[i] + bytes[i]) & 255;
            }
            int position = 0;
            while (position < bytes.Length)
            {
                List<Candidate> candidates = Probe(bytes, sums, position, forcedProtocol);
                List<Candidate> valid = candidates.Where(c => c.Valid).ToList();
                if (valid.Count == 1)
                {
                    AddFrame(result, bytes, position, valid[0]);
                    position += valid[0].ConsumedLength;
                    continue;
                }
                if (valid.Count > 1)
                {
                    int length = valid.Max(c => c.ConsumedLength);
                    AddPart(result, bytes, position, length, StreamPartKind.Ambiguous, null,
                        "同时满足多个规约的帧结构和校验：" + Names(valid) + "。请关闭自动识别并手动选择规约。");
                    position += length;
                    continue;
                }

                List<Candidate> checksummed = candidates.Where(c => c.Complete && c.ChecksumMatches).ToList();
                if (checksummed.Count > 0)
                {
                    position = AddDamagedOrAmbiguous(result, bytes, sums, position, checksummed, forcedProtocol);
                    continue;
                }
                List<Candidate> framed = candidates.Where(c => c.Complete && c.EndMatches).ToList();
                if (framed.Count > 0)
                {
                    position = AddDamagedOrAmbiguous(result, bytes, sums, position, framed, forcedProtocol);
                    continue;
                }
                List<Candidate> incomplete = candidates.Where(c => !c.Complete).ToList();
                if (incomplete.Count > 0 || IsOnlyPreamble(bytes, position))
                {
                    ProtocolKind? kind = incomplete.Count == 1 ? (ProtocolKind?)incomplete[0].Protocol : null;
                    string message = incomplete.Count == 0 ? "前导 FE 后尚无完整帧头，等待继续输入。"
                        : "报文尚不完整，候选规约：" + Names(incomplete) + "。保留当前尾部，等待继续输入。";
                    AddPart(result, bytes, position, bytes.Length - position, StreamPartKind.Incomplete, kind, message);
                    break;
                }
                List<Candidate> complete = candidates.Where(c => c.Complete).ToList();
                if (complete.Count > 0)
                {
                    position = AddDamagedOrAmbiguous(result, bytes, sums, position, complete, forcedProtocol);
                    continue;
                }

                int next = FindBoundary(bytes, sums, position + 1, bytes.Length, forcedProtocol, false);
                if (next < 0)
                {
                    next = bytes.Length;
                }
                AddPart(result, bytes, position, next - position, StreamPartKind.Unrecognized, null,
                    "未识别字节已保留；未按所支持规约找到可用帧头。");
                position = next;
            }
            return result;
        }

        private static int AddDamagedOrAmbiguous(StreamParseResult result, byte[] bytes, int[] sums,
            int position, List<Candidate> candidates, ProtocolKind? forcedProtocol)
        {
            int length = candidates.Max(c => c.ConsumedLength);
            int next = -1;
            if (!candidates.Any(c => c.EndMatches || c.ChecksumMatches))
            {
                // An intact end marker or CS preserves the declared envelope, including nested data.
                next = FindBoundary(bytes, sums, position + 1, position + length, forcedProtocol, true);
            }
            if (next >= 0)
            {
                AddPart(result, bytes, position, next - position, StreamPartKind.Unrecognized, null,
                    "当前帧的结束符或校验不符；已在后续找到校验通过的边界，前面的字节保留待核对。");
                return next;
            }
            if (candidates.Count == 1)
            {
                AddFrame(result, bytes, position, candidates[0]);
            }
            else
            {
                AddPart(result, bytes, position, length, StreamPartKind.Ambiguous, null,
                    "损坏报文存在多个规约候选：" + Names(candidates) + "。请手动选择规约。");
            }
            return position + length;
        }

        private static void AddFrame(StreamParseResult result, byte[] bytes, int position, Candidate candidate)
        {
            int start = position;
            int length = candidate.ConsumedLength;
            if (candidate.Protocol == ProtocolKind.CsgLocal && candidate.PrefixLength > 0)
            {
                AddPart(result, bytes, position, candidate.PrefixLength, StreamPartKind.Unrecognized, null,
                    "本地规约不包含前导 FE；这些字节作为帧前数据保留。");
                start += candidate.PrefixLength;
                length -= candidate.PrefixLength;
            }
            byte[] frame = Slice(bytes, start, length);
            FrameResult parsed = FrameCodec.Parse(candidate.Protocol, Hex.Format(frame));
            result.Parts.Add(new StreamPart
            {
                Offset = start, Bytes = frame, Protocol = candidate.Protocol, Result = parsed,
                Kind = StreamPartKind.Frame,
                Message = parsed.IsFrameValid ? "帧结构校验通过" : string.Join("；", parsed.Errors)
            });
        }

        private static void AddPart(StreamParseResult result, byte[] bytes, int offset, int length,
            StreamPartKind kind, ProtocolKind? protocol, string message)
        {
            result.Parts.Add(new StreamPart
            {
                Offset = offset, Bytes = Slice(bytes, offset, length), Protocol = protocol,
                Kind = kind, Message = message
            });
        }

        private static byte[] Slice(byte[] bytes, int offset, int length)
        {
            byte[] slice = new byte[length];
            Buffer.BlockCopy(bytes, offset, slice, 0, length);
            return slice;
        }

        private static string Names(IEnumerable<Candidate> candidates)
        {
            return string.Join("、", candidates.Select(c => c.Protocol == ProtocolKind.Dlt645 ? "DL/T 645-2007"
                : c.Protocol == ProtocolKind.CsgLocal ? "南网本地" : "南网上行").Distinct());
        }

        private static bool IsOnlyPreamble(byte[] bytes, int position)
        {
            while (position < bytes.Length && bytes[position] == 0xFE)
            {
                position++;
            }
            return position == bytes.Length;
        }

        private static int FindBoundary(byte[] bytes, int[] sums, int start, int end,
            ProtocolKind? forcedProtocol, bool requireValid)
        {
            int position = start;
            while (position < end)
            {
                if (bytes[position] != 0x68 && bytes[position] != 0xFE)
                {
                    position++;
                    continue;
                }
                List<Candidate> candidates = Probe(bytes, sums, position, forcedProtocol);
                if (requireValid ? candidates.Any(c => c.Valid)
                    : candidates.Count > 0 || IsOnlyPreamble(bytes, position))
                {
                    return position;
                }
                // A rejected FE run has already been examined together with the following 68.
                while (position < end && bytes[position] == 0xFE)
                {
                    position++;
                }
                position++;
            }
            return -1;
        }

        private static List<Candidate> Probe(byte[] bytes, int[] sums, int position, ProtocolKind? forcedProtocol)
        {
            List<Candidate> candidates = new List<Candidate>();
            int start = position;
            while (start < bytes.Length && bytes[start] == 0xFE)
            {
                start++;
            }
            if (start >= bytes.Length || bytes[start] != 0x68)
            {
                return candidates;
            }
            int available = bytes.Length - start;
            for (int p = 0; p < 3; p++)
            {
                ProtocolKind protocol = (ProtocolKind)p;
                if (forcedProtocol.HasValue && forcedProtocol.Value != protocol)
                {
                    continue;
                }
                int length;
                if (protocol == ProtocolKind.Dlt645)
                {
                    if (!Probe645(bytes, start, available, forcedProtocol.HasValue, out length))
                    {
                        continue;
                    }
                }
                else if (protocol == ProtocolKind.CsgLocal)
                {
                    if (!ProbeLocal(bytes, start, available, forcedProtocol.HasValue, out length))
                    {
                        continue;
                    }
                }
                else if (!ProbeUplink(bytes, start, available, forcedProtocol.HasValue, out length))
                {
                    continue;
                }
                Candidate candidate = new Candidate
                {
                    Protocol = protocol, PrefixLength = start - position,
                    CoreLength = length, Complete = length > 0 && length <= available
                };
                if (candidate.Complete)
                {
                    int cs = start + length - 2;
                    int from = start + (protocol == ProtocolKind.Dlt645 ? 0 : protocol == ProtocolKind.CsgLocal ? 3 : 6);
                    candidate.EndMatches = bytes[start + length - 1] == 0x16;
                    candidate.ChecksumMatches = ((sums[cs] - sums[from] + 256) & 255) == bytes[cs];
                }
                candidates.Add(candidate);
            }
            return candidates;
        }

        private static bool Probe645(byte[] bytes, int start, int available, bool forced, out int length)
        {
            length = 0;
            if (!forced)
            {
                for (int i = 1; i < Math.Min(available, 7); i++)
                {
                    byte address = bytes[start + i];
                    if (address != 0xAA && ((address & 15) > 9 || (address >> 4) > 9))
                    {
                        return false;
                    }
                }
            }
            if (available >= 8 && bytes[start + 7] != 0x68)
            {
                return false;
            }
            if (!forced && available >= 9)
            {
                int function = bytes[start + 8] & 31;
                if (function != 8 && (function < 0x11 || function > 0x1B))
                {
                    return false;
                }
            }
            if (available >= 10)
            {
                int dataLength = bytes[start + 9];
                int function = bytes[start + 8] & 31;
                if (!forced && (((function == 0x11 || function == 0x12) && dataLength > 200)
                    || (function == 0x14 && dataLength > 50)))
                {
                    return false;
                }
                length = 12 + dataLength;
            }
            return true;
        }

        private static bool ProbeLocal(byte[] bytes, int start, int available, bool forced, out int length)
        {
            length = 0;
            if (available < 3)
            {
                return true;
            }
            length = bytes[start + 1] | bytes[start + 2] << 8;
            if (length < 12)
            {
                return false;
            }
            if (available >= 4)
            {
                byte control = bytes[start + 3];
                if ((!forced && (control & 0x1F) != 0) || ((control & 0x20) != 0 && length < 24))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ProbeUplink(byte[] bytes, int start, int available, bool forced, out int length)
        {
            length = 0;
            if (available < 3)
            {
                return true;
            }
            int userLength = bytes[start + 1] | bytes[start + 2] << 8;
            if (userLength < 10 || userLength > 16383)
            {
                return false;
            }
            if (!forced && ((available >= 4 && bytes[start + 3] != bytes[start + 1])
                || (available >= 5 && bytes[start + 4] != bytes[start + 2])))
            {
                return false;
            }
            if (available >= 6 && bytes[start + 5] != 0x68)
            {
                return false;
            }
            length = userLength + 8;
            return true;
        }
    }
}
