using System;
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
        return (T)form.Controls.Find(name, true).Single();
    }

    private static MainForm NewForm()
    {
        MainForm form = new MainForm();
        form.BindingContext = new BindingContext();
        IntPtr window = form.Handle;
        IntPtr tabs = Named<TabControl>(form, "workspaceTabs").Handle;
        IntPtr grid = Named<DataGridView>(form, "parsedFields").Handle;
        IntPtr batch = Named<DataGridView>(form, "batchSummary").Handle;
        IntPtr details = Named<DataGridView>(form, "batchFields").Handle;
        Check(window != IntPtr.Zero && tabs != IntPtr.Zero && grid != IntPtr.Zero
            && batch != IntPtr.Zero && details != IntPtr.Zero, "Handles missing");
        return form;
    }

    private static void Click(Button button)
    {
        Check(button.Enabled, "Disabled button: " + button.Text);
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button, new object[] { EventArgs.Empty });
        Application.DoEvents();
    }

    private static void Recognize(MainForm form)
    {
        Click(Named<Button>(form, "parseFrames"));
    }

    private static void Batch(MainForm form)
    {
        Named<TabControl>(form, "workspaceTabs").SelectedIndex = 1;
        Click(Named<Button>(form, "parseBatch"));
    }

    private static string Compact(string value)
    {
        return string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
    }

    private static FrameField[] Data(DataGridView grid)
    {
        System.Collections.Generic.IEnumerable<FrameField> data = grid.DataSource as System.Collections.Generic.IEnumerable<FrameField>;
        return data == null ? new FrameField[0] : data.ToArray();
    }

    private static bool HasField(DataGridView grid, string name)
    {
        return Data(grid).Any(f => f.Name == name);
    }

    private static void SelectField(DataGridView grid, string name)
    {
        DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().Single(r => ((FrameField)r.DataBoundItem).Name == name);
        grid.CurrentCell = row.Cells[0];
        Application.DoEvents();
    }

    private static void PumpFor(int milliseconds)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void CheckUnparsed(MainForm form)
    {
        Check(Data(Named<DataGridView>(form, "parsedFields")).Length == 0
            && Named<DataGridView>(form, "batchSummary").Rows.Count == 0, "Recognition happened without click");
    }

    private static void RunTest(string name, Action action)
    {
        try
        {
            action();
            Passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + e.Message);
        }
    }

    private static void Single(string frame, int otherProtocol, string field)
    {
        using (MainForm form = NewForm())
        {
            Named<ComboBox>(form, "protocol").SelectedIndex = otherProtocol;
            TextBox raw = Named<TextBox>(form, "rawInput");
            raw.Text = Compact(frame);
            PumpFor(450);
            CheckUnparsed(form);
            Recognize(form);
            Check(HasField(Named<DataGridView>(form, "parsedFields"), field), "Wrong automatic protocol");
            Check(Named<TextBox>(form, "formattedStream").Text == frame, "Formatting changed");
            Check(raw.Text == Compact(frame), "Raw text changed");
        }
    }

    public static int Run()
    {
        Passed = 0;
        failed = 0;
        RunTest("window startup leaves single and batch workspaces empty", delegate
        {
            using (MainForm form = NewForm())
            {
                PumpFor(450);
                Check(Named<TextBox>(form, "rawInput").TextLength == 0 && Named<TextBox>(form, "batchInput").TextLength == 0, "Startup loaded sample");
                CheckUnparsed(form);
                Check(Named<TabControl>(form, "workspaceTabs").TabPages.Count == 3, "Missing separate pages");
            }
        });
        RunTest("single click identifies 645", delegate { Single(Read645, 2, "通信地址 A"); });
        RunTest("single click identifies local", delegate { Single(Local, 0, "总长度 L"); });
        RunTest("single click identifies uplink", delegate { Single(Uplink, 1, "省地市区县码 A1"); });
        RunTest("automatic mode disables manual choice on both parser pages, not builder", delegate
        {
            using (MainForm form = NewForm())
            {
                ComboBox protocol = Named<ComboBox>(form, "protocol");
                CheckBox automatic = Named<CheckBox>(form, "automaticProtocol");
                TabControl tabs = Named<TabControl>(form, "workspaceTabs");
                Check(!protocol.Enabled, "Manual picker enabled");
                tabs.SelectedIndex = 1;
                Check(!protocol.Enabled, "Batch manual picker enabled");
                automatic.Checked = false;
                Check(protocol.Enabled, "Manual choice unavailable");
                automatic.Checked = true;
                tabs.SelectedIndex = 2;
                Check(protocol.Enabled && !automatic.Visible, "Builder blocked");
                protocol.SelectedIndex = 2;
                Click(Named<Button>(form, "composeFrame"));
                Check(FrameCodec.Parse(ProtocolKind.CsgUplink, Named<TextBox>(form, "builtFrame").Text).IsFrameValid, "Builder failed");
                tabs.SelectedIndex = 0;
                Check(!protocol.Enabled, "Parser mode not restored");
                CheckUnparsed(form);
            }
        });
        RunTest("single spaces and compact input remain equivalent", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                raw.Text = Local.ToLowerInvariant().Replace(" ", "\t ");
                Recognize(form);
                string formatted = Named<TextBox>(form, "formattedStream").Text;
                raw.Text = Compact(Local);
                CheckUnparsed(form);
                Recognize(form);
                Check(formatted == Local && Named<TextBox>(form, "formattedStream").Text == Local, "Different input formats");
            }
        });
        RunTest("native business input preserves legal payload beyond 32767 characters", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<TabControl>(form, "workspaceTabs").SelectedIndex = 2;
                Named<ComboBox>(form, "protocol").SelectedIndex = 2;
                TextBox box = Named<TextBox>(form, "businessInput");
                byte[] bytes = Enumerable.Repeat((byte)0xAA, 13334).ToArray();
                string expected = Hex.Format(bytes);
                box.Clear();
                foreach (char c in expected)
                {
                    SendMessage(box.Handle, 0x0102, (IntPtr)c, IntPtr.Zero);
                }
                Check(box.Text == expected && Hex.Parse(box.Text).SequenceEqual(bytes), "Native input truncated");
            }
        });
        RunTest("batch mixed stream shows independent summaries and selected fields", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "batchInput");
                raw.Text = Compact(Read645 + Local + Uplink);
                Batch(form);
                DataGridView list = Named<DataGridView>(form, "batchSummary");
                DataGridView fields = Named<DataGridView>(form, "batchFields");
                TextBox frame = Named<TextBox>(form, "batchSelectedFrame");
                Check(list.Rows.Count == 3, "Wrong frame count");
                list.CurrentCell = list.Rows[1].Cells[0];
                Check(HasField(fields, "总长度 L") && frame.Text == Local, "Wrong local details");
                SelectField(fields, "数据标识 DI");
                Check(frame.SelectedText == "01 03 00 E8" && frame.SelectionStart == 18, "Wrong local highlight");
                list.CurrentCell = list.Rows[2].Cells[0];
                SelectField(fields, "首个数据标识 DI");
                Check(frame.SelectedText == "00 00 01 00" && frame.SelectionStart == 54, "Wrong uplink highlight");
                Check(raw.Text == Compact(Read645 + Local + Uplink), "Batch input changed");
            }
        });
        RunTest("batch partial tail is retained and completes after another click", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "batchInput");
                string tail = Compact(Local);
                raw.Text = Compact(Read645) + tail.Substring(0, 11);
                Batch(form);
                Check(Named<DataGridView>(form, "batchSummary").Rows.Count == 2
                    && Named<Label>(form, "batchStatus").Text.Contains("半字节"), "Partial tail lost");
                raw.AppendText(tail.Substring(11));
                PumpFor(450);
                CheckUnparsed(form);
                Batch(form);
                Check(Named<DataGridView>(form, "batchSummary").Rows.Count == 2
                    && Named<Label>(form, "batchStatus").Text.Contains("通过 2"), "Tail did not complete");
            }
        });
        RunTest("invalid single HEX reports inline only after clicking", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "rawInput");
                raw.Text = Read645;
                Recognize(form);
                raw.Text += "ZZ";
                PumpFor(450);
                CheckUnparsed(form);
                Recognize(form);
                Check(Named<Label>(form, "parseStatus").Text.Contains("错误"), "Missing format error");
            }
        });
        RunTest("batch appends wait for click and preserve all frames", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBox raw = Named<TextBox>(form, "batchInput");
                for (int i = 0; i < 6; i++)
                {
                    raw.AppendText(Compact(Read645));
                    PumpFor(80);
                    CheckUnparsed(form);
                }
                Batch(form);
                Check(Named<DataGridView>(form, "batchSummary").Rows.Count == 6, "Appended frames missing");
            }
        });
        RunTest("single ambiguity still requires an explicit manual choice", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<TextBox>(form, "rawInput").Text = Hex.Format(FrameCodec.BuildLocal(0x60, "8C1168000000", "000000000001", 3, 1, Hex.Identifier("E8000301"), new byte[128]));
                Recognize(form);
                Check(Data(Named<DataGridView>(form, "parsedFields")).Length == 0, "Ambiguity silently resolved");
                Named<CheckBox>(form, "automaticProtocol").Checked = false;
                Named<ComboBox>(form, "protocol").SelectedIndex = 1;
                Recognize(form);
                Check(HasField(Named<DataGridView>(form, "parsedFields"), "总长度 L"), "Manual local failed");
            }
        });
        RunTest("manual parser selection does not revert to automatic", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<CheckBox>(form, "automaticProtocol").Checked = false;
                Named<ComboBox>(form, "protocol").SelectedIndex = 1;
                Named<TextBox>(form, "rawInput").Text = Local;
                Recognize(form);
                Check(HasField(Named<DataGridView>(form, "parsedFields"), "总长度 L"), "Manual local failed");
                Named<ComboBox>(form, "protocol").SelectedIndex = 0;
                Recognize(form);
                Check(!HasField(Named<DataGridView>(form, "parsedFields"), "总长度 L"), "Manual choice overridden");
            }
        });
        RunTest("single rejects multiple frames and transfers input without parsing", delegate
        {
            using (MainForm form = NewForm())
            {
                string raw = Compact(Read645 + Local);
                Named<TextBox>(form, "rawInput").Text = raw;
                Recognize(form);
                Check(Data(Named<DataGridView>(form, "parsedFields")).Length == 0
                    && Named<Label>(form, "parseStatus").Text.Contains("多帧"), "Single silently dropped extra frame");
                Click(Named<Button>(form, "sendToBatch"));
                Check(Named<TextBox>(form, "batchInput").Text == raw
                    && Named<DataGridView>(form, "batchSummary").Rows.Count == 0, "Transfer changed or parsed data");
                Batch(form);
                Check(Named<DataGridView>(form, "batchSummary").Rows.Count == 2, "Transferred batch failed");
            }
        });
        RunTest("minimum window keeps batch controls and theme button within parent bounds", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<TextBox>(form, "batchInput").Text = Compact(Read645 + Local + Uplink);
                Batch(form);
                form.Size = form.MinimumSize;
                form.PerformLayout();
                Application.DoEvents();
                foreach (string name in new[] { "batchInput", "batchSplit", "batchFields", "themeToggle" })
                {
                    for (Control view = Named<Control>(form, name); view.Parent != null; view = view.Parent)
                    {
                        Check(view.Parent.ClientRectangle.Contains(view.Bounds), name + " clipped by " + view.Parent.GetType().Name);
                    }
                }
                SplitContainer split = Named<SplitContainer>(form, "batchSplit");
                Check(split.Panel2.Width >= split.Panel2MinSize, "Details panel below minimum width");
                Check(Named<DataGridView>(form, "batchSummary").Rows.Count == 3, "Resize lost frames");
            }
        });
        RunTest("theme switches dark-light-dark without altering input or parsed results", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<TextBox>(form, "rawInput").Text = Read645;
                Recognize(form);
                Named<TextBox>(form, "batchInput").Text = Compact(Read645 + Local);
                Batch(form);
                DataGridView list = Named<DataGridView>(form, "batchSummary");
                list.CurrentCell = list.Rows[1].Cells[0];
                object selected = list.CurrentRow.Tag;
                TextBox details = Named<TextBox>(form, "batchSelectedFrame");
                string original = details.Text;
                Button theme = Named<Button>(form, "themeToggle");
                Check(WorkspaceStyle.IsDark, "Default theme differs from reference");
                Click(theme);
                Check(!WorkspaceStyle.IsDark && details.BackColor == WorkspaceStyle.Surface
                    && list.CurrentRow.Tag == selected && details.Text == original && list.Rows.Count == 2, "Light switch lost data or selection");
                Check(theme.AccessibleName.Contains("暗色"), "Wrong theme action");
                Click(theme);
                Check(WorkspaceStyle.IsDark && details.Text == original
                    && Named<TextBox>(form, "rawInput").Text == Read645, "Dark switch changed input");
            }
        });
        Console.WriteLine("WINDOW RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
