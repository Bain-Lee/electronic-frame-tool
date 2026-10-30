using System;
using System.Diagnostics;
using System.Drawing;
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
    private const string LocalVersion = "68 15 00 80 03 01 01 03 00 E8 41 42 43 44 28 10 26 34 12 1E 16";
    private const string Uplink = "68 10 00 10 00 68 4B 06 01 44 01 00 00 01 0C 60 01 01 00 00 01 00 07 16";
    private static int failed;
    public static int Passed { get; private set; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr value, IntPtr extra);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static T Named<T>(Control form, string name) where T : Control
    {
        return (T)form.Controls.Find(name, true).Single();
    }

    private static T Parameter<T>(MainForm form, string name) where T : Control
    {
        return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    private static MainForm NewForm()
    {
        MainForm form = new MainForm();
        Named<TabControl>(form, "composerModes").SelectedIndex = 1;
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

    private static void FieldKey(DataGridView grid, Keys key)
    {
        grid.GetType().GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(grid, new object[] { new KeyEventArgs(key) });
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

    private static void Purpose(MainForm form, int protocol, string key)
    {
        Named<TabControl>(form, "workspaceTabs").SelectedIndex = 2;
        Named<TabControl>(form, "composerModes").SelectedIndex = 0;
        Named<ComboBox>(form, "protocol").SelectedIndex = protocol;
        PurposeCommand command = PurposeComposer.GetCommands((ProtocolKind)protocol).Single(item => item.Key == key);
        Named<ListBox>(form, "purposeCategory").SelectedItem = command.Category;
        Named<ComboBox>(form, "purposeCommand").SelectedItem = command;
        Application.DoEvents();
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
            TextBoxBase raw = Named<TextBoxBase>(form, "rawInput");
            raw.Text = Compact(frame);
            PumpFor(450);
            CheckUnparsed(form);
            Recognize(form);
            int expected = frame == Read645 ? 0 : frame == Local ? 1 : 2;
            Check(Named<ComboBox>(form, "protocol").SelectedIndex == expected, "Detected protocol not reflected in picker");
            Check(HasField(Named<DataGridView>(form, "parsedFields"), field), "Wrong automatic protocol");
            DataGridView grid = Named<DataGridView>(form, "parsedFields");
            SelectField(grid, field);
            Check(Hex.Compact(raw.SelectedText) == Hex.Compact(Data(grid).Single(f => f.Name == field).Raw), "Field not located in original input");
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
                Check(Named<TextBoxBase>(form, "rawInput").TextLength == 0 && Named<TextBoxBase>(form, "batchInput").TextLength == 0, "Startup loaded sample");
                CheckUnparsed(form);
                Check(Named<TabControl>(form, "workspaceTabs").TabPages.Count == 3, "Missing separate pages");
                Check(!WorkspaceStyle.IsDark, "Startup did not use light theme");
                Check(form.Controls.Find("formattedStream", true).Length == 0, "Single frame still has redundant formatted pane");
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
                Check(FrameCodec.Parse(ProtocolKind.CsgUplink, Named<TextBoxBase>(form, "builtFrame").Text).IsFrameValid, "Builder failed");
                tabs.SelectedIndex = 0;
                Check(!protocol.Enabled, "Parser mode not restored");
                CheckUnparsed(form);
            }
        });
        RunTest("protocol reflection preserves composer values and parser results", delegate
        {
            using (MainForm form = NewForm())
            {
                TabControl tabs = Named<TabControl>(form, "workspaceTabs");
                ComboBox protocol = Named<ComboBox>(form, "protocol");
                tabs.SelectedIndex = 2;
                protocol.SelectedIndex = 1;
                Named<TextBoxBase>(form, "businessInput").Text = "AA BB";
                Click(Named<Button>(form, "composeFrame"));
                string built = Named<TextBoxBase>(form, "builtFrame").Text;
                tabs.SelectedIndex = 0;
                Named<TextBoxBase>(form, "rawInput").Text = Uplink;
                Recognize(form);
                Check(protocol.SelectedIndex == 2 && HasField(Named<DataGridView>(form, "parsedFields"), "省地市区县码 A1"), "Reflection cleared parser result");
                tabs.SelectedIndex = 2;
                Check(protocol.SelectedIndex == 1 && Named<TextBoxBase>(form, "businessInput").Text == "AA BB"
                    && Named<TextBoxBase>(form, "builtFrame").Text == built, "Reflection reset composer");
                tabs.SelectedIndex = 0;
                Check(protocol.SelectedIndex == 2, "Parser protocol display not restored");
            }
        });
        RunTest("unrecognized input clears automatic protocol display", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBoxBase raw = Named<TextBoxBase>(form, "rawInput");
                raw.Text = Local;
                Recognize(form);
                raw.Text = "AA BB";
                Recognize(form);
                Check(Named<ComboBox>(form, "protocol").SelectedIndex == -1, "Unknown input kept stale detected protocol");
            }
        });
        RunTest("single spaces and compact input remain equivalent", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBoxBase raw = Named<TextBoxBase>(form, "rawInput");
                raw.Text = Local.ToLowerInvariant().Replace(" ", "\t ");
                Recognize(form);
                string[] spaced = Data(Named<DataGridView>(form, "parsedFields")).Select(f => f.Offset + ":" + f.Raw).ToArray();
                raw.Text = Compact(Local);
                CheckUnparsed(form);
                Recognize(form);
                Check(spaced.SequenceEqual(Data(Named<DataGridView>(form, "parsedFields")).Select(f => f.Offset + ":" + f.Raw)), "Different input formats");
            }
        });
        RunTest("native business input preserves legal payload beyond 32767 characters", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<TabControl>(form, "workspaceTabs").SelectedIndex = 2;
                Named<ComboBox>(form, "protocol").SelectedIndex = 2;
                TextBoxBase box = Named<TextBoxBase>(form, "businessInput");
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
        RunTest("single field selection follows compact, separated and prefixed HEX", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBoxBase raw = Named<TextBoxBase>(form, "rawInput");
                foreach (string text in new[]
                {
                    Compact(Read645),
                    Read645.ToLowerInvariant().Replace(" ", " ,\r\n "),
                    "0xFE:0xFE-" + string.Join(", ", Read645.Split(' ').Select(b => "0x" + b))
                })
                {
                    raw.Text = text;
                    string original = raw.Text;
                    Recognize(form);
                    SelectField(Named<DataGridView>(form, "parsedFields"), "数据标识 DI");
                    Check(Hex.Compact(raw.SelectedText) == "33333433", "Wrong original HEX selection");
                    Check(raw.Text == original, "Field selection rewrote original input");
                }
            }
        });
        RunTest("batch mixed stream shows independent summaries and selected fields", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBoxBase raw = Named<TextBoxBase>(form, "batchInput");
                raw.Text = Compact(Read645 + Local + Uplink);
                Batch(form);
                DataGridView list = Named<DataGridView>(form, "batchSummary");
                DataGridView fields = Named<DataGridView>(form, "batchFields");
                TextBoxBase frame = Named<TextBoxBase>(form, "batchSelectedFrame");
                Check(list.Rows.Count == 3, "Wrong frame count");
                list.CurrentCell = list.Rows[1].Cells[0];
                Check(Named<ComboBox>(form, "protocol").SelectedIndex == 1, "Batch selection did not reflect local protocol");
                Check(HasField(fields, "总长度 L") && frame.Text == Local, "Wrong local details");
                SelectField(fields, "数据标识 DI");
                Check(frame.SelectedText == "01 03 00 E8" && frame.SelectionStart == 18, "Wrong local highlight");
                list.CurrentCell = list.Rows[2].Cells[0];
                Check(Named<ComboBox>(form, "protocol").SelectedIndex == 2, "Batch selection did not reflect uplink protocol");
                SelectField(fields, "首个数据标识 DI");
                Check(frame.SelectedText == "00 00 01 00" && frame.SelectionStart == 54, "Wrong uplink highlight");
                Check(raw.Text == Compact(Read645 + Local + Uplink), "Batch input changed");
            }
        });
        RunTest("batch partial tail is retained and completes after another click", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBoxBase raw = Named<TextBoxBase>(form, "batchInput");
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
                TextBoxBase raw = Named<TextBoxBase>(form, "rawInput");
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
                TextBoxBase raw = Named<TextBoxBase>(form, "batchInput");
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
                Named<TextBoxBase>(form, "rawInput").Text = Hex.Format(FrameCodec.BuildLocal(0x60, "8C1168000000", "000000000001", 3, 1, Hex.Identifier("E8000301"), new byte[128]));
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
                Named<TextBoxBase>(form, "rawInput").Text = Local;
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
                Named<TextBoxBase>(form, "rawInput").Text = raw;
                Recognize(form);
                Check(Data(Named<DataGridView>(form, "parsedFields")).Length == 0
                    && Named<Label>(form, "parseStatus").Text.Contains("多帧"), "Single silently dropped extra frame");
                Click(Named<Button>(form, "sendToBatch"));
                Check(Named<TextBoxBase>(form, "batchInput").Text == raw
                    && Named<DataGridView>(form, "batchSummary").Rows.Count == 0, "Transfer changed or parsed data");
                Batch(form);
                Check(Named<DataGridView>(form, "batchSummary").Rows.Count == 2, "Transferred batch failed");
            }
        });
        RunTest("minimum window keeps batch controls and theme button within parent bounds", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<TextBoxBase>(form, "batchInput").Text = Compact(Read645 + Local + Uplink);
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
        RunTest("all multiline editors show scrollbars only for overflowing content", delegate
        {
            using (MainForm form = NewForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-16000, -16000);
                form.Show();
                string[][] editors =
                {
                    new[] { "rawInput", "parseDiagnostics" },
                    new[] { "batchInput", "batchSelectedFrame", "batchDiagnostics" },
                    new[] { "businessInput", "builtFrame" }
                };
                for (int page = 0; page < editors.Length; page++)
                {
                    Named<TabControl>(form, "workspaceTabs").SelectedIndex = page;
                    foreach (string name in editors[page])
                    {
                        TextBoxBase editor = Named<TextBoxBase>(form, name);
                        editor.Text = "68 16";
                        Application.DoEvents();
                        Check((GetWindowLong(editor.Handle, -16) & 0x00300000) == 0, name + " shows scrollbars for short text");
                        editor.Text = string.Join(Environment.NewLine, Enumerable.Repeat("68 16", 100));
                        Application.DoEvents();
                        Check((GetWindowLong(editor.Handle, -16) & 0x00200000) != 0, name + " hides overflowing vertical scrollbar");
                        SendMessage(editor.Handle, 0x0115, (IntPtr)7, IntPtr.Zero); // WM_VSCROLL / SB_BOTTOM.
                        Check((int)SendMessage(editor.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero) > 0, name + " cannot scroll to overflow text");
                        editor.Clear();
                        Application.DoEvents();
                        Check((GetWindowLong(editor.Handle, -16) & 0x00300000) == 0, name + " keeps empty scrollbars");
                    }
                }
            }
        });
        RunTest("editor resizing and wrapping update native scrollbar visibility", delegate
        {
            using (Form host = new Form())
            using (WorkspaceTextBox editor = new WorkspaceTextBox { Size = new Size(240, 60), Font = new Font("Consolas", 10F) })
            {
                host.ShowInTaskbar = false;
                host.StartPosition = FormStartPosition.Manual;
                host.Location = new Point(-16000, -16000);
                host.Controls.Add(editor);
                host.Show();
                editor.Text = string.Join(Environment.NewLine, Enumerable.Repeat("68 16", 10));
                Application.DoEvents();
                Check((GetWindowLong(editor.Handle, -16) & 0x00200000) != 0, "Missing vertical overflow scrollbar");
                editor.Height = 300;
                Application.DoEvents();
                Check((GetWindowLong(editor.Handle, -16) & 0x00300000) == 0, "Resize kept unnecessary scrollbars");
                editor.Height = 60;
                editor.Text = new string('A', 400);
                Application.DoEvents();
                Check((GetWindowLong(editor.Handle, -16) & 0x00300000) == 0x00200000, "Wrapped input does not scroll vertically");
                editor.WordWrap = false;
                Application.DoEvents();
                Check((GetWindowLong(editor.Handle, -16) & 0x00300000) == 0x00100000, "Unwrapped input does not scroll horizontally");
                editor.Text = "68 16";
                Application.DoEvents();
                Check((GetWindowLong(editor.Handle, -16) & 0x00300000) == 0, "Shortened line kept scrollbars");
            }
        });
        RunTest("theme switches light-dark-light without altering input or parsed results", delegate
        {
            using (MainForm form = NewForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-16000, -16000);
                form.Show();
                Named<TextBoxBase>(form, "rawInput").Text = Read645;
                Recognize(form);
                Named<TextBoxBase>(form, "batchInput").Text = Compact(Read645 + Local);
                Batch(form);
                DataGridView list = Named<DataGridView>(form, "batchSummary");
                list.CurrentCell = list.Rows[1].Cells[0];
                object selected = list.CurrentRow.Tag;
                TextBoxBase details = Named<TextBoxBase>(form, "batchSelectedFrame");
                string original = details.Text;
                Button theme = Named<Button>(form, "themeToggle");
                Check(!WorkspaceStyle.IsDark, "Default theme is not light");
                Click(theme);
                Check(WorkspaceStyle.IsDark && details.BackColor == WorkspaceStyle.Surface
                    && list.CurrentRow.Tag == selected && details.Text == original && list.Rows.Count == 2, "Dark switch lost data or selection");
                Check(theme.AccessibleName.Contains("亮色"), "Wrong theme action");
                Click(theme);
                Check(!WorkspaceStyle.IsDark && details.Text == original
                    && Named<TextBoxBase>(form, "rawInput").Text == Read645, "Light switch changed input");
            }
        });
        RunTest("local field groups fold independently and preserve nested state and raw selection", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBoxBase raw = Named<TextBoxBase>(form, "rawInput");
                raw.Text = LocalVersion;
                Recognize(form);
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                Check(HasField(grid, "用户数据域") && HasField(grid, "应用数据域")
                    && HasField(grid, "DIR") && HasField(grid, "厂商代码"), "Missing expanded field groups");
                SelectField(grid, "控制域 C");
                FieldKey(grid, Keys.Left);
                Check(!HasField(grid, "DIR") && HasField(grid, "应用功能 AFN"), "Control collapse hid unrelated data");
                Check(raw.SelectedText == "80", "Collapsed control lost original byte selection");
                FieldKey(grid, Keys.Right);
                Check(HasField(grid, "DIR"), "Control did not expand");
                SelectField(grid, "数据标识内容");
                FieldKey(grid, Keys.Left);
                Check(!HasField(grid, "厂商代码") && HasField(grid, "数据标识 DI"), "Content collapse lost sibling DI");
                SelectField(grid, "用户数据域");
                FieldKey(grid, Keys.Left);
                Check(!HasField(grid, "应用功能 AFN") && !HasField(grid, "应用数据域")
                    && HasField(grid, "校验和 CS"), "User data collapse lost frame structure");
                Check(raw.SelectedText == "03 01 01 03 00 E8 41 42 43 44 28 10 26 34 12", "Group byte range is wrong");
                FieldKey(grid, Keys.Right);
                Check(HasField(grid, "数据标识内容") && !HasField(grid, "厂商代码"), "Nested collapse state was reset");
                Click(Named<Button>(form, "themeToggle"));
                Check(!HasField(grid, "厂商代码"), "Theme change reset collapsed data");
                SelectField(grid, "数据标识内容");
                FieldKey(grid, Keys.Right);
                SelectField(grid, "厂商代码");
                Check(raw.SelectedText == "41 42" && raw.Text == LocalVersion, "Expanded field selection changed raw input");
                Recognize(form);
                Check(HasField(grid, "DIR") && HasField(grid, "厂商代码"), "New parse did not start expanded");
            }
        });
        RunTest("645 data collapse retains control and checksum and restores prefixed HEX mapping", delegate
        {
            using (MainForm form = NewForm())
            {
                TextBoxBase raw = Named<TextBoxBase>(form, "rawInput");
                raw.Text = "0xFE:0xFE-" + string.Join(", ", Read645.Split(' ').Select(b => "0x" + b));
                string original = raw.Text;
                Recognize(form);
                DataGridView grid = Named<DataGridView>(form, "parsedFields");
                SelectField(grid, "数据域 DATA");
                FieldKey(grid, Keys.Left);
                Check(!HasField(grid, "数据标识 DI") && HasField(grid, "控制码 C")
                    && HasField(grid, "校验和 CS"), "645 data did not collapse independently");
                Check(Hex.Compact(raw.SelectedText) == "33333433", "645 group includes bytes outside DATA");
                FieldKey(grid, Keys.Right);
                SelectField(grid, "数据标识 DI");
                Check(Hex.Compact(raw.SelectedText) == "33333433" && raw.Text == original, "645 mapping changed after expanding");
            }
        });
        RunTest("batch field groups fold by arrow and switching frames restores complete details", delegate
        {
            using (MainForm form = NewForm())
            {
                Named<TextBoxBase>(form, "batchInput").Text = Compact(Read645 + LocalVersion + Uplink);
                Batch(form);
                DataGridView list = Named<DataGridView>(form, "batchSummary");
                DataGridView grid = Named<DataGridView>(form, "batchFields");
                list.CurrentCell = list.Rows[1].Cells[0];
                SelectField(grid, "用户数据域");
                int row = grid.CurrentRow.Index;
                grid.GetType().GetMethod("OnCellMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(grid, new object[] { new DataGridViewCellMouseEventArgs(2, row, 8, 10,
                        new MouseEventArgs(MouseButtons.Left, 1, 8, 10, 0)) });
                Application.DoEvents();
                Check(!HasField(grid, "应用功能 AFN") && HasField(grid, "校验和 CS"), "Arrow did not fold batch details");
                list.CurrentCell = list.Rows[2].Cells[0];
                Check(HasField(grid, "控制域 C") && HasField(grid, "首个数据标识 DI"), "Uplink children are not expanded");
                SelectField(grid, "用户数据域");
                FieldKey(grid, Keys.Left);
                Check(!HasField(grid, "控制域 C") && HasField(grid, "校验和 CS"), "Uplink user data range is wrong");
                FieldKey(grid, Keys.Right);
                SelectField(grid, "首个数据标识 DI");
                Check(Named<TextBoxBase>(form, "batchSelectedFrame").SelectedText == "00 00 01 00", "Uplink highlight changed");
                list.CurrentCell = list.Rows[1].Cells[0];
                Check(HasField(grid, "厂商代码") && HasField(grid, "DIR") && list.Rows.Count == 3, "Frame switch lost details or summaries");
            }
        });
        RunTest("composer hides unsupported parameters and collapses their label rows for each protocol", delegate
        {
            using (MainForm form = NewForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-16000, -16000);
                form.Show();
                Named<TabControl>(form, "workspaceTabs").SelectedIndex = 2;
                string[][] visible =
                {
                    new[] { "control", "address", "identifier", "preamble" },
                    new[] { "control", "address", "destination", "afn", "sequence", "identifier" },
                    new[] { "control", "address", "afn", "sequence" }
                };
                for (int index = 0; index < visible.Length; index++)
                {
                    Named<ComboBox>(form, "protocol").SelectedIndex = index;
                    Application.DoEvents();
                    foreach (string name in new[] { "control", "address", "destination", "afn", "sequence", "identifier", "preamble" })
                    {
                        Control field = Parameter<Control>(form, name);
                        bool expected = visible[index].Contains(name);
                        Check(field.Visible == expected, "Wrong parameter visibility: " + index + " / " + name);
                        TableLayoutPanel table = (TableLayoutPanel)field.Parent;
                        int row = table.GetRow(field);
                        Check(table.GetControlFromPosition(0, row).Visible == expected, "Parameter label visibility differs");
                        if (!expected)
                        {
                            Check(table.RowStyles[row].Height == 0, "Hidden parameter left a blank row: " + name);
                        }
                        else if (index != 1 || (name != "address" && name != "destination"))
                        {
                            Check(field.Enabled && (!(field is TextBox) || !((TextBox)field).ReadOnly), "Visible input is locked: " + name);
                        }
                    }
                    Click(Named<Button>(form, "composeFrame"));
                    Check(FrameCodec.Parse((ProtocolKind)index, Named<TextBoxBase>(form, "builtFrame").Text).IsFrameValid, "Default composer failed after row changes");
                }
            }
        });
        RunTest("local address fields explain ADD state and retain entered addresses without transmitting hints", delegate
        {
            using (MainForm form = NewForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-16000, -16000);
                form.Show();
                Named<TabControl>(form, "workspaceTabs").SelectedIndex = 2;
                Named<ComboBox>(form, "protocol").SelectedIndex = 1;
                TextBox code = Parameter<TextBox>(form, "control");
                TextBox source = Parameter<TextBox>(form, "address");
                TextBox destination = Parameter<TextBox>(form, "destination");
                Check(source.ReadOnly && destination.ReadOnly && source.Text.Contains("ADD=0")
                    && destination.Text.Contains("ADD=0"), "Inactive addresses still display example values");
                code.Text = "60";
                Check(!source.ReadOnly && !destination.ReadOnly && source.TextLength == 0
                    && destination.TextLength == 0, "Address entry did not open blank");
                source.Text = "112233445566";
                destination.Text = "667788990011";
                Click(Named<Button>(form, "composeFrame"));
                FrameResult addressed = FrameCodec.Parse(ProtocolKind.CsgLocal, Named<TextBoxBase>(form, "builtFrame").Text);
                Check(addressed.IsFrameValid && addressed.Fields.Single(field => field.Name == "源地址 ASRC").Meaning.StartsWith(source.Text)
                    && addressed.Fields.Single(field => field.Name == "目的地址 ADST").Meaning.StartsWith(destination.Text), "Entered addresses were not composed");
                code.Text = "40";
                Check(source.ReadOnly && source.Text.Contains("ADD=0") && destination.ReadOnly, "ADD=0 did not restore explanations");
                Click(Named<Button>(form, "themeToggle"));
                Check(source.ForeColor == WorkspaceStyle.Secondary && destination.ForeColor == WorkspaceStyle.Secondary, "Hint colors were lost on theme change");
                Click(Named<Button>(form, "composeFrame"));
                FrameResult unaddressed = FrameCodec.Parse(ProtocolKind.CsgLocal, Named<TextBoxBase>(form, "builtFrame").Text);
                Check(unaddressed.IsFrameValid && unaddressed.Bytes.Length == 12, "Explanation text leaked into a frame");
                code.Text = "60";
                Check(source.Text == "112233445566" && destination.Text == "667788990011", "ADD toggle lost entered addresses");
                code.Text = "6";
                Check(source.ReadOnly && destination.ReadOnly && source.Text.Contains("控制码"), "Incomplete C has no inline explanation");
                code.Text = "60";
                Check(!source.ReadOnly && source.Text == "112233445566" && destination.Text == "667788990011", "Incomplete C lost address values");
            }
        });
        RunTest("purpose composer is primary and all protocols generate and transfer without parsing", delegate
        {
            using (MainForm form = new MainForm())
            {
                Check(Named<TabControl>(form, "composerModes").SelectedIndex == 0, "Purpose mode is not primary");
                string[] keys = { "645.read", "local.E8000301", "up.current" };
                for (int protocol = 0; protocol < keys.Length; protocol++)
                {
                    Purpose(form, protocol, keys[protocol]);
                    Click(Named<Button>(form, "composePurpose"));
                    string hex = Named<TextBoxBase>(form, "purposePreview").Text;
                    Check(FrameCodec.Parse((ProtocolKind)protocol, hex).IsFrameValid, "Purpose UI failed for protocol " + protocol);
                    Click(Named<Button>(form, "purposeToParser"));
                    Check(Named<TextBoxBase>(form, "rawInput").Text == hex && Named<TabControl>(form, "workspaceTabs").SelectedIndex == 0, "Transfer did not reach parser");
                    CheckUnparsed(form);
                }
            }
        });
        RunTest("purpose ordinary task modes replace points with editable type groups and keep times", delegate
        {
            using (MainForm form = NewForm())
            {
                Purpose(form, 2, "up.task.set");
                DateTime time = new DateTime(2026, 10, 30, 10, 52, 0);
                Named<DateTimePicker>(form, "purpose_reportTime").Value = time;
                Named<ComboBox>(form, "purpose_mode").SelectedIndex = 4;
                Check(form.Controls.Find("purpose_points", true).Length == 0, "Unavailable point field retained");
                DataGridView grid = Named<DataGridView>(form, "purpose_typeGroups");
                Check(grid.Columns.Count == 2 && !grid.ReadOnly, "No editable type selectors");
                grid.Rows[0].Cells[0].Value = "3";
                grid.Rows[0].Cells[1].Value = "7";
                Click(Named<Button>(form, "composePurpose"));
                Check(Named<TextBoxBase>(form, "purposePreview").Text.EndsWith("01 07 03 01 00 01 01 02"), "Type group edits not composed");
                Check(!Named<Button>(form, "purposeToParser").Enabled && Named<Label>(form, "purposeStatus").Text.Contains("参数明文"), "Plaintext treated as complete frame");
                Named<ComboBox>(form, "purpose_mode").SelectedIndex = 0;
                Check(form.Controls.Find("purpose_typeGroups", true).Length == 0 && form.Controls.Find("purpose_points", true).Length == 1, "Mode did not restore points");
                Check(Named<DateTimePicker>(form, "purpose_reportTime").Value == time, "Mode switch reset time");
            }
        });
        RunTest("purpose parameter choice resets incompatible editors and defaults", delegate
        {
            using (MainForm form = NewForm())
            {
                Purpose(form, 2, "up.parameter.set");
                Named<NumericUpDown>(form, "purpose_value").Value = 45;
                Named<ComboBox>(form, "purpose_parameter").SelectedIndex = 2;
                Check(Named<TextBoxBase>(form, "purpose_value").Text == "CMNET", "Heartbeat leaked into APN");
                Named<ComboBox>(form, "purpose_parameter").SelectedIndex = 3;
                Check(Named<ComboBox>(form, "purpose_value").SelectedIndex == 0, "APN leaked into transport selector");
                Named<ComboBox>(form, "purpose_parameter").SelectedIndex = 1;
                Check(form.Controls.Find("purpose_value", true).Length == 0 && form.Controls.Find("purpose_time", true).Length == 1, "Clock retains irrelevant input");
                Click(Named<Button>(form, "composePurpose"));
                Check(Named<TextBoxBase>(form, "purposePreview").TextLength > 0 && !Named<Button>(form, "purposeToParser").Enabled, "Parameter editing failed");
            }
        });
        RunTest("purpose task mode switch cannot turn invalid type-cell text into valid parameters", delegate
        {
            using (MainForm form = NewForm())
            {
                Purpose(form, 2, "up.task.set");
                Named<ComboBox>(form, "purpose_mode").SelectedIndex = 4;
                Named<DataGridView>(form, "purpose_typeGroups").Rows[0].Cells[0].Value = "3:7";
                Named<ComboBox>(form, "purpose_mode").SelectedIndex = 0;
                Named<ComboBox>(form, "purpose_mode").SelectedIndex = 4;
                Click(Named<Button>(form, "composePurpose"));
                Check((string)Named<Label>(form, "purposeStatus").Tag == "error"
                    && Named<TextBoxBase>(form, "purposePreview").TextLength == 0, "Invalid type input was composed");
            }
        });
        RunTest("purpose event range folds time fields and restores their values", delegate
        {
            using (MainForm form = NewForm())
            {
                Purpose(form, 2, "up.events");
                DateTime start = new DateTime(2026, 10, 30, 10, 52, 0);
                Named<DateTimePicker>(form, "purpose_from").Value = start;
                Named<DateTimePicker>(form, "purpose_to").Value = start.AddHours(1);
                Named<ComboBox>(form, "purpose_rangeMode").SelectedIndex = 1;
                Check(form.Controls.Find("purpose_from", true).Length == 0 && form.Controls.Find("purpose_to", true).Length == 0, "Recent ten keeps time inputs");
                Click(Named<Button>(form, "composePurpose"));
                Check(Named<TextBoxBase>(form, "purposePreview").Text.Contains(string.Join(" ", Enumerable.Repeat("FF", 12))), "Recent-ten range not generated");
                Named<ComboBox>(form, "purpose_rangeMode").SelectedIndex = 0;
                Check(Named<DateTimePicker>(form, "purpose_from").Value == start, "Range mode lost time");
            }
        });
        RunTest("purpose local multicast all-nodes mode removes address rows and restores input", delegate
        {
            using (MainForm form = NewForm())
            {
                Purpose(form, 1, "local.E8020207");
                Named<TextBoxBase>(form, "purpose_nodes").Text = "112233445566";
                Named<TextBoxBase>(form, "purpose_packets").Text = "68 01 16";
                Named<ComboBox>(form, "purpose_combined").SelectedIndex = 1;
                Named<ComboBox>(form, "purpose_allNodes").SelectedIndex = 1;
                Check(form.Controls.Find("purpose_nodes", true).Length == 0, "All-node multicast retained address list");
                Click(Named<Button>(form, "composePurpose"));
                Check(FrameCodec.Parse(ProtocolKind.CsgLocal, Named<TextBoxBase>(form, "purposePreview").Text).IsFrameValid, "Combined multicast UI failed");
                Named<ComboBox>(form, "purpose_allNodes").SelectedIndex = 0;
                Check(Named<TextBoxBase>(form, "purpose_nodes").Text == "112233445566", "Target toggle lost node addresses");
            }
        });
        RunTest("purpose protocol reflection preserves command fields and generated output", delegate
        {
            using (MainForm form = NewForm())
            {
                Purpose(form, 2, "up.current");
                Named<TextBoxBase>(form, "purpose_points").Text = "3";
                Click(Named<Button>(form, "composePurpose"));
                string built = Named<TextBoxBase>(form, "purposePreview").Text;
                Named<TabControl>(form, "workspaceTabs").SelectedIndex = 0;
                Named<TextBoxBase>(form, "rawInput").Text = Local;
                Recognize(form);
                Named<TabControl>(form, "workspaceTabs").SelectedIndex = 2;
                Check(Named<ComboBox>(form, "protocol").SelectedIndex == 2 && ((PurposeCommand)Named<ComboBox>(form, "purposeCommand").SelectedItem).Key == "up.current", "Recognition changed purpose protocol or command");
                Check(Named<TextBoxBase>(form, "purpose_points").Text == "3" && Named<TextBoxBase>(form, "purposePreview").Text == built, "Recognition reset purpose values");
            }
        });
        RunTest("purpose invalid input clears stale output inline and Ctrl-Enter uses active mode", delegate
        {
            using (MainForm form = NewForm())
            {
                Purpose(form, 2, "up.current");
                typeof(MainForm).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { new KeyEventArgs(Keys.Control | Keys.Enter) });
                Check(Named<TextBoxBase>(form, "purposePreview").TextLength > 0 && Named<TextBoxBase>(form, "builtFrame").TextLength == 0, "Ctrl-Enter used raw composer");
                Named<TextBoxBase>(form, "purpose_points").Text = "2033";
                Check(Named<TextBoxBase>(form, "purposePreview").TextLength == 0 && !Named<Button>(form, "copyPurpose").Enabled, "Input edit retained stale frame");
                Click(Named<Button>(form, "composePurpose"));
                Check((string)Named<Label>(form, "purposeStatus").Tag == "error", "No inline validation");
            }
        });
        RunTest("XML identifier picker retains checked items across name searches", delegate
        {
            Type type = typeof(MainForm).Assembly.GetType("FrameTools.IdentifierPicker", true);
            PurposeCommand command = PurposeComposer.GetCommands(ProtocolKind.CsgUplink).Single(item => item.Key == "up.current");
            using (Form dialog = (Form)Activator.CreateInstance(type, new object[] { command, PurposeComposer.Defaults(command), "02010100" }))
            {
                TextBox filter = Named<TextBox>(dialog, "identifierFilter");
                CheckedListBox list = Named<CheckedListBox>(dialog, "identifierChoices");
                Check(list.Items.Count <= 200, "Too many items loaded into control");
                filter.Text = "02010200";
                Check(list.Items.Count == 1, "Exact DI search failed");
                list.SetItemChecked(0, true);
                filter.Text = "02010100";
                Check(list.Items.Count == 1 && list.GetItemChecked(0), "Previous checked item lost");
                Check(Named<Label>(dialog, "identifierSelection").Text.Contains("已选2项"), "Selection list not maintained");
            }
        });
        RunTest("purpose minimum layout and themes preserve visible controls and values", delegate
        {
            using (MainForm form = NewForm())
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-16000, -16000);
                form.Size = form.MinimumSize;
                form.Show();
                Purpose(form, 2, "up.history");
                Click(Named<Button>(form, "composePurpose"));
                string built = Named<TextBoxBase>(form, "purposePreview").Text;
                Control view = Named<Control>(form, "purposeComposer");
                foreach (string name in new[] { "composePurpose", "copyPurpose", "purposeToParser", "purposePreview", "purposeStatus" })
                {
                    Control child = Named<Control>(form, name);
                    Rectangle bounds = view.RectangleToClient(child.RectangleToScreen(child.ClientRectangle));
                    Check(child.Visible && view.ClientRectangle.Contains(bounds), "Purpose control clipped at minimum size: " + name);
                }
                Check(Named<Panel>(form, "purposeFieldsScroll").VerticalScroll.Visible, "Long form has no scrollbar");
                Click(Named<Button>(form, "themeToggle"));
                Check(Named<TextBoxBase>(form, "purposePreview").BackColor == WorkspaceStyle.Surface, "Dark preview theme missing");
                Click(Named<Button>(form, "themeToggle"));
                Check(Named<TextBoxBase>(form, "purposePreview").Text == built, "Theme switch changed frame");
            }
        });
        Console.WriteLine("WINDOW RESULT: " + Passed + " passed, " + failed + " failed");
        return failed;
    }
}
