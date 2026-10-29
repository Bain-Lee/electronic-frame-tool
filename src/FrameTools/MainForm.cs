using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FrameTools
{
    public sealed class MainForm : Form
    {
        private readonly ComboBox protocol = new ComboBox();
        private readonly CheckBox automaticProtocol = new CheckBox();
        private readonly TabControl tabs = new WorkspaceTabs();
        private readonly ThemeButton themeButton = new ThemeButton();
        private readonly ToolTip themeTip = new ToolTip();
        private readonly BatchParserView batch;
        private readonly Label protocolLabel = new Label();
        private readonly WorkspaceTextBox input = new WorkspaceTextBox();
        private readonly WorkspaceTextBox diagnostics = new WorkspaceTextBox();
        private readonly FrameFieldGrid fields = new FrameFieldGrid();
        private readonly Label status = new Label();
        private readonly TextBox control = new TextBox();
        private readonly WorkspaceAddressBox address = new WorkspaceAddressBox();
        private readonly WorkspaceAddressBox destination = new WorkspaceAddressBox();
        private readonly TextBox afn = new TextBox();
        private readonly TextBox sequence = new TextBox();
        private readonly TextBox identifier = new TextBox();
        private readonly NumericUpDown preamble = new NumericUpDown();
        private readonly WorkspaceTextBox payload = new WorkspaceTextBox();
        private readonly WorkspaceTextBox output = new WorkspaceTextBox();
        private readonly Label addressLabel = new Label();
        private readonly Label identifierLabel = new Label();
        private readonly Label payloadLabel = new Label();
        private readonly Label builderHelp = new Label();
        private readonly TableLayoutPanel composerParameters = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 4 };
        private readonly Label composerHint = new Label
        {
            Dock = DockStyle.Fill, ForeColor = WorkspaceStyle.Secondary, Tag = "secondary", Padding = new Padding(24, 5, 0, 0)
        };
        private WorkspaceButton exportButton;
        private FrameResult current;
        private StreamParseResult parsedStream;
        private int[] inputDigits = new int[0];
        private ProtocolKind configuredProtocol;
        private bool reflectingProtocol;

        public MainForm()
        {
            WorkspaceStyle.IsDark = false;
            Text = "FrameTools 0.5.0 — 电力报文工具";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(1020, 740);
            Size = new Size(1280, 900);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = WorkspaceStyle.Background;
            ForeColor = WorkspaceStyle.Text;
            KeyPreview = true;

            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            Panel heading = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 2) };
            heading.Controls.Add(new Label
            {
                Text = "FrameTools 电力报文工具", Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
                ForeColor = WorkspaceStyle.Text, AutoSize = true, Location = new Point(0, 0)
            });
            themeButton.Name = "themeToggle";
            themeButton.Location = new Point(4, 0);
            themeButton.Click += delegate { WorkspaceStyle.IsDark = !WorkspaceStyle.IsDark; ApplyTheme(); };
            Panel themePanel = new Panel { Dock = DockStyle.Right, Width = 34 };
            themePanel.Controls.Add(themeButton);
            heading.Controls.Add(themePanel);
            heading.Controls.Add(new Label { Text = "v0.5.0", Dock = DockStyle.Right, Width = 70, TextAlign = ContentAlignment.MiddleRight, Tag = "secondary" });
            themePanel.SendToBack();
            shell.Controls.Add(heading, 0, 0);
            WorkspaceGroupBox protocolGroup = new WorkspaceGroupBox { Text = "规约选择", Dock = DockStyle.Fill, Padding = new Padding(7, 3, 7, 2), Margin = new Padding(0, 0, 0, 5) };
            FlowLayoutPanel selector = Toolbar();
            protocolLabel.Text = "手动规约";
            protocolLabel.AutoSize = true;
            protocolLabel.Margin = new Padding(0, 5, 12, 0);
            selector.Controls.Add(protocolLabel);
            protocol.Name = "protocol";
            ConfigurePicker(protocol);
            protocol.Width = 385;
            protocol.Margin = new Padding(0, 0, 0, 0);
            protocol.Items.AddRange(new object[] { "DL/T 645-2007", "南网本地通信模块接口（试行）", "南网上行通信规约（2024版 V2.0）" });
            selector.Controls.Add(protocol);
            automaticProtocol.Name = "automaticProtocol";
            automaticProtocol.Text = "自动识别规约";
            automaticProtocol.AutoSize = true;
            automaticProtocol.FlatStyle = FlatStyle.Flat;
            automaticProtocol.Checked = true;
            automaticProtocol.Margin = new Padding(16, 3, 0, 0);
            selector.Controls.Add(automaticProtocol);
            protocolGroup.Controls.Add(selector);
            shell.Controls.Add(protocolGroup, 0, 1);
            tabs.Dock = DockStyle.Fill;
            tabs.Name = "workspaceTabs";
            shell.Controls.Add(tabs, 0, 2);
            BuildParserTab();
            batch = new BatchParserView(delegate { return automaticProtocol.Checked ? (ProtocolKind?)null : SelectedProtocol; });
            batch.SelectionChanged += delegate
            {
                if (tabs.SelectedIndex == 1)
                {
                    UpdateProtocolMode();
                }
            };
            TabPage batchPage = new TabPage("多帧解析") { Padding = new Padding(8) };
            batchPage.Controls.Add(batch);
            tabs.TabPages.Add(batchPage);
            BuildComposerTab();
            protocol.SelectedIndexChanged += delegate
            {
                if (reflectingProtocol || protocol.SelectedIndex < 0)
                {
                    return;
                }
                configuredProtocol = (ProtocolKind)protocol.SelectedIndex;
                SetDefaults();
                InvalidateParsedInput();
                batch.InvalidateResults();
            };
            automaticProtocol.CheckedChanged += delegate
            {
                UpdateProtocolMode();
                InvalidateParsedInput();
                batch.InvalidateResults();
            };
            tabs.SelectedIndexChanged += delegate { UpdateProtocolMode(); };
            protocol.SelectedIndex = 0;
            UpdateProtocolMode();
            ApplyTheme();
            Disposed += delegate { themeTip.Dispose(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (tabs.SelectedIndex == 2)
                    {
                        Guard(Compose);
                    }
                    else if (tabs.SelectedIndex == 1)
                    {
                        Guard(batch.Parse);
                    }
                    else
                    {
                        Guard(ParseInput);
                    }
                }
            };
        }

        private ProtocolKind SelectedProtocol { get { return configuredProtocol; } }

        private void ApplyTheme()
        {
            WorkspaceStyle.Apply(this);
            string action = WorkspaceStyle.IsDark ? "切换到亮色主题" : "切换到暗色主题";
            themeButton.AccessibleName = action;
            themeTip.SetToolTip(themeButton, action);
        }

        private static FlowLayoutPanel Toolbar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        }

        private static void ConfigureHex(WorkspaceTextBox box, bool readOnly)
        {
            box.Dock = DockStyle.Fill;
            box.Multiline = true;
            box.MaxLength = int.MaxValue;
            box.Font = new Font("Consolas", 10.5F);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.ReadOnly = readOnly;
            box.BackColor = WorkspaceStyle.Surface;
            box.ForeColor = WorkspaceStyle.Text;
            box.HideSelection = false;
        }

        private static void ConfigurePicker(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.FlatStyle = FlatStyle.Standard;
            box.BackColor = WorkspaceStyle.Surface;
            box.DrawMode = DrawMode.OwnerDrawFixed;
            box.ItemHeight = Math.Max(20, box.Font.Height + 3);
            box.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= box.Items.Count)
                {
                    return;
                }
                bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                Color background = !box.Enabled ? WorkspaceStyle.Header : selected ? WorkspaceStyle.Blue : WorkspaceStyle.Surface;
                using (Brush fill = new SolidBrush(background))
                {
                    e.Graphics.FillRectangle(fill, e.Bounds);
                }
                Rectangle textBounds = e.Bounds;
                textBounds.Inflate(-5, 0);
                TextRenderer.DrawText(e.Graphics, box.Items[e.Index].ToString(), e.Font, textBounds,
                    !box.Enabled ? WorkspaceStyle.Secondary : selected ? Color.White : WorkspaceStyle.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        private WorkspaceButton Button(FlowLayoutPanel panel, string text, Action action)
        {
            WorkspaceButton button = new WorkspaceButton
            {
                Text = text, AutoSize = true, MinimumSize = new Size(65, 25),
                Margin = new Padding(0, 2, 6, 2)
            };
            button.Click += delegate { Guard(action); };
            panel.Controls.Add(button);
            return button;
        }

        private void UpdateProtocolMode()
        {
            bool composer = tabs.SelectedIndex == 2;
            bool automatic = !composer && automaticProtocol.Checked;
            protocolLabel.Text = composer ? "组帧规约" : automatic ? "识别规约" : "手动规约";
            protocol.Enabled = composer || !automaticProtocol.Checked;
            automaticProtocol.Visible = !composer;
            ProtocolKind? displayed = SelectedProtocol;
            if (automatic)
            {
                displayed = tabs.SelectedIndex == 1 ? batch.SelectedProtocol
                    : parsedStream != null && parsedStream.Parts.Count == 1 && parsedStream.Parts[0].Result != null
                        ? parsedStream.Parts[0].Protocol : null;
            }
            reflectingProtocol = true;
            protocol.SelectedIndex = displayed.HasValue ? (int)displayed.Value : -1;
            reflectingProtocol = false;
        }

        private void BuildParserTab()
        {
            TabPage page = new TabPage("单帧解析") { BackColor = BackColor, Padding = new Padding(8) };
            tabs.TabPages.Add(page);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 145));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            page.Controls.Add(layout);
            ConfigureHex(input, false);
            input.Name = "rawInput";
            input.AccessibleName = "待解析十六进制报文";
            input.TextChanged += delegate { InvalidateParsedInput(); };
            TableLayoutPanel sourcePane = EditorPane("原始报文", input);
            sourcePane.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(sourcePane, 0, 0);
            FlowLayoutPanel actions = Toolbar();
            WorkspaceButton parseButton = Button(actions, "识别并解析", ParseInput);
            parseButton.Name = "parseFrames";
            parseButton.Primary = true;
            parseButton.AccessibleDescription = "点击处理当前输入，快捷键 Ctrl+Enter";
            Button(actions, "载入请求示例", LoadRequest);
            Button(actions, "645 电能应答示例", delegate
            {
                protocol.SelectedIndex = 0;
                input.Text = "68 01 00 00 00 00 00 68 91 08 33 33 34 33 AB 89 67 45 17 16";
            });
            Button(actions, "打开文本", OpenText);
            exportButton = Button(actions, "导出全部解析", ExportReport);
            exportButton.Enabled = false;
            Button(actions, "转到多帧解析", delegate { batch.InputText = input.Text; tabs.SelectedIndex = 1; }).Name = "sendToBatch";
            layout.Controls.Add(actions, 0, 1);
            status.Name = "parseStatus";
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = WorkspaceStyle.Secondary;
            layout.Controls.Add(status, 0, 2);
            fields.Name = "parsedFields";
            WorkspaceStyle.ConfigureGrid(fields);
            fields.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Offset", HeaderText = "帧内偏移", Width = 85 });
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Length", HeaderText = "字节数", Width = 65 });
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "字段", Width = 215 });
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Raw", HeaderText = "原始字节", Width = 245 });
            fields.Columns[3].DefaultCellStyle.Font = new Font("Consolas", 10F);
            fields.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Meaning", HeaderText = "解读", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            });
            fields.CurrentCellChanged += delegate
            {
                if (current == null || fields.CurrentRow == null)
                {
                    return;
                }
                FrameField field = fields.CurrentRow.DataBoundItem as FrameField;
                if (field != null)
                {
                    int first = field.Offset * 2;
                    int last = first + field.Length * 2 - 1;
                    if (field.Length > 0 && last < inputDigits.Length)
                    {
                        input.Select(inputDigits[first], inputDigits[last] - inputDigits[first] + 1);
                        input.ScrollToCaret();
                    }
                }
            };
            layout.Controls.Add(fields, 0, 3);
            ConfigureHex(diagnostics, true);
            diagnostics.Name = "parseDiagnostics";
            diagnostics.Font = Font;
            diagnostics.ForeColor = WorkspaceStyle.Secondary;
            diagnostics.Tag = "secondary";
            diagnostics.AccessibleName = "帧结构诊断与解析范围";
            layout.Controls.Add(EditorSurface(diagnostics), 0, 4);
        }

        private TableLayoutPanel EditorPane(string title, WorkspaceTextBox editor)
        {
            TableLayoutPanel pane = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pane.Controls.Add(new Label
            {
                Text = title, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            pane.Controls.Add(EditorSurface(editor), 0, 1);
            return pane;
        }

        private static Panel EditorSurface(WorkspaceTextBox editor)
        {
            Panel surface = new WorkspaceEditor();
            editor.BorderStyle = BorderStyle.None;
            surface.Controls.Add(editor);
            return surface;
        }

        private void BuildComposerTab()
        {
            TabPage page = new TabPage("报文组装") { BackColor = BackColor, Padding = new Padding(8) };
            page.AutoScroll = true;
            tabs.TabPages.Add(page);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Top, RowCount = 7, ColumnCount = 1, Height = 660, MinimumSize = new Size(0, 660) };
            page.ClientSizeChanged += delegate { layout.Height = Math.Max(660, page.ClientSize.Height - page.Padding.Vertical); };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 270));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            page.Controls.Add(layout);
            TableLayoutPanel parameters = composerParameters;
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            AddParameter(parameters, 0, "控制码 C", control);
            addressLabel.Text = "地址";
            AddParameter(parameters, 1, addressLabel, address);
            AddParameter(parameters, 2, "目的地址（高字节先写）", destination);
            AddParameter(parameters, 3, "应用功能 AFN（HEX）", afn);
            AddParameter(parameters, 4, "帧序列 SEQ（HEX）", sequence);
            AddParameter(parameters, 5, identifierLabel, identifier);
            AddParameter(parameters, 6, "前导 FE 数量（645）", preamble);
            preamble.Maximum = 4;
            parameters.Controls.Add(composerHint, 2, 0);
            parameters.SetColumnSpan(composerHint, 2);
            parameters.SetRowSpan(composerHint, 7);
            layout.Controls.Add(parameters, 0, 0);
            payloadLabel.Dock = DockStyle.Fill;
            payloadLabel.TextAlign = ContentAlignment.MiddleLeft;
            payloadLabel.Font = new Font(Font, FontStyle.Bold);
            layout.Controls.Add(payloadLabel, 0, 1);
            ConfigureHex(payload, false);
            payload.Name = "businessInput";
            payload.AccessibleName = "组帧业务数据";
            layout.Controls.Add(EditorSurface(payload), 0, 2);
            FlowLayoutPanel actions = Toolbar();
            actions.Padding = new Padding(0, 6, 0, 0);
            WorkspaceButton composeButton = Button(actions, "生成报文", Compose);
            composeButton.Primary = true;
            composeButton.Name = "composeFrame";
            Button(actions, "复制结果", delegate { Clipboard.SetText(RequireOutput()); });
            Button(actions, "发送到解析页", delegate
            {
                input.Text = RequireOutput();
                tabs.SelectedIndex = 0;
            });
            Button(actions, "恢复请求示例参数", SetDefaults);
            layout.Controls.Add(actions, 0, 3);
            ConfigureHex(output, true);
            output.Name = "builtFrame";
            output.AccessibleName = "组装结果";
            layout.Controls.Add(new Label { Text = "生成结果", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            layout.Controls.Add(EditorSurface(output), 0, 5);
            builderHelp.Dock = DockStyle.Fill;
            builderHelp.ForeColor = WorkspaceStyle.Secondary;
            builderHelp.Tag = "secondary";
            builderHelp.Padding = new Padding(0, 8, 0, 0);
            layout.Controls.Add(builderHelp, 0, 6);
            control.TextChanged += delegate { UpdateAddressState(); output.Clear(); };
            foreach (TextBoxBase parameter in new TextBoxBase[] { address, destination, afn, sequence, identifier, payload })
            {
                parameter.TextChanged += delegate { output.Clear(); };
            }
            preamble.ValueChanged += delegate { output.Clear(); };
        }

        private static void AddParameter(TableLayoutPanel table, int row, string text, Control field)
        {
            AddParameter(table, row, new Label { Text = text }, field);
        }

        private static void AddParameter(TableLayoutPanel table, int row, Label label, Control field)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(3, 4, 3, 4);
            field.BackColor = WorkspaceStyle.Surface;
            field.ForeColor = WorkspaceStyle.Text;
            TextBox text = field as TextBox;
            if (text != null)
            {
                text.BorderStyle = BorderStyle.FixedSingle;
                text.Font = new Font("Consolas", 10.5F);
            }
            table.Controls.Add(label, 0, row);
            table.Controls.Add(field, 1, row);
        }

        private void SetDefaults()
        {
            bool is645 = SelectedProtocol == ProtocolKind.Dlt645;
            bool local = SelectedProtocol == ProtocolKind.CsgLocal;
            control.Text = is645 ? "11" : local ? "40" : "4B";
            address.AddressValue = is645 ? "000000000001" : local ? "" : "06 01 44 01 00 00 01";
            destination.AddressValue = "";
            afn.Text = local ? "03" : "0C";
            sequence.Text = local ? "01" : "60";
            identifier.Text = is645 ? "00010000" : local ? "E8000301" : "";
            preamble.Value = is645 ? 4 : 0;
            SetParameterVisible(destination, local);
            SetParameterVisible(afn, !is645);
            SetParameterVisible(sequence, !is645);
            SetParameterVisible(identifier, is645 || local);
            SetParameterVisible(preamble, is645);
            ((TableLayoutPanel)composerParameters.Parent).RowStyles[0].Height = composerParameters.RowStyles.Cast<RowStyle>().Sum(row => row.Height) + 4;
            payload.Text = is645 || local ? "" : "01 01 00 00 01 00";
            addressLabel.Text = is645 ? "表地址（高字节先写）" : local ? "源地址（高字节先写）" : "地址 A（7 字节传输顺序）";
            identifierLabel.Text = is645 ? "数据标识 DI（可空）" : "数据标识 DI";
            composerHint.Text = is645 ? "C 按十六进制填写。\n\nDI 单独填写，其他明文字节放入业务数据区。\n\nFE 数量可填写 0…4。"
                : local ? "C / AFN / SEQ 均按十六进制填写。\n\nADD=1 时填写源、目的地址。例如 C 从 40 改为 60。\n\nDI 单独填写，业务数据不包含 DI。"
                : "C / AFN / SEQ 均按十六进制填写。\n\n地址按 A1(3) + A2(3) + A3(1) 的传输顺序填写。\n\n业务区填写完整的 DA / DI / 数据 / 可选 PW / Tp。";
            payloadLabel.Text = is645 ? "业务数据 HEX（DI 之后的明文字节，自动加 33H）"
                : local ? "数据标识内容 HEX（不含 DI，不做 ±33H）" : "完整应用业务区 HEX（含 DA / DI / 数据 / 可选 PW / Tp；不做 ±33H）";
            builderHelp.Text = is645 ? "自动处理地址字节序、DI 字节序、数据域 +33H、L 和 CS。读地址时 C=13，DI 清空。写数据的密码及操作者代码由使用者按标准填入。"
                : local ? "按本机试行标准生成：总长度 L、可选 12 字节地址、8 位 SEQ、4 字节 DI 和 CS。C 的 D5 为 ADD；ADD=0 时不使用源、目的地址。"
                : "按上行标准 2024版V2.0 生成：直接 BIN 长度 L，重复两次；7 字节地址；CS 从 C 开始累加。PW/MAC 与业务内容由使用者准备，不计算认证值。";
            UpdateAddressState();
            output.Clear();
        }

        private void SetParameterVisible(Control field, bool visible)
        {
            int row = composerParameters.GetRow(field);
            composerParameters.GetControlFromPosition(0, row).Visible = visible;
            field.Visible = visible;
            composerParameters.RowStyles[row].Height = visible ? 38 : 0;
        }

        private void UpdateAddressState()
        {
            bool local = SelectedProtocol == ProtocolKind.CsgLocal;
            if (!local)
            {
                address.SetAvailable(true, "");
                return;
            }
            bool addressed = false;
            string reason = "请先输入完整的1字节控制码 C";
            try
            {
                byte[] bytes = Hex.Parse(control.Text);
                if (bytes.Length == 1)
                {
                    addressed = (bytes[0] & 0x20) != 0;
                    reason = "ADD=0，不携带地址；D5=1 后可填写";
                }
            }
            catch (FormatException)
            {
                // Explain why address entry is unavailable while C is incomplete.
            }
            address.SetAvailable(addressed, reason);
            destination.SetAvailable(addressed, reason);
        }

        private static byte OneByte(TextBox box, string name)
        {
            byte[] value = Hex.Parse(box.Text);
            if (value.Length != 1)
            {
                throw new FormatException(name + " 必须为 1 字节 HEX，例如 01、0C、60。");
            }
            return value[0];
        }

        private void Compose()
        {
            byte c = OneByte(control, "控制域");
            byte[] data = Hex.Parse(payload.Text);
            byte[] frame;
            if (SelectedProtocol == ProtocolKind.Dlt645)
            {
                byte[] di = string.IsNullOrWhiteSpace(identifier.Text) ? new byte[0] : Hex.Identifier(identifier.Text);
                frame = FrameCodec.Build645(address.AddressValue, c, di.Concat(data).ToArray(), (int)preamble.Value);
            }
            else if (SelectedProtocol == ProtocolKind.CsgLocal)
            {
                frame = FrameCodec.BuildLocal(c, address.AddressValue, destination.AddressValue, OneByte(afn, "AFN"),
                    OneByte(sequence, "SEQ"), Hex.Identifier(identifier.Text), data);
            }
            else
            {
                frame = FrameCodec.BuildUplink(c, Hex.Parse(address.AddressValue), OneByte(afn, "AFN"), OneByte(sequence, "SEQ"), data);
            }
            output.Text = Hex.Format(frame);
        }

        private void LoadRequest()
        {
            SetDefaults();
            Compose();
            input.Text = output.Text;
        }

        private void ClearParsedView()
        {
            current = null;
            parsedStream = null;
            fields.DataSource = null;
            inputDigits = new int[0];
            diagnostics.Clear();
            exportButton.Enabled = false;
        }

        private void InvalidateParsedInput()
        {
            ClearParsedView();
            UpdateProtocolMode();
            status.Tag = "secondary";
            status.ForeColor = WorkspaceStyle.Secondary;
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                status.Text = "粘贴一条报文后点击识别；连续日志请使用“多帧解析”。";
                return;
            }
            status.Text = "输入已更新，点击“识别并解析”查看结果。";
        }

        private void ParseInput()
        {
            ClearParsedView();
            status.Text = "正在解析…";
            status.Tag = "secondary";
            status.ForeColor = WorkspaceStyle.Secondary;
            try
            {
                parsedStream = FrameStream.Parse(input.Text, automaticProtocol.Checked ? (ProtocolKind?)null : SelectedProtocol);
            }
            catch (FormatException e)
            {
                status.Text = "输入格式错误；请保留 HEX 字节并移除日志时间戳或文字。";
                status.Tag = "error";
                status.ForeColor = WorkspaceStyle.Error;
                diagnostics.Text = e.Message;
                return;
            }
            UpdateProtocolMode();
            if (parsedStream.Parts.Count > 1)
            {
                status.Tag = "warning";
                status.ForeColor = WorkspaceStyle.Warning;
                status.Text = "检测到多条报文或多个数据片段，请点击“转到多帧解析”逐条查看。";
                diagnostics.Text = "原始输入已保留。转入多帧页后，点击“开始多帧解析”查看全部内容。";
                return;
            }
            exportButton.Enabled = parsedStream.Parts.Count > 0 || parsedStream.PendingNibble.Length > 0;
            int frames = parsedStream.Parts.Count(p => p.Kind == StreamPartKind.Frame);
            int valid = parsedStream.Parts.Count(p => p.Result != null && p.Result.IsFrameValid);
            int unresolved = parsedStream.Parts.Count - frames;
            status.Tag = frames > valid ? "error" : unresolved > 0 || parsedStream.PendingNibble.Length > 0 ? "warning" : "success";
            status.ForeColor = frames > valid ? WorkspaceStyle.Error
                : unresolved > 0 || parsedStream.PendingNibble.Length > 0 ? WorkspaceStyle.Warning : WorkspaceStyle.Success;
            status.Text = "识别 " + frames + " 帧 · 结构通过 " + valid + " · 结构错误 " + (frames - valid)
                + " · 待定片段 " + unresolved + (parsedStream.PendingNibble.Length > 0 ? " · 末尾半字节待补全" : "");
            if (parsedStream.Parts.Count > 0)
            {
                inputDigits = Regex.Matches(input.Text, @"\b0[xX]|[0-9a-fA-F]").Cast<Match>()
                    .Where(m => m.Length == 1).Select(m => m.Index).ToArray();
                ShowSinglePart();
            }
            else
            {
                status.Text = parsedStream.PendingNibble.Length > 0 ? "末尾只有半个字节，等待继续输入。" : "请粘贴 HEX 报文，可连续输入多条。";
            }
        }

        private string ProtocolLabel(ProtocolKind? kind)
        {
            return kind.HasValue ? protocol.Items[(int)kind.Value].ToString() : "规约未确定";
        }

        private void ShowSinglePart()
        {
            current = null;
            fields.DataSource = null;
            if (parsedStream == null || parsedStream.Parts.Count != 1)
            {
                return;
            }
            StreamPart part = parsedStream.Parts[0];
            current = part.Result;
            StringBuilder description = new StringBuilder();
            description.AppendLine(ProtocolLabel(part.Protocol) + "；源字节偏移 " + part.Offset + "；字段表偏移相对于本条报文。");
            description.AppendLine(part.Message);
            if (current != null)
            {
                fields.ShowFields(current, part.Protocol);
                foreach (string error in current.Errors)
                {
                    description.AppendLine("错误：" + error);
                }
                foreach (string note in current.Notes)
                {
                    description.AppendLine("说明：" + note);
                }
            }
            if (parsedStream.PendingNibble.Length > 0)
            {
                description.AppendLine("流末尾保留半字节 " + parsedStream.PendingNibble + "，补全后再次点击识别。");
            }
            diagnostics.Text = description.ToString();
        }

        private string RequireOutput()
        {
            if (string.IsNullOrEmpty(output.Text))
            {
                throw new FormatException("请先生成报文。");
            }
            return output.Text;
        }

        private void OpenText()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "文本报文 (*.txt;*.hex;*.log)|*.txt;*.hex;*.log|所有文件 (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    input.Text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                }
            }
        }

        private void ExportReport()
        {
            if (parsedStream == null || parsedStream.Parts.Count > 1 || (parsedStream.Parts.Count == 0 && parsedStream.PendingNibble.Length == 0))
            {
                throw new FormatException("请先点击“识别并解析”，再导出结果。");
            }
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "UTF-8 文本 (*.txt)|*.txt", FileName = "报文解析.txt" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    StringBuilder report = new StringBuilder();
                    report.AppendLine("FrameTools 0.5.0");
                    report.AppendLine(automaticProtocol.Checked ? "自动识别规约" : "手动规约：" + protocol.Text);
                    report.AppendLine(status.Text);
                    report.AppendLine("整形报文流：");
                    report.AppendLine(parsedStream.FormattedText);
                    for (int i = 0; i < parsedStream.Parts.Count; i++)
                    {
                        StreamPart part = parsedStream.Parts[i];
                        report.AppendLine();
                        report.AppendLine("第 " + (i + 1) + " 条 / 片段：" + ProtocolLabel(part.Protocol) + "；源偏移 " + part.Offset + "；" + part.Kind);
                        report.AppendLine(Hex.Format(part.Bytes));
                        report.AppendLine(part.Message);
                        if (part.Result == null)
                        {
                            continue;
                        }
                        report.AppendLine("源偏移\t帧内偏移\t字节数\t字段\t原始字节\t解读");
                        foreach (FrameField field in part.Result.Fields)
                        {
                            report.AppendLine((part.Offset + field.Offset) + "\t" + field.Offset + "\t" + field.Length + "\t" + field.Name + "\t" + field.Raw + "\t" + field.Meaning);
                        }
                        foreach (string error in part.Result.Errors)
                        {
                            report.AppendLine("错误：" + error);
                        }
                        foreach (string note in part.Result.Notes)
                        {
                            report.AppendLine("说明：" + note);
                        }
                    }
                    if (parsedStream.PendingNibble.Length > 0)
                    {
                        report.AppendLine("流末尾半字节待补全：" + parsedStream.PendingNibble);
                    }
                    File.WriteAllText(dialog.FileName, report.ToString(), new UTF8Encoding(true));
                }
            }
        }

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (FormatException e)
            {
                MessageBox.Show(this, e.Message, "请检查输入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (IOException e)
            {
                MessageBox.Show(this, e.Message, "文件读写失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (UnauthorizedAccessException e)
            {
                MessageBox.Show(this, e.Message, "无法访问文件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (ExternalException e)
            {
                MessageBox.Show(this, e.Message, "系统操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
