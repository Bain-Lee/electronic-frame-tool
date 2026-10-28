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
        private readonly TabControl tabs = new TabControl();
        private readonly TextBox input = new TextBox();
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
        private FrameResult current;

        public MainForm()
        {
            Text = "FrameTools 0.1.0 — 电力报文工具";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(1000, 720);
            Size = new Size(1240, 900);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 248, 251);
            KeyPreview = true;

            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(16) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            Panel heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label
            {
                Text = "电力报文工作台", Font = new Font(Font.FontFamily, 17, FontStyle.Bold),
                ForeColor = Color.FromArgb(23, 49, 78), AutoSize = true, Location = new Point(0, 0)
            });
            heading.Controls.Add(new Label
            {
                Text = "解析字段 · 检查帧结构 · 参数组帧    |    离线使用    |    Windows / .NET Framework 4.8",
                AutoSize = true, ForeColor = Color.FromArgb(79, 94, 110), Location = new Point(2, 37)
            });
            shell.Controls.Add(heading, 0, 0);
            FlowLayoutPanel selector = Toolbar();
            selector.Controls.Add(new Label { Text = "当前协议", AutoSize = true, Margin = new Padding(0, 8, 10, 0) });
            protocol.DropDownStyle = ComboBoxStyle.DropDownList;
            protocol.Width = 425;
            protocol.Items.AddRange(new object[] { "DL/T 645-2007", "南网本地通信模块接口（试行）", "南网上行通信规约（2024版 V2.0）" });
            selector.Controls.Add(protocol);
            selector.Controls.Add(new Label { Text = "一次解析一帧；偏移从 0 开始", AutoSize = true, Margin = new Padding(16, 8, 0, 0) });
            shell.Controls.Add(selector, 0, 1);
            tabs.Dock = DockStyle.Fill;
            shell.Controls.Add(tabs, 0, 2);
            BuildParserTab();
            BuildComposerTab();
            protocol.SelectedIndexChanged += delegate
            {
                SetDefaults();
                fields.DataSource = null;
                current = null;
                diagnostics.Clear();
                status.Text = "协议已切换，请解析输入报文或载入当前协议示例。";
            };
            protocol.SelectedIndex = 0;
            LoadRequest();
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
            box.Font = new Font("Consolas", 11F);
            box.ScrollBars = ScrollBars.Vertical;
            box.ReadOnly = readOnly;
            box.BackColor = Color.White;
            box.HideSelection = false;
        }

        private void Button(FlowLayoutPanel panel, string text, Action action)
        {
            System.Windows.Forms.Button button = new System.Windows.Forms.Button
            {
                Text = text, AutoSize = true, Height = 31, MinimumSize = new Size(80, 31),
                Margin = new Padding(0, 0, 10, 0), Padding = new Padding(5, 0, 5, 0)
            };
            button.Click += delegate { Guard(action); };
            panel.Controls.Add(button);
        }

        private void BuildParserTab()
        {
            TabPage page = new TabPage("报文解析") { BackColor = BackColor, Padding = new Padding(10) };
            tabs.TabPages.Add(page);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            page.Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "十六进制报文    支持连续 HEX、空白分隔和 0x 前缀；点击结果行可定位字节", AutoSize = true }, 0, 0);
            ConfigureHex(input, false);
            input.AccessibleName = "待解析十六进制报文";
            input.TextChanged += delegate
            {
                current = null;
                fields.DataSource = null;
                diagnostics.Clear();
                status.Text = "报文已变更，请重新解析。";
                status.ForeColor = Color.FromArgb(79, 94, 110);
            };
            layout.Controls.Add(input, 0, 1);
            FlowLayoutPanel actions = Toolbar();
            Button(actions, "解析  Ctrl+Enter", ParseInput);
            Button(actions, "当前协议请求示例", LoadRequest);
            Button(actions, "645 电能应答示例", delegate
            {
                protocol.SelectedIndex = 0;
                input.Text = "68 01 00 00 00 00 00 68 91 08 33 33 34 33 AB 89 67 45 17 16";
                ParseInput();
            });
            Button(actions, "打开文本", OpenText);
            Button(actions, "导出解析", ExportReport);
            layout.Controls.Add(actions, 0, 2);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 3);
            fields.Dock = DockStyle.Fill;
            fields.ReadOnly = true;
            fields.AllowUserToAddRows = false;
            fields.AllowUserToDeleteRows = false;
            fields.RowHeadersVisible = false;
            fields.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            fields.MultiSelect = false;
            fields.AutoGenerateColumns = false;
            fields.BackgroundColor = Color.White;
            fields.BorderStyle = BorderStyle.FixedSingle;
            fields.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Offset", HeaderText = "偏移", Width = 60 });
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Length", HeaderText = "字节数", Width = 65 });
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "字段", Width = 175 });
            fields.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Raw", HeaderText = "原始字节", Width = 245 });
            fields.Columns[3].DefaultCellStyle.Font = new Font("Consolas", 10F);
            fields.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Meaning", HeaderText = "解读", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            });
            fields.SelectionChanged += delegate
            {
                if (current == null || fields.CurrentRow == null)
                {
                    return;
                }
                FrameField field = fields.CurrentRow.DataBoundItem as FrameField;
                if (field != null)
                {
                    input.Select(field.Offset * 3, Math.Max(0, field.Length * 3 - 1));
                }
            };
            layout.Controls.Add(fields, 0, 4);
            ConfigureHex(diagnostics, true);
            diagnostics.Font = Font;
            diagnostics.AccessibleName = "帧结构诊断与解析范围";
            layout.Controls.Add(diagnostics, 0, 5);
        }

        private void BuildComposerTab()
        {
            TabPage page = new TabPage("组装报文") { BackColor = BackColor, Padding = new Padding(12) };
            tabs.TabPages.Add(page);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 245));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            page.Controls.Add(layout);
            TableLayoutPanel parameters = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 4 };
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            AddParameter(parameters, 0, "控制域 / 控制码 C（HEX）", control);
            addressLabel.Text = "地址";
            AddParameter(parameters, 1, addressLabel, address);
            AddParameter(parameters, 2, "目的地址（本地 ADD=1）", destination);
            AddParameter(parameters, 3, "应用功能 AFN（HEX）", afn);
            AddParameter(parameters, 4, "帧序列 SEQ（HEX）", sequence);
            AddParameter(parameters, 5, "DI（DI3…DI0，645 可空）", identifier);
            AddParameter(parameters, 6, "前导 FE 数量（645）", preamble);
            preamble.Maximum = 4;
            Label hint = new Label
            {
                Text = "C / AFN / SEQ 均按十六进制输入。\n\n645：DI 单独填写，其他明文字节放入业务数据区。\n\n本地：ADD 位决定是否携带地址。\n\n上行：业务区按传输顺序填写完整的 DA / DI / 数据 / 可选 PW / Tp。",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(79, 94, 110), Padding = new Padding(15, 5, 0, 0)
            };
            parameters.Controls.Add(hint, 2, 0);
            parameters.SetColumnSpan(hint, 2);
            parameters.SetRowSpan(hint, 7);
            layout.Controls.Add(parameters, 0, 0);
            payloadLabel.Dock = DockStyle.Fill;
            payloadLabel.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(payloadLabel, 0, 1);
            ConfigureHex(payload, false);
            payload.AccessibleName = "组帧业务数据";
            layout.Controls.Add(payload, 0, 2);
            FlowLayoutPanel actions = Toolbar();
            Button(actions, "生成报文  Ctrl+Enter", Compose);
            Button(actions, "复制结果", delegate { Clipboard.SetText(RequireOutput()); });
            Button(actions, "发送到解析页", delegate
            {
                input.Text = RequireOutput();
                tabs.SelectedIndex = 0;
                ParseInput();
            });
            Button(actions, "恢复请求示例参数", SetDefaults);
            layout.Controls.Add(actions, 0, 3);
            ConfigureHex(output, true);
            output.AccessibleName = "组装结果";
            layout.Controls.Add(output, 0, 4);
            builderHelp.Dock = DockStyle.Fill;
            builderHelp.ForeColor = Color.FromArgb(79, 94, 110);
            builderHelp.Padding = new Padding(0, 8, 0, 0);
            layout.Controls.Add(builderHelp, 0, 5);
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
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(3, 4, 3, 4);
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
            ParseInput();
        }

        private void ParseInput()
        {
            // Clear the previous result before parsing, so invalid input never leaves a stale success state.
            current = null;
            fields.DataSource = null;
            diagnostics.Clear();
            status.Text = "正在解析…";
            status.ForeColor = Color.FromArgb(79, 94, 110);
            FrameResult result;
            try
            {
                result = FrameCodec.Parse(SelectedProtocol, input.Text);
            }
            catch (FormatException)
            {
                status.Text = "输入格式错误，尚未完成解析。";
                status.ForeColor = Color.Firebrick;
                throw;
            }
            input.Text = Hex.Format(result.Bytes);
            current = result;
            fields.DataSource = result.Fields;
            status.ForeColor = result.IsFrameValid ? Color.FromArgb(20, 110, 80) : Color.Firebrick;
            status.Text = result.IsFrameValid ? "帧结构校验通过 · " + result.Bytes.Length + " 字节 · 业务内容验证范围见下方"
                : "帧结构存在 " + result.Errors.Count + " 项错误 · 已保留可解析字段";
            diagnostics.Text = string.Join(Environment.NewLine,
                result.Errors.Select(e => "错误：" + e).Concat(result.Notes.Select(n => "说明：" + n)));
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
                    ParseInput();
                }
            }
        }

        private void ExportReport()
        {
            if (current == null)
            {
                throw new FormatException("请先解析报文。");
            }
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "UTF-8 文本 (*.txt)|*.txt", FileName = "报文解析.txt" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    StringBuilder report = new StringBuilder();
                    report.AppendLine("FrameTools 0.1.0");
                    report.AppendLine(protocol.Text);
                    report.AppendLine(status.Text);
                    report.AppendLine(Hex.Format(current.Bytes));
                    report.AppendLine("偏移\t字节数\t字段\t原始字节\t解读");
                    foreach (FrameField field in current.Fields)
                    {
                        report.AppendLine(field.Offset + "\t" + field.Length + "\t" + field.Name + "\t" + field.Raw + "\t" + field.Meaning);
                    }
                    report.AppendLine(diagnostics.Text);
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
