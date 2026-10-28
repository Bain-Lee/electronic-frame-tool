using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace FrameTools
{
    public sealed class MainForm : Form
    {
        private readonly ComboBox protocol = new ComboBox();
        private readonly CheckBox automaticProtocol = new ProtocolSwitch();
        private readonly TabControl tabs = new WorkspaceTabs();
        private readonly Label protocolLabel = new Label();
        private readonly TextBox input = new TextBox();
        private readonly TextBox formatted = new TextBox();
        private readonly ComboBox streamParts = new ComboBox();
        private readonly TextBox diagnostics = new TextBox();
        private readonly DataGridView fields = new DataGridView();
        private readonly Label status = new Label();
        private readonly TextBox control = new TextBox();
        private readonly TextBox address = new TextBox();
        private readonly TextBox destination = new TextBox();
        private readonly TextBox afn = new TextBox();
        private readonly TextBox sequence = new TextBox();
        private readonly TextBox identifier = new TextBox();
        private readonly NumericUpDown preamble = new NumericUpDown();
        private readonly TextBox payload = new TextBox();
        private readonly TextBox output = new TextBox();
        private readonly Label addressLabel = new Label();
        private readonly Label payloadLabel = new Label();
        private readonly Label builderHelp = new Label();
        private WorkspaceButton exportButton;
        private FrameResult current;
        private StreamParseResult parsedStream;
        private int[] formattedStarts = new int[0];

        public MainForm()
        {
            Text = "FrameTools 0.3.0 — 电力报文工具";
            Font = new Font("Microsoft YaHei UI", 9.5F);
            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(1000, 800);
            Size = new Size(1240, 960);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = WorkspaceStyle.Background;
            ForeColor = WorkspaceStyle.Text;
            KeyPreview = true;

            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(24, 18, 24, 20) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            Panel heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label
            {
                Text = "电力报文", Font = new Font(Font.FontFamily, 21, FontStyle.Bold),
                ForeColor = WorkspaceStyle.Text, AutoSize = true, Location = new Point(0, 0)
            });
            heading.Controls.Add(new Label
            {
                Text = "查看报文结构，核对每个字节。",
                AutoSize = true, ForeColor = WorkspaceStyle.Secondary, Location = new Point(2, 42)
            });
            heading.Controls.Add(new Label { Text = "v0.3.0", Dock = DockStyle.Right, Width = 90, TextAlign = ContentAlignment.MiddleRight, ForeColor = WorkspaceStyle.Secondary });
            shell.Controls.Add(heading, 0, 0);
            FlowLayoutPanel selector = Toolbar();
            protocolLabel.Text = "手动规约";
            protocolLabel.AutoSize = true;
            protocolLabel.Margin = new Padding(0, 11, 14, 0);
            selector.Controls.Add(protocolLabel);
            protocol.Name = "protocol";
            ConfigurePicker(protocol);
            protocol.Width = 385;
            protocol.Margin = new Padding(0, 7, 0, 0);
            protocol.Items.AddRange(new object[] { "DL/T 645-2007", "南网本地通信模块接口（试行）", "南网上行通信规约（2024版 V2.0）" });
            selector.Controls.Add(protocol);
            automaticProtocol.Name = "automaticProtocol";
            automaticProtocol.Text = "自动识别规约";
            automaticProtocol.AutoSize = true;
            automaticProtocol.Checked = true;
            automaticProtocol.Margin = new Padding(20, 0, 0, 0);
            selector.Controls.Add(automaticProtocol);
            shell.Controls.Add(selector, 0, 1);
            tabs.Dock = DockStyle.Fill;
            tabs.Name = "workspaceTabs";
            shell.Controls.Add(tabs, 0, 2);
            BuildParserTab();
            BuildComposerTab();
            protocol.SelectedIndexChanged += delegate
            {
                SetDefaults();
                InvalidateParsedInput();
            };
            automaticProtocol.CheckedChanged += delegate
            {
                UpdateProtocolMode();
                InvalidateParsedInput();
            };
            tabs.SelectedIndexChanged += delegate { UpdateProtocolMode(); };
            protocol.SelectedIndex = 0;
            UpdateProtocolMode();
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (tabs.SelectedIndex == 0)
                    {
                        Guard(ParseInput);
                    }
                    else
                    {
                        Guard(Compose);
                    }
                }
            };
        }

        private ProtocolKind SelectedProtocol { get { return (ProtocolKind)protocol.SelectedIndex; } }

        private static FlowLayoutPanel Toolbar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        }

        private static void ConfigureHex(TextBox box, bool readOnly)
        {
            box.Dock = DockStyle.Fill;
            box.Multiline = true;
            box.MaxLength = int.MaxValue;
            box.Font = new Font("Consolas", 10.5F);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.ScrollBars = ScrollBars.Vertical;
            box.ReadOnly = readOnly;
            box.BackColor = WorkspaceStyle.Surface;
            box.ForeColor = WorkspaceStyle.Text;
            box.HideSelection = false;
        }

        private static void ConfigurePicker(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.FlatStyle = FlatStyle.Flat;
            box.BackColor = WorkspaceStyle.Surface;
            box.DrawMode = DrawMode.OwnerDrawFixed;
            box.ItemHeight = Math.Max(24, box.Font.Height + 6);
            box.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= box.Items.Count)
                {
                    return;
                }
                bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                Color background = !box.Enabled ? Color.FromArgb(232, 232, 237) : selected ? WorkspaceStyle.Blue : WorkspaceStyle.Surface;
                using (Brush fill = new SolidBrush(background))
                {
                    e.Graphics.FillRectangle(fill, e.Bounds);
                }
                Rectangle textBounds = e.Bounds;
                textBounds.Inflate(-5, 0);
                TextRenderer.DrawText(e.Graphics, box.Items[e.Index].ToString(), e.Font, textBounds,
                    !box.Enabled ? WorkspaceStyle.Secondary : selected ? Color.White : WorkspaceStyle.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
        }

        private WorkspaceButton Button(FlowLayoutPanel panel, string text, Action action)
        {
            WorkspaceButton button = new WorkspaceButton
            {
                Text = text, AutoSize = true, MinimumSize = new Size(80, 36),
                Margin = new Padding(0, 0, 10, 0)
            };
            button.Click += delegate { Guard(action); };
            panel.Controls.Add(button);
            return button;
        }

        private void UpdateProtocolMode()
        {
            bool composer = tabs.SelectedIndex == 1;
            protocolLabel.Text = composer ? "组帧规约" : "手动规约";
            protocol.Enabled = composer || !automaticProtocol.Checked;
            automaticProtocol.Visible = !composer;
        }

        private void BuildParserTab()
        {
            TabPage page = new TabPage("报文解析") { BackColor = BackColor, Padding = new Padding(14) };
            tabs.TabPages.Add(page);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            page.Controls.Add(layout);
            TableLayoutPanel editors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
            editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            editors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            ConfigureHex(input, false);
            input.Name = "rawInput";
            input.AccessibleName = "待解析十六进制报文";
            input.TextChanged += delegate { InvalidateParsedInput(); };
            TableLayoutPanel sourcePane = EditorPane("原始报文", input);
            sourcePane.Margin = new Padding(0, 0, 8, 0);
            editors.Controls.Add(sourcePane, 0, 0);
            ConfigureHex(formatted, true);
            formatted.Name = "formattedStream";
            formatted.AccessibleName = "自动整形报文流";
            formatted.WordWrap = false;
            formatted.ScrollBars = ScrollBars.Both;
            TableLayoutPanel formattedPane = EditorPane("整形结果", formatted);
            formattedPane.Margin = new Padding(8, 0, 0, 0);
            editors.Controls.Add(formattedPane, 1, 0);
            layout.Controls.Add(editors, 0, 0);
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
            layout.Controls.Add(actions, 0, 1);
            FlowLayoutPanel navigation = Toolbar();
            navigation.Controls.Add(new Label { Text = "逐条查看", AutoSize = true, Margin = new Padding(0, 10, 14, 0), ForeColor = WorkspaceStyle.Secondary });
            streamParts.Name = "streamParts";
            ConfigurePicker(streamParts);
            streamParts.Width = 565;
            streamParts.Margin = new Padding(0, 7, 12, 0);
            streamParts.SelectedIndexChanged += delegate { ShowStreamPart(); };
            navigation.Controls.Add(streamParts);
            Button(navigation, "上一条", delegate
            {
                if (streamParts.SelectedIndex > 0)
                {
                    streamParts.SelectedIndex--;
                }
            });
            Button(navigation, "下一条", delegate
            {
                if (streamParts.SelectedIndex < streamParts.Items.Count - 1)
                {
                    streamParts.SelectedIndex++;
                }
            });
            layout.Controls.Add(navigation, 0, 2);
            status.Name = "parseStatus";
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = WorkspaceStyle.Secondary;
            layout.Controls.Add(status, 0, 3);
            fields.Name = "parsedFields";
            fields.Dock = DockStyle.Fill;
            fields.ReadOnly = true;
            fields.AllowUserToAddRows = false;
            fields.AllowUserToDeleteRows = false;
            fields.RowHeadersVisible = false;
            fields.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            fields.MultiSelect = false;
            fields.AutoGenerateColumns = false;
            fields.BackgroundColor = Color.White;
            fields.BorderStyle = BorderStyle.None;
            fields.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            fields.GridColor = Color.FromArgb(229, 229, 234);
            fields.EnableHeadersVisualStyles = false;
            fields.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            fields.ColumnHeadersHeight = 36;
            fields.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(229, 229, 234);
            fields.ColumnHeadersDefaultCellStyle.ForeColor = WorkspaceStyle.Secondary;
            fields.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 239, 255);
            fields.DefaultCellStyle.SelectionForeColor = WorkspaceStyle.Text;
            fields.DefaultCellStyle.Padding = new Padding(5, 5, 5, 5);
            fields.RowTemplate.Height = 34;
            fields.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Offset", HeaderText = "帧内偏移", Width = 85 });
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Length", HeaderText = "字节数", Width = 65 });
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "字段", Width = 175 });
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
                    int selected = streamParts.SelectedIndex;
                    if (selected >= 0 && selected < formattedStarts.Length)
                    {
                        formatted.Select(formattedStarts[selected] + field.Offset * 3, Math.Max(0, field.Length * 3 - 1));
                        formatted.ScrollToCaret();
                    }
                }
            };
            layout.Controls.Add(fields, 0, 4);
            ConfigureHex(diagnostics, true);
            diagnostics.Name = "parseDiagnostics";
            diagnostics.Font = Font;
            diagnostics.ForeColor = WorkspaceStyle.Secondary;
            diagnostics.AccessibleName = "帧结构诊断与解析范围";
            layout.Controls.Add(EditorSurface(diagnostics), 0, 5);
        }

        private TableLayoutPanel EditorPane(string title, TextBox editor)
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

        private static Panel EditorSurface(TextBox editor)
        {
            Panel surface = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(0), BackColor = WorkspaceStyle.Background };
            surface.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (surface.Width < 2 || surface.Height < 2)
                {
                    return;
                }
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (System.Drawing.Drawing2D.GraphicsPath path = WorkspaceStyle.RoundedRectangle(new RectangleF(0.5F, 0.5F, surface.Width - 1F, surface.Height - 1F), 12))
                using (Brush fill = new SolidBrush(WorkspaceStyle.Surface))
                using (Pen border = new Pen(WorkspaceStyle.Border))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
            };
            editor.BorderStyle = BorderStyle.None;
            surface.Controls.Add(editor);
            return surface;
        }

        private void BuildComposerTab()
        {
            TabPage page = new TabPage("组装报文") { BackColor = BackColor, Padding = new Padding(12) };
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
            TableLayoutPanel parameters = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 4 };
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            AddParameter(parameters, 0, "控制码 C", control);
            addressLabel.Text = "地址";
            AddParameter(parameters, 1, addressLabel, address);
            AddParameter(parameters, 2, "目的地址（本地 ADD=1）", destination);
            AddParameter(parameters, 3, "应用功能 AFN（HEX）", afn);
            AddParameter(parameters, 4, "帧序列 SEQ（HEX）", sequence);
            AddParameter(parameters, 5, "数据标识 DI（645 可空）", identifier);
            AddParameter(parameters, 6, "前导 FE 数量（645）", preamble);
            preamble.Maximum = 4;
            Label hint = new Label
            {
                Text = "C / AFN / SEQ 均按十六进制输入。\n\n645：DI 单独填写，其他明文字节放入业务数据区。\n\n本地：ADD 位决定是否携带地址。\n\n上行：业务区按传输顺序填写完整的 DA / DI / 数据 / 可选 PW / Tp。",
                Dock = DockStyle.Fill, ForeColor = WorkspaceStyle.Secondary, Padding = new Padding(24, 5, 0, 0)
            };
            parameters.Controls.Add(hint, 2, 0);
            parameters.SetColumnSpan(hint, 2);
            parameters.SetRowSpan(hint, 7);
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
            builderHelp.Padding = new Padding(0, 8, 0, 0);
            layout.Controls.Add(builderHelp, 0, 6);
            control.TextChanged += delegate { UpdateAddressState(); output.Clear(); };
            foreach (TextBox parameter in new[] { address, destination, afn, sequence, identifier, payload })
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
            address.Text = is645 ? "000000000001" : local ? "440106000001" : "06 01 44 01 00 00 01";
            destination.Text = "000000000001";
            afn.Text = local ? "03" : "0C";
            sequence.Text = local ? "01" : "60";
            identifier.Text = is645 ? "00010000" : local ? "E8000301" : "";
            preamble.Value = is645 ? 4 : 0;
            afn.Enabled = !is645;
            sequence.Enabled = !is645;
            identifier.Enabled = is645 || local;
            preamble.Enabled = is645;
            payload.Text = is645 || local ? "" : "01 01 00 00 01 00";
            addressLabel.Text = is645 ? "表地址（高字节先写）" : local ? "源地址（高字节先写）" : "地址 A（7 字节传输顺序）";
            payloadLabel.Text = is645 ? "业务数据 HEX（DI 之后的明文字节，自动加 33H）"
                : local ? "数据标识内容 HEX（不含 DI，不做 ±33H）" : "完整应用业务区 HEX（含 DA / DI / 数据 / 可选 PW / Tp；不做 ±33H）";
            builderHelp.Text = is645 ? "自动处理地址字节序、DI 字节序、数据域 +33H、L 和 CS。读地址时 C=13，DI 清空。写数据的密码及操作者代码由使用者按标准填入。"
                : local ? "按本机试行标准生成：总长度 L、可选 12 字节地址、8 位 SEQ、4 字节 DI 和 CS。C 的 D5 为 ADD；ADD=0 时不使用源、目的地址。"
                : "按上行标准 2024版V2.0 生成：直接 BIN 长度 L，重复两次；7 字节地址；CS 从 C 开始累加。PW/MAC 与业务内容由使用者准备，不计算认证值。";
            UpdateAddressState();
            output.Clear();
        }

        private void UpdateAddressState()
        {
            bool local = SelectedProtocol == ProtocolKind.CsgLocal;
            if (!local)
            {
                address.Enabled = true;
                destination.Enabled = false;
                return;
            }
            bool addressed = false;
            try
            {
                byte[] bytes = Hex.Parse(control.Text);
                addressed = bytes.Length == 1 && (bytes[0] & 0x20) != 0;
            }
            catch (FormatException)
            {
                // Keep address inputs disabled while the control byte is incomplete.
            }
            address.Enabled = !local || addressed;
            destination.Enabled = local && addressed;
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
                frame = FrameCodec.Build645(address.Text, c, di.Concat(data).ToArray(), (int)preamble.Value);
            }
            else if (SelectedProtocol == ProtocolKind.CsgLocal)
            {
                frame = FrameCodec.BuildLocal(c, address.Text, destination.Text, OneByte(afn, "AFN"),
                    OneByte(sequence, "SEQ"), Hex.Identifier(identifier.Text), data);
            }
            else
            {
                frame = FrameCodec.BuildUplink(c, Hex.Parse(address.Text), OneByte(afn, "AFN"), OneByte(sequence, "SEQ"), data);
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
            streamParts.Items.Clear();
            formatted.Clear();
            formattedStarts = new int[0];
            diagnostics.Clear();
            exportButton.Enabled = false;
        }

        private void InvalidateParsedInput()
        {
            ClearParsedView();
            status.ForeColor = WorkspaceStyle.Secondary;
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                status.Text = "粘贴报文后，点击“识别并解析”。支持连续多帧与 Ctrl+Enter。";
                return;
            }
            status.Text = "输入已更新，点击“识别并解析”查看结果。";
        }

        private void ParseInput()
        {
            ClearParsedView();
            status.Text = "正在解析…";
            status.ForeColor = Color.FromArgb(79, 94, 110);
            try
            {
                parsedStream = FrameStream.Parse(input.Text, automaticProtocol.Checked ? (ProtocolKind?)null : SelectedProtocol);
            }
            catch (FormatException e)
            {
                status.Text = "输入格式错误；请保留 HEX 字节并移除日志时间戳或文字。";
                status.ForeColor = Color.Firebrick;
                diagnostics.Text = e.Message;
                return;
            }
            formatted.Text = parsedStream.FormattedText;
            exportButton.Enabled = parsedStream.Parts.Count > 0 || parsedStream.PendingNibble.Length > 0;
            formattedStarts = new int[parsedStream.Parts.Count];
            int character = 0;
            streamParts.BeginUpdate();
            for (int i = 0; i < parsedStream.Parts.Count; i++)
            {
                StreamPart part = parsedStream.Parts[i];
                formattedStarts[i] = character;
                character += Math.Max(0, part.Bytes.Length * 3 - 1) + Environment.NewLine.Length;
                string description = part.Kind == StreamPartKind.Frame
                    ? ProtocolLabel(part.Protocol) + (part.Result.IsFrameValid ? " · 结构通过" : " · 结构错误")
                    : part.Kind == StreamPartKind.Incomplete ? "未完整，等待后续字节"
                    : part.Kind == StreamPartKind.Ambiguous ? "规约有歧义，请手动选择" : "未识别数据";
                streamParts.Items.Add("第 " + (i + 1) + " 条 / 片段 · " + description + " · 源偏移 " + part.Offset + " · " + part.Bytes.Length + " 字节");
            }
            streamParts.EndUpdate();
            int frames = parsedStream.Parts.Count(p => p.Kind == StreamPartKind.Frame);
            int valid = parsedStream.Parts.Count(p => p.Result != null && p.Result.IsFrameValid);
            int unresolved = parsedStream.Parts.Count - frames;
            status.ForeColor = frames > valid ? Color.Firebrick
                : unresolved > 0 || parsedStream.PendingNibble.Length > 0 ? Color.DarkGoldenrod : Color.FromArgb(20, 110, 80);
            status.Text = "识别 " + frames + " 帧 · 结构通过 " + valid + " · 结构错误 " + (frames - valid)
                + " · 待定片段 " + unresolved + (parsedStream.PendingNibble.Length > 0 ? " · 末尾半字节待补全" : "");
            if (parsedStream.Parts.Count > 0)
            {
                streamParts.SelectedIndex = 0;
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

        private void ShowStreamPart()
        {
            current = null;
            fields.DataSource = null;
            if (parsedStream == null || streamParts.SelectedIndex < 0)
            {
                return;
            }
            int selected = streamParts.SelectedIndex;
            StreamPart part = parsedStream.Parts[selected];
            current = part.Result;
            StringBuilder description = new StringBuilder();
            description.AppendLine(ProtocolLabel(part.Protocol) + "；源字节偏移 " + part.Offset + "；字段表偏移相对于本条报文。");
            description.AppendLine(part.Message);
            if (current != null)
            {
                fields.DataSource = current.Fields;
                foreach (string error in current.Errors)
                {
                    description.AppendLine("错误：" + error);
                }
                foreach (string note in current.Notes)
                {
                    description.AppendLine("说明：" + note);
                }
            }
            else
            {
                formatted.Select(formattedStarts[selected], Math.Max(0, part.Bytes.Length * 3 - 1));
                formatted.ScrollToCaret();
            }
            if (parsedStream.PendingNibble.Length > 0)
            {
                description.AppendLine("流末尾保留半字节 " + parsedStream.PendingNibble + "，继续输入后自动补全。");
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
            if (parsedStream == null || (parsedStream.Parts.Count == 0 && parsedStream.PendingNibble.Length == 0))
            {
                throw new FormatException("请先点击“识别并解析”，再导出结果。");
            }
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "UTF-8 文本 (*.txt)|*.txt", FileName = "报文解析.txt" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    StringBuilder report = new StringBuilder();
                    report.AppendLine("FrameTools 0.3.0");
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
