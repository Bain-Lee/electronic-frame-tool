using System;
using System.Linq;
using FrameTools;

internal static class StreamTests
{
    private const string Read645 = "68 01 00 00 00 00 00 68 11 04 33 33 34 33 B3 16";
    private const string Local = "68 0C 00 40 03 01 01 03 00 E8 30 16";
    private const string Uplink = "68 10 00 10 00 68 4B 06 01 44 01 00 00 01 0C 60 01 01 00 00 01 00 07 16";

    public static int Passed { get; private set; }
    public static int Failed { get; private set; }

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
            Console.WriteLine("PASS stream " + name);
        }
        catch (Exception e)
        {
            Failed++;
            Console.WriteLine("FAIL stream " + name + ": " + e.Message);
        }
    }

    private static string Packed(string text)
    {
        return Hex.Compact(text);
    }

    private static void Preserved(StreamParseResult result, string original)
    {
        string compact = Packed(original);
        Check(Packed(result.FormattedText) == compact, "Formatted stream loses or changes input bytes/nibbles");
        int offset = 0;
        foreach (StreamPart part in result.Parts)
        {
            Check(part.Offset == offset, "Part offsets are not contiguous");
            offset += part.Bytes.Length;
        }
        Check(offset * 2 + result.PendingNibble.Length == compact.Length, "Not all input bytes were accounted for");
    }

    private static StreamPart OnlyFrame(StreamParseResult result)
    {
        Check(result.Parts.Count == 1, "Expected exactly one part");
        StreamPart part = result.Parts[0];
        Check(part.Kind == StreamPartKind.Frame && part.Result != null, "Expected an identified frame");
        return part;
    }

    public static int Run()
    {
        Passed = 0;
        Failed = 0;
        Run("spaced, compact and lowercase vectors identify all three protocols", delegate
        {
            string[] vectors = { Read645, Local, Uplink };
            for (int p = 0; p < vectors.Length; p++)
            {
                foreach (string text in new[] { vectors[p], Packed(vectors[p]), Packed(vectors[p]).ToLowerInvariant() })
                {
                    StreamParseResult result = FrameStream.Parse(text);
                    StreamPart frame = OnlyFrame(result);
                    Check(frame.Protocol == (ProtocolKind)p && frame.Result.IsFrameValid, "Wrong protocol or frame result");
                    Preserved(result, text);
                }
            }
        });
        Run("mixed compact stream splits by declared lengths", delegate
        {
            string text = Packed(Read645 + Local + Uplink + Read645);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 4 && result.Parts.All(p => p.Kind == StreamPartKind.Frame && p.Result.IsFrameValid), "Missing mixed frame");
            Check(result.Parts.Select(p => p.Protocol).SequenceEqual(new ProtocolKind?[]
                { ProtocolKind.Dlt645, ProtocolKind.CsgLocal, ProtocolKind.CsgUplink, ProtocolKind.Dlt645 }), "Mixed order changed");
            Preserved(result, text);
        });
        Run("mixed whitespace and byte separators", delegate
        {
            string text = Read645.Replace(" ", "\t") + "\r\n" + Local.Replace(" ", ",") + " " + Uplink.Replace(" ", "-");
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 3 && result.Parts.All(p => p.Kind == StreamPartKind.Frame), "Separator handling");
            Preserved(result, text);
        });
        Run("0x-prefixed byte stream", delegate
        {
            string text = string.Join(" ", Hex.Parse(Read645).Select(b => "0x" + b.ToString("X2"))) + Packed(Local);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 2 && result.Parts.All(p => p.Kind == StreamPartKind.Frame), "0x handling");
            Preserved(result, text);
        });
        Run("payload 68 and 16 do not delimit frames", delegate
        {
            byte[] bytes = FrameCodec.BuildLocal(0x40, "", "", 3, 1, Hex.Identifier("E8000301"), Hex.Parse("68 16 68 16"));
            string text = Hex.Format(bytes) + Packed(Read645);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 2 && result.Parts[0].Bytes.SequenceEqual(bytes), "Payload was split at a marker");
            Preserved(result, text);
        });
        Run("645 and uplink retain FE while local FE is separate", delegate
        {
            string text = "FE FE " + Read645 + " FE " + Local + " FE FE FE FE " + Uplink;
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 4, "FE part count");
            Check(result.Parts[0].Kind == StreamPartKind.Frame && result.Parts[0].Bytes[0] == 0xFE, "645 FE missing");
            Check(result.Parts[1].Kind == StreamPartKind.Unrecognized && result.Parts[1].Bytes.SequenceEqual(new byte[] { 0xFE }), "Local FE included in frame");
            Check(result.Parts[2].Protocol == ProtocolKind.CsgLocal && result.Parts[2].Result.IsFrameValid, "Local after FE");
            Check(result.Parts[3].Protocol == ProtocolKind.CsgUplink && result.Parts[3].Bytes[0] == 0xFE, "Uplink FE missing");
            Preserved(result, text);
        });
        Run("bad CS remains diagnosable and following frame survives", delegate
        {
            byte[] bad = Hex.Parse(Read645);
            bad[bad.Length - 2] ^= 1;
            string text = Hex.Format(bad) + Packed(Local);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 2 && result.Parts[0].Kind == StreamPartKind.Frame, "Damaged frame discarded");
            Check(!result.Parts[0].Result.IsFrameValid && result.Parts[0].Result.Errors.Any(e => e.Contains("校验和")), "Missing CS diagnosis");
            Check(result.Parts[1].Protocol == ProtocolKind.CsgLocal && result.Parts[1].Result.IsFrameValid, "Following frame lost");
            Preserved(result, text);
        });
        Run("bad end remains diagnosable and following frame survives", delegate
        {
            byte[] bad = Hex.Parse(Local);
            bad[bad.Length - 1] = 0;
            string text = Hex.Format(bad) + Packed(Read645);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 2 && result.Parts[0].Kind == StreamPartKind.Frame && !result.Parts[0].Result.IsFrameValid, "Bad end diagnosis missing");
            Check(result.Parts[1].Protocol == ProtocolKind.Dlt645 && result.Parts[1].Result.IsFrameValid, "Following frame lost");
            Preserved(result, text);
        });
        Run("checksum-matching bad-end 645 outranks an incomplete local hypothesis", delegate
        {
            string text = "68 12 00 00 00 00 00 68 11 04 33 33 34 33 C4 00";
            StreamParseResult result = FrameStream.Parse(text);
            StreamPart part = OnlyFrame(result);
            Check(part.Protocol == ProtocolKind.Dlt645 && !part.Result.IsFrameValid
                && part.Result.Errors.Any(e => e.Contains("结束字节")), "Bad-end diagnosis was hidden by an incomplete local hypothesis");
            Preserved(result, text);
        });
        Run("checksum-matching bad-end 645 does not consume the following local frame", delegate
        {
            string text = "68 12 00 00 00 00 00 68 11 04 33 33 34 33 C4 00 " + Local;
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 2 && result.Parts.All(p => p.Kind == StreamPartKind.Frame), "Bad-end frame swallowed a following boundary");
            Check(result.Parts[0].Protocol == ProtocolKind.Dlt645 && !result.Parts[0].Result.IsFrameValid, "Bad 645 diagnosis missing");
            Check(result.Parts[1].Protocol == ProtocolKind.CsgLocal && result.Parts[1].Result.IsFrameValid
                && result.Parts[1].Offset == 16, "Following local frame was not recovered at byte 16");
            Preserved(result, text);
        });
        Run("provably corrupt inflated length recovers at validated boundary", delegate
        {
            byte[] bad = Hex.Parse(Local);
            bad[1] = 23;
            string text = Hex.Format(bad) + Packed(Local);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 2 && result.Parts[0].Kind == StreamPartKind.Unrecognized, "Corrupt prefix not retained");
            Check(result.Parts[0].Bytes.SequenceEqual(bad), "Corrupt prefix changed");
            Check(result.Parts[1].Protocol == ProtocolKind.CsgLocal && result.Parts[1].Result.IsFrameValid, "Recovery missed validated frame");
            Preserved(result, text);
        });
        Run("corrupt length landing on a later 16 retains the errored declared envelope", delegate
        {
            byte[] bad = Hex.Parse(Local);
            bad[1] = 24;
            string text = Hex.Format(bad) + Packed(Local);
            StreamParseResult result = FrameStream.Parse(text);
            StreamPart part = OnlyFrame(result);
            Check(!part.Result.IsFrameValid && part.Bytes.Length == 24, "Intact declared end was discarded for a guessed inner boundary");
            Preserved(result, text);
        });
        Run("impossible short length preserves bytes and recovers", delegate
        {
            byte[] bad = Hex.Parse(Local);
            bad[1] = 5;
            string text = Hex.Format(bad) + Packed(Read645);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 2 && result.Parts[0].Kind == StreamPartKind.Unrecognized, "Impossible prefix classification");
            Check(result.Parts[1].Protocol == ProtocolKind.Dlt645, "Recovery missed following 645");
            Preserved(result, text);
        });
        Run("unprovable long length remains incomplete without stripping inner bytes", delegate
        {
            byte[] bad = Hex.Parse(Local);
            bad[1] = 0;
            bad[2] = 2;
            string text = Hex.Format(bad) + Packed(Read645);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 1 && result.Parts[0].Kind == StreamPartKind.Incomplete, "Incomplete envelope was stripped");
            Preserved(result, text);
        });
        Run("genuine partial envelope containing a valid nested frame is not stripped", delegate
        {
            byte[] frame = FrameCodec.BuildLocal(0x40, "", "", 3, 1, Hex.Identifier("E8000301"),
                Hex.Parse(Read645).Concat(new byte[10]).ToArray());
            string text = Hex.Format(frame.Take(frame.Length - 4).ToArray());
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 1 && result.Parts[0].Kind == StreamPartKind.Incomplete, "Nested valid frame escaped partial envelope");
            Preserved(result, text);
        });
        Run("completed envelope containing a valid nested frame remains one frame", delegate
        {
            byte[] frame = FrameCodec.BuildLocal(0x40, "", "", 3, 1, Hex.Identifier("E8000301"), Hex.Parse(Read645));
            StreamParseResult result = FrameStream.Parse(Hex.Format(frame));
            Check(OnlyFrame(result).Bytes.SequenceEqual(frame), "Nested frame was split");
        });
        Run("bad-CS outer frame retains diagnosis when payload contains a valid inner frame", delegate
        {
            byte[] frame = FrameCodec.BuildLocal(0x40, "", "", 3, 1, Hex.Identifier("E8000301"), Hex.Parse(Read645));
            frame[frame.Length - 2] ^= 1;
            string text = Hex.Format(frame) + Packed(Uplink);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 2 && result.Parts[0].Kind == StreamPartKind.Frame, "Bad outer CS promoted the nested frame");
            Check(result.Parts[0].Protocol == ProtocolKind.CsgLocal && !result.Parts[0].Result.IsFrameValid
                && result.Parts[0].Bytes.SequenceEqual(frame), "Outer diagnosis or bytes lost");
            Check(result.Parts[1].Protocol == ProtocolKind.CsgUplink && result.Parts[1].Result.IsFrameValid, "Following frame lost");
            Preserved(result, text);
        });
        Run("CS-matching bad-end outer frame does not promote a valid inner frame", delegate
        {
            byte[] frame = FrameCodec.BuildLocal(0x40, "", "", 3, 1, Hex.Identifier("E8000301"), Hex.Parse(Read645));
            frame[frame.Length - 1] = 0;
            StreamPart part = OnlyFrame(FrameStream.Parse(Hex.Format(frame)));
            Check(part.Protocol == ProtocolKind.CsgLocal && !part.Result.IsFrameValid && part.Bytes.SequenceEqual(frame), "Bad end stripped a checksum-matching outer frame");
        });
        Run("all partial prefixes of all three vectors are preserved", delegate
        {
            foreach (string vector in new[] { Read645, Local, Uplink })
            {
                string compact = Packed(vector);
                for (int length = 1; length < compact.Length; length++)
                {
                    string partial = compact.Substring(0, length);
                    StreamParseResult result = FrameStream.Parse(partial);
                    Check(!result.Parts.Any(p => p.Kind == StreamPartKind.Frame), "Partial frame was accepted");
                    Preserved(result, partial);
                }
            }
        });
        Run("completed frame plus half frame parses complete frame now", delegate
        {
            string text = Packed(Read645 + Local).Substring(0, Packed(Read645).Length + 11);
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts[0].Kind == StreamPartKind.Frame && result.Parts[0].Protocol == ProtocolKind.Dlt645, "Completed frame was hidden");
            Check(result.Parts[1].Kind == StreamPartKind.Incomplete && result.PendingNibble.Length == 1, "Half frame/nibble not retained");
            Preserved(result, text);
        });
        Run("odd final nibble does not hide previous complete frames", delegate
        {
            string text = Packed(Read645) + "6";
            StreamParseResult result = FrameStream.Parse(text);
            Check(OnlyFrame(result).Result.IsFrameValid && result.PendingNibble == "6", "Odd nibble handling");
            Preserved(result, text);
        });
        Run("single nibble is retained until a byte arrives", delegate
        {
            StreamParseResult result = FrameStream.Parse("a");
            Check(result.Parts.Count == 0 && result.PendingNibble == "A" && result.FormattedText == "A", "Single nibble lost");
        });
        Run("unknown prefix and suffix stay visible", delegate
        {
            string text = "00 FF " + Read645 + " DE AD";
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 3 && result.Parts[0].Kind == StreamPartKind.Unrecognized
                && result.Parts[1].Kind == StreamPartKind.Frame && result.Parts[2].Kind == StreamPartKind.Unrecognized, "Noise discarded");
            Preserved(result, text);
        });
        Run("FE-only input is pending", delegate
        {
            StreamParseResult result = FrameStream.Parse("FEFEFE");
            Check(result.Parts.Count == 1 && result.Parts[0].Kind == StreamPartKind.Incomplete, "FE waiting state");
            Preserved(result, "FEFEFE");
        });
        Run("real dual-valid local/645 frame stays ambiguous", delegate
        {
            byte[] frame = FrameCodec.BuildLocal(0x60, "8C1168000000", "000000000001", 3, 1, Hex.Identifier("E8000301"), new byte[128]);
            string text = Hex.Format(frame);
            Check(FrameCodec.Parse(ProtocolKind.Dlt645, text).IsFrameValid && FrameCodec.Parse(ProtocolKind.CsgLocal, text).IsFrameValid, "Test vector is not genuinely ambiguous");
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 1 && result.Parts[0].Kind == StreamPartKind.Ambiguous && result.Parts[0].Protocol == null, "Ambiguity guessed away");
            Preserved(result, text);
        });
        Run("manual protocol resolves genuine ambiguity", delegate
        {
            byte[] frame = FrameCodec.BuildLocal(0x60, "8C1168000000", "000000000001", 3, 1, Hex.Identifier("E8000301"), new byte[128]);
            foreach (ProtocolKind protocol in new[] { ProtocolKind.Dlt645, ProtocolKind.CsgLocal })
            {
                StreamPart part = OnlyFrame(FrameStream.Parse(Hex.Format(frame), protocol));
                Check(part.Protocol == protocol && part.Result.IsFrameValid, "Manual protocol not honored");
            }
        });
        Run("FE does not silently resolve a genuine local/645 ambiguity", delegate
        {
            byte[] frame = FrameCodec.BuildLocal(0x60, "8C1168000000", "000000000001", 3, 1, Hex.Identifier("E8000301"), new byte[128]);
            string text = "FE FE " + Hex.Format(frame);
            Check(FrameStream.Parse(text).Parts.Single().Kind == StreamPartKind.Ambiguous, "FE forced 645 without considering local+prefix");
            StreamParseResult forced = FrameStream.Parse(text, ProtocolKind.CsgLocal);
            Check(forced.Parts.Count == 2 && forced.Parts[0].Kind == StreamPartKind.Unrecognized && forced.Parts[1].Protocol == ProtocolKind.CsgLocal, "Manual local FE boundary");
            Preserved(forced, text);
        });
        Run("manual protocol filters incompatible frames", delegate
        {
            StreamParseResult result = FrameStream.Parse(Read645, ProtocolKind.CsgLocal);
            Check(!result.Parts.Any(p => p.Kind == StreamPartKind.Frame), "Wrong manual protocol ignored");
            Preserved(result, Read645);
        });
        Run("automatic 645 matching respects existing read/write length limits", delegate
        {
            byte[] controls = { 0x11, 0x14 };
            int[] sizes = { 201, 51 };
            for (int i = 0; i < controls.Length; i++)
            {
                byte[] frame = FrameCodec.Build645("000000000001", 0x16, new byte[sizes[i]], 0);
                frame[8] = controls[i];
                frame[frame.Length - 2] = Hex.Sum(frame, 0, frame.Length - 2);
                string text = Hex.Format(frame);
                Check(!FrameStream.Parse(text).Parts.Any(p => p.Kind == StreamPartKind.Frame), "Illegal length treated as an automatic match");
                StreamPart manual = OnlyFrame(FrameStream.Parse(text, ProtocolKind.Dlt645));
                Check(!manual.Result.IsFrameValid && manual.Result.Errors.Any(e => e.Contains("长度不得超过")), "Manual length diagnosis lost");
                Preserved(FrameStream.Parse(text), text);
            }
        });
        Run("formatting is idempotent for a mixed stream and partial tail", delegate
        {
            string text = "FF " + Packed(Read645 + Local + Uplink) + "686";
            StreamParseResult first = FrameStream.Parse(text);
            StreamParseResult second = FrameStream.Parse(first.FormattedText);
            Check(first.FormattedText == second.FormattedText, "Formatting changed on repeated parse");
            Check(first.Parts.Select(p => p.Kind).SequenceEqual(second.Parts.Select(p => p.Kind)), "Classification changed after formatting");
            Preserved(second, text);
        });
        Run("invalid character remains an input error", delegate
        {
            bool rejected = false;
            try
            {
                FrameStream.Parse("68 ZZ");
            }
            catch (FormatException)
            {
                rejected = true;
            }
            Check(rejected, "Invalid log text was silently stripped");
        });
        Run("strict single-frame HEX parsing still rejects odd nibble", delegate
        {
            bool rejected = false;
            try
            {
                Hex.Parse("681");
            }
            catch (FormatException)
            {
                rejected = true;
            }
            Check(rejected, "Strict HEX contract changed");
        });
        Run("large continuous stream identifies every frame", delegate
        {
            string text = string.Concat(Enumerable.Repeat(Packed(Read645), 4096));
            StreamParseResult result = FrameStream.Parse(text);
            Check(result.Parts.Count == 4096 && result.Parts.All(p => p.Kind == StreamPartKind.Frame && p.Result.IsFrameValid), "Large stream lost frames");
            Preserved(result, text);
        });
        Console.WriteLine("STREAM RESULT: " + Passed + " passed, " + Failed + " failed");
        return Failed;
    }
}
