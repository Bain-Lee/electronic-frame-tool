using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr value, IntPtr extra);

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
        // Tab selection events require a native tab handle, as in a displayed production form.
        IntPtr formHandle = form.Handle;
        IntPtr tabHandle = Named<TabControl>(form, "workspaceTabs").Handle;
        IntPtr gridHandle = Named<DataGridView>(form, "parsedFields").Handle;
        Check(formHandle != IntPtr.Zero && tabHandle != IntPtr.Zero && gridHandle != IntPtr.Zero, "Window handles were not created");
        return form;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Click(Button button)
    {
        Check(button.Enabled, "Button is disabled: " + button.Text);
        // Dispatch the actual Click handler without showing a desktop test window.
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button, new object[] { EventArgs.Empty });
        Application.DoEvents();
    }

    private static void Recognize(MainForm form)
    {
        Click(Named<Button>(form, "parseFrames"));
    }

    private static void CheckUnparsed(MainForm form, string message)
    {
        Check(Named<ComboBox>(form, "streamParts").Items.Count == 0
            && Named<TextBox>(form, "formattedStream").TextLength == 0
            && Data(Named<DataGridView>(form, "parsedFields")).Length == 0, message);
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

    private static void AutoRecognizesOnClick(string frame, int otherProtocol, string expectedField)
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
            PumpFor(450);
            CheckUnparsed(form, "Pasting started recognition before the user clicked");
            Recognize(form);
            WaitFor(delegate { return parts.Items.Count == 1 && formatted.Text == frame && HasField(grid, expectedField); },
                "Click did not identify and parse the pasted frame");
            Check(raw.Text == Compact(frame), "Automatic formatting overwrote the raw input");
            Check(formatted.ReadOnly, "Formatted output must be read-only");
        }
    }

    public static int Run()
    {
        Passed = 0;
        failed = 0;
        RunTest("window startup stays empty without recognition", delegate
        {
            using (MainForm form = NewForm())
            {
                PumpFor(450);
                Check(Named<TextBox>(form, "rawInput").TextLength == 0, "Startup loaded an unsolicited example");
                CheckUnparsed(form, "Startup performed recognition or populated old results");
            }
        });
        RunTest("window click identifies compact 645 despite manual uplink selection", delegate
        {
            AutoRecognizesOnClick(Read645, 2, "通信地址 A");
        });
        RunTest("window click identifies compact local despite manual 645 selection", delegate
        {
            AutoRecognizesOnClick(Local, 0, "总长度 L");
        });
        RunTest("window click identifies compact uplink despite manual local selection", delegate
        {
            AutoRecognizesOnClick(Uplink, 1, "省地市区县码 A1");
        });
        RunTest("window automatic mode disables manual parsing while builder selection remains usable", delegate
        {
            using (MainForm form = NewForm())
            {
                CheckBox automatic = Named<CheckBox>(form, "automaticProtocol");
                ComboBox protocol = Named<ComboBox>(form, "protocol");
                TabControl tabs = Named<TabControl>(form, "workspaceTabs");
                Check(automatic.Checked && !protocol.Enabled, "Automatic parsing left manual protocol enabled");
                automatic.Checked = false;
                Check(protocol.Enabled, "Manual protocol did not become available");
                automatic.Checked = true;
                Check(!protocol.Enabled, "Automatic mode did not disable manual selection");
                tabs.SelectedIndex = 1;
                Check(protocol.Enabled && !automatic.Visible, "Builder protocol was blocked by automatic parsing mode");
                protocol.SelectedIndex = 2;
                Button generate = Descendants(form).OfType<Button>().Single(button => button.Text.StartsWith("生成报文", StringComparison.Ordinal));
                Click(generate);
                TextBox result = Named<TextBox>(form, "builtFrame");
                Check(result.TextLength > 0 && FrameCodec.Parse(ProtocolKind.CsgUplink, result.Text).IsFrameValid,
                    "Builder could not generate an uplink frame in automatic parsing mode");
                tabs.SelectedIndex = 0;
                Check(automatic.Checked && !protocol.Enabled, "Returning to parsing did not restore automatic mode");
                CheckUnparsed(form, "Changing parsing or builder mode performed recognition");
            }
        });
        RunTest("window spaces and compact HEX produce identical formatted output", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                raw.Text = "  " + Local.ToLowerInvariant().Replace(" ", "\t ") + "\r\n";
                Recognize(form);
                WaitFor(delegate { return formatted.Text == Local && HasField(grid, "总长度 L"); }, "Spaced input not parsed");
                raw.Text = Compact(Local);
                CheckUnparsed(form, "Changing input retained the previous parsed result");
                Recognize(form);
                WaitFor(delegate { return formatted.Text == Local && HasField(grid, "总长度 L"); }, "Compact input differs");
                Check(raw.Text == Compact(Local), "Raw compact text changed");
            }
        });
        RunTest("window native business-data editing preserves a valid-sized payload beyond 32767 characters", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<TabControl>(form, "workspaceTabs").SelectedIndex = 1;
                Named<ComboBox>(form, "protocol").SelectedIndex = 2;
                TextBox business = Named<TextBox>(form, "businessInput");
                byte[] application = Enumerable.Repeat((byte)0xAA, 13334).ToArray();
                string expected = Hex.Format(application);
                byte[] frame = FrameCodec.BuildUplink(0x4B, Hex.Parse("06 01 44 01 00 00 01"), 0x0C, 0x60, application);
                Check(frame.Length == application.Length + 18 && expected.Length > 32767, "Expected a legal-sized long HEX payload");
                business.Clear();
                IntPtr handle = business.Handle;
                foreach (char character in expected)
                {
                    // WM_CHAR follows native editing limits; assigning Text or SelectedText bypasses them.
                    SendMessage(handle, 0x0102, (IntPtr)character, IntPtr.Zero);
                }
                Check(business.Text == expected, "Native input truncated " + expected.Length + " characters to " + business.TextLength);
                Check(Hex.Parse(business.Text).SequenceEqual(application), "Business-data bytes changed during editing");
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
                Recognize(form);
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
                Recognize(form);
                WaitFor(delegate { return parts.Items.Count == 2 && Compact(formatted.Text) == partial; },
                    "Complete first frame or incomplete odd-nibble tail was lost");
                Check(raw.Text == partial, "Partial raw text changed");
                raw.AppendText(compactLocal.Substring(11));
                PumpFor(450);
                CheckUnparsed(form, "Appending a tail parsed without a click");
                Recognize(form);
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
                Recognize(form);
                WaitFor(delegate { return parts.Items.Count == 1 && HasField(grid, "通信地址 A"); }, "Valid baseline failed");
                raw.Text = Compact(Read645) + "ZZ";
                PumpFor(450);
                CheckUnparsed(form, "Invalid edited text retained prior results");
                Check(!status.Text.Contains("错误"), "Editing started format validation before recognition");
                Recognize(form);
                WaitFor(delegate
                {
                    return parts.Items.Count == 0 && Data(grid).Length == 0 && formatted.Text.Length == 0
                        && status.Text.Contains("错误");
                }, "Invalid input retained stale fields or lacked an inline error");
                Check(raw.Text.EndsWith("ZZ"), "Invalid input was silently discarded");
                Check(!status.Text.Contains("通过"), "Old successful status remained visible");
            }
        });
        RunTest("window appended stream waits for click and then parses every frame", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                TextBox formatted = Named<TextBox>(form, "formattedStream");
                ComboBox parts = Named<ComboBox>(form, "streamParts");
                raw.Clear();
                int chunks = 6;
                string chunk = Compact(Read645);
                for (int i = 0; i < chunks; i++)
                {
                    raw.AppendText(chunk);
                    PumpFor(80);
                    CheckUnparsed(form, "Appending automatically started recognition");
                }
                string expectedRaw = string.Concat(Enumerable.Repeat(chunk, chunks));
                PumpFor(450);
                CheckUnparsed(form, "The final appended stream parsed without recognition");
                Recognize(form);
                WaitFor(delegate { return parts.Items.Count == chunks && Compact(formatted.Text) == expectedRaw; },
                    "Recognition did not parse every appended frame");
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
                Recognize(form);
                WaitFor(delegate
                {
                    return parts.Items.Count == 1 && formatted.Text == expected && Data(grid).Length == 0
                        && parts.Items[0].ToString().Contains("歧义");
                }, "Dual-valid frame was silently assigned a protocol");
                protocol.SelectedIndex = 1;
                automatic.Checked = false;
                CheckUnparsed(form, "Manual protocol change implicitly resolved ambiguity");
                Recognize(form);
                WaitFor(delegate { return HasField(grid, "总长度 L"); }, "Manual local choice did not resolve ambiguity");
                protocol.SelectedIndex = 0;
                Recognize(form);
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
                Recognize(form);
                WaitFor(delegate { return HasField(grid, "总长度 L") && formatted.Text == Local; }, "Manual local parse failed");
                protocol.SelectedIndex = 0;
                PumpFor(450);
                CheckUnparsed(form, "Manual protocol switch started recognition without a click");
                Recognize(form);
                Check(!HasField(grid, "总长度 L"), "Manual 645 selection was overridden by local auto-detection");
                Check(!automatic.Checked && raw.Text == Compact(Local), "Manual mode or input changed unexpectedly");
            }
        });
        Console.WriteLine("WINDOW RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
