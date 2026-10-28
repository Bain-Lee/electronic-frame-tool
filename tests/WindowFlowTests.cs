using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FrameTools;

internal static class WindowFlowTests
{
    private const string Read645 = "68 01 00 00 00 00 00 68 11 04 33 33 34 33 B3 16";
    private const string Local = "68 0C 00 40 03 01 01 03 00 E8 30 16";
    private const string Uplink = "68 10 00 10 00 68 4B 06 01 44 01 00 00 01 0C 60 01 01 00 00 01 00 07 16";
    private static int failed;

    public static int Passed { get; private set; }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static T Named<T>(MainForm form, string name) where T : Control
    {
        Control[] matches = form.Controls.Find(name, true);
        Check(matches.Length == 1 && matches[0] is T, "Expected one " + typeof(T).Name + " named " + name);
        return (T)matches[0];
    }

    private static MainForm NewForm()
    {
        MainForm form = new MainForm();
        form.BindingContext = new BindingContext();
        // Create the test form and grid handles without showing a desktop window.
        IntPtr formHandle = form.Handle;
        IntPtr gridHandle = Named<DataGridView>(form, "parsedFields").Handle;
        Check(formHandle != IntPtr.Zero && gridHandle != IntPtr.Zero, "Window handles were not created");
        return form;
    }

    private static void WaitFor(Func<bool> condition, string message)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 2000)
        {
            Application.DoEvents();
            if (condition())
            {
                return;
            }
            Thread.Sleep(10);
        }
        throw new Exception(message);
    }

    private static void PumpFor(int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static FrameField[] Data(DataGridView grid)
    {
        IEnumerable<FrameField> data = grid.DataSource as IEnumerable<FrameField>;
        return data == null ? new FrameField[0] : data.ToArray();
    }

    private static bool HasField(DataGridView grid, string name)
    {
        return Data(grid).Any(field => field.Name == name);
    }

    private static string Compact(string text)
    {
        return string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
    }

    private static void SelectField(DataGridView grid, string name)
    {
        DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().Single(item =>
        {
            FrameField field = item.DataBoundItem as FrameField;
            return field != null && field.Name == name;
        });
        grid.ClearSelection();
        grid.CurrentCell = row.Cells[0];
        row.Selected = true;
        Application.DoEvents();
    }

    private static void RunTest(string name, Action test)
    {
        try
        {
            test();
            Passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error.Message);
        }
    }

    private static void AutoRecognizes(string frame, int otherProtocol, string expectedField)
    {
        using (MainForm form = NewForm())
        {
            Check(Named<CheckBox>(form, "automaticProtocol").Checked, "Automatic detection is not the default");
            Named<ComboBox>(form, "protocol").SelectedIndex = otherProtocol;
            TextBox raw = Named<TextBox>(form, "rawInput");
            TextBox formatted = Named<TextBox>(form, "formattedStream");
            DataGridView grid = Named<DataGridView>(form, "parsedFields");
            ComboBox parts = Named<ComboBox>(form, "streamParts");
            raw.Text = Compact(frame);
            WaitFor(delegate { return parts.Items.Count == 1 && formatted.Text == frame && HasField(grid, expectedField); },
                "Pasted frame was not automatically detected and parsed");
            Check(raw.Text == Compact(frame), "Automatic formatting overwrote the raw input");
            Check(formatted.ReadOnly, "Formatted output must be read-only");
        }
    }

    public static int Run()
    {
        Passed = 0;
        failed = 0;
        RunTest("window auto-detects pasted compact 645 despite manual uplink selection", delegate
        {
            AutoRecognizes(Read645, 2, "通信地址 A");
        });
        RunTest("window auto-detects pasted compact local despite manual 645 selection", delegate
        {
            AutoRecognizes(Local, 0, "总长度 L");
        });
        RunTest("window auto-detects pasted compact uplink despite manual local selection", delegate
        {
            AutoRecognizes(Uplink, 1, "省地市区县码 A1");
        });
        RunTest("window spaces and compact HEX produce identical formatted output", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                raw.Text = "  " + Local.ToLowerInvariant().Replace(" ", "\t ") + "\r\n";
                WaitFor(delegate { return formatted.Text == Local && HasField(grid, "总长度 L"); }, "Spaced input not parsed");
                raw.Text = Compact(Local);
                WaitFor(delegate { return formatted.Text == Local && HasField(grid, "总长度 L"); }, "Compact input differs");
                Check(raw.Text == Compact(Local), "Raw compact text changed");
            }
        });
        RunTest("window mixed continuous stream selects each frame and locates its bytes", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                ComboBox parts = Named<ComboBox>(form, "streamParts");
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                string expected = Read645 + Environment.NewLine + Local + Environment.NewLine + Uplink;
                raw.Text = Compact(Read645 + Local + Uplink);
                WaitFor(delegate { return parts.Items.Count == 3 && formatted.Text == expected; }, "Mixed stream not split into three lines");
                parts.SelectedIndex = 1;
                WaitFor(delegate { return HasField(grid, "总长度 L"); }, "Second frame did not show local fields");
                Check(!HasField(grid, "通信地址 A"), "Second frame retained first-frame fields");
                SelectField(grid, "数据标识 DI");
                Check(formatted.SelectedText == "01 03 00 E8", "Local DI selected the wrong bytes: text='"
                    + formatted.SelectedText + "', start=" + formatted.SelectionStart + ", current="
                    + ((FrameField)grid.CurrentRow.DataBoundItem).Name);
                Check(formatted.SelectionStart == Read645.Length + Environment.NewLine.Length + 6 * 3,
                    "Local byte selection did not include the previous formatted line");
                parts.SelectedIndex = 2;
                WaitFor(delegate { return HasField(grid, "省地市区县码 A1"); }, "Third frame did not show uplink fields");
                SelectField(grid, "首个数据标识 DI");
                Check(formatted.SelectedText == "00 00 01 00", "Uplink DI selected the wrong bytes");
                Check(formatted.SelectionStart == Read645.Length + Local.Length + Environment.NewLine.Length * 2 + 18 * 3,
                    "Uplink byte selection did not include both previous formatted lines");
                Check(raw.Text == Compact(Read645 + Local + Uplink), "Mixed raw text changed");
            }
        });
        RunTest("window appended half-byte tail becomes a second frame without losing input", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                ComboBox parts = Named<ComboBox>(form, "streamParts");
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                string compactLocal = Compact(Local);
                string partial = Compact(Read645) + compactLocal.Substring(0, 11);
                raw.Text = partial;
                WaitFor(delegate { return parts.Items.Count == 2 && Compact(formatted.Text) == partial; },
                    "Complete first frame or incomplete odd-nibble tail was lost");
                Check(raw.Text == partial, "Partial raw text changed");
                raw.AppendText(compactLocal.Substring(11));
                WaitFor(delegate { return parts.Items.Count == 2 && formatted.Text == Read645 + Environment.NewLine + Local; },
                    "Appending the tail did not complete the second frame");
                parts.SelectedIndex = 1;
                WaitFor(delegate { return HasField(grid, "总长度 L"); }, "Completed second frame did not parse as local");
                Check(raw.Text == Compact(Read645 + Local), "Completed raw text changed");
            }
        });
        RunTest("window invalid HEX clears stale valid results without a modal action", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                ComboBox parts = Named<ComboBox>(form, "streamParts");
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                Label status = Named<Label>(form, "parseStatus");
                raw.Text = Compact(Read645);
                WaitFor(delegate { return parts.Items.Count == 1 && HasField(grid, "通信地址 A"); }, "Valid baseline failed");
                raw.Text = Compact(Read645) + "ZZ";
                WaitFor(delegate
                {
                    return parts.Items.Count == 0 && Data(grid).Length == 0 && formatted.Text.Length == 0
                        && status.Text.Contains("错误");
                }, "Invalid input retained stale fields or lacked an inline error");
                Check(raw.Text.EndsWith("ZZ"), "Invalid input was silently discarded");
                Check(!status.Text.Contains("通过"), "Old successful status remained visible");
            }
        });
        RunTest("window parses while uninterrupted chunks continue arriving", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                ComboBox parts = Named<ComboBox>(form, "streamParts");
                Label status = Named<Label>(form, "parseStatus");
                raw.Clear();
                bool appending = true;
                int observedFramesDuringAppend = 0;
                status.TextChanged += delegate
                {
                    if (appending && status.Text.StartsWith("识别 ", StringComparison.Ordinal))
                    {
                        observedFramesDuringAppend = Math.Max(observedFramesDuringAppend, parts.Items.Count);
                    }
                };
                Stopwatch watch = Stopwatch.StartNew();
                int chunks = 0;
                long nextAppend = 0;
                string chunk = Compact(Read645);
                // Every 80 ms is faster than the 350 ms parse interval, so debounce starvation would fail.
                while (watch.ElapsedMilliseconds < 850)
                {
                    if (watch.ElapsedMilliseconds >= nextAppend)
                    {
                        raw.AppendText(chunk);
                        chunks++;
                        nextAppend += 80;
                    }
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                appending = false;
                Check(observedFramesDuringAppend > 0, "Continuous input postponed every parse until input stopped");
                string expectedRaw = string.Concat(Enumerable.Repeat(chunk, chunks));
                bool finalParseCompleted = false;
                while (watch.ElapsedMilliseconds < 1800)
                {
                    Application.DoEvents();
                    if (parts.Items.Count == chunks && Compact(formatted.Text) == expectedRaw)
                    {
                        finalParseCompleted = true;
                        break;
                    }
                    Thread.Sleep(10);
                }
                Check(finalParseCompleted, "The final continuous-input chunk was not parsed within the bound");
                Check(raw.Text == expectedRaw, "Continuous appending changed or lost raw input");
            }
        });
        RunTest("window ambiguous stream waits for manual protocol selection", delegate
        {
            using (MainForm form = NewForm())
            {
                CheckBox automatic = Named<CheckBox>(form, "automaticProtocol");
                ComboBox protocol = Named<ComboBox>(form, "protocol");
                ComboBox parts = Named<ComboBox>(form, "streamParts");
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                byte[] bytes = FrameCodec.BuildLocal(0x60, "8C1168000000", "000000000001", 3, 1,
                    Hex.Identifier("E8000301"), new byte[128]);
                string expected = Hex.Format(bytes);
                raw.Text = Compact(expected);
                WaitFor(delegate
                {
                    return parts.Items.Count == 1 && formatted.Text == expected && Data(grid).Length == 0
                        && parts.Items[0].ToString().Contains("歧义");
                }, "Dual-valid frame was silently assigned a protocol");
                protocol.SelectedIndex = 1;
                automatic.Checked = false;
                WaitFor(delegate { return HasField(grid, "总长度 L"); }, "Manual local choice did not resolve ambiguity");
                protocol.SelectedIndex = 0;
                WaitFor(delegate { return HasField(grid, "通信地址 A"); }, "Manual 645 choice did not resolve ambiguity");
                Check(raw.Text == Compact(expected), "Resolving ambiguity changed raw input");
            }
        });
        RunTest("window manual mode respects the selected protocol", delegate
        {
            using (MainForm form = NewForm())
            {
                CheckBox automatic = Named<CheckBox>(form, "automaticProtocol");
                ComboBox protocol = Named<ComboBox>(form, "protocol");
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                automatic.Checked = false;
                protocol.SelectedIndex = 1;
                raw.Text = Compact(Local);
                WaitFor(delegate { return HasField(grid, "总长度 L") && formatted.Text == Local; }, "Manual local parse failed");
                protocol.SelectedIndex = 0;
                PumpFor(600);
                Check(!HasField(grid, "总长度 L"), "Manual 645 selection was overridden by local auto-detection");
                Check(!automatic.Checked && raw.Text == Compact(Local), "Manual mode or input changed unexpectedly");
            }
        });
        Console.WriteLine("WINDOW RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
