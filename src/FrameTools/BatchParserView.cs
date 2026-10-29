using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace FrameTools
{
    public sealed class BatchParserView : UserControl
    {
        private readonly Func<ProtocolKind?> getProtocol;
        private readonly WorkspaceTextBox input = new WorkspaceTextBox();
        private readonly WorkspaceTextBox selectedFrame = new WorkspaceTextBox();
        private readonly WorkspaceTextBox diagnostics = new WorkspaceTextBox();
        private readonly DataGridView summary = new DataGridView();
        private readonly FrameFieldGrid fields = new FrameFieldGrid();
        private readonly Label status = new Label();
        private readonly WorkspaceButton export = new WorkspaceButton();
        private StreamParseResult parsed;

        public event EventHandler SelectionChanged;

        public ProtocolKind? SelectedProtocol
        {
            get
            {
                StreamPart part = summary.CurrentRow == null ? null : summary.CurrentRow.Tag as StreamPart;
                return part != null && part.Result != null ? part.Protocol : null;
            }
        }

        public BatchParserView(Func<ProtocolKind?> getProtocol)
        {
            this.getProtocol = getProtocol;
            Dock = DockStyle.Fill;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(layout);
            WorkspaceGroupBox sourceGroup = new WorkspaceGroupBox { Text = "批量输入报文（连续 HEX / 空格 / 换行）", Dock = DockStyle.Fill, Padding = new Padding(7) };
            TableLayoutPanel source = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            source.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            source.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddButton(actions, "开始多帧解析", "parseBatch", Parse, true);
            AddButton(actions, "打开文本", "openBatch", OpenText, false);
            AddButton(actions, "清空", "clearBatch", delegate { input.Clear(); }, false);
            export.Text = "导出全部解析";
            export.Enabled = false;
            export.Click += delegate { Guard(Export); };
            actions.Controls.Add(export);
            source.Controls.Add(actions, 0, 0);
            ConfigureHex(input, "batchInput", false);
            input.TextChanged += delegate { InvalidateResults(); };
            WorkspaceEditor inputSurface = new WorkspaceEditor();
            inputSurface.Controls.Add(input);
            source.Controls.Add(inputSurface, 0, 1);
            sourceGroup.Controls.Add(source);
            layout.Controls.Add(sourceGroup, 0, 0);

            SplitContainer split = new SplitContainer
            {
                Name = "batchSplit", Size = new Size(1100, 460), Dock = DockStyle.Fill,
                Panel1MinSize = 220, Panel2MinSize = 450, SplitterDistance = 420, SplitterWidth = 5,
                FixedPanel = FixedPanel.Panel1
            };
            layout.Controls.Add(split, 0, 1);
            GroupBox listGroup = new WorkspaceGroupBox { Text = "解析结果摘要", Dock = DockStyle.Fill, Padding = new Padding(7) };
            split.Panel1.Controls.Add(listGroup);
            summary.Name = "batchSummary";
            WorkspaceStyle.ConfigureGrid(summary);
            summary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", Width = 32 });
            summary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Width = 60 });
            summary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "长度", Width = 50 });
            summary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "规约", Width = 90 });
            summary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "源偏移", Width = 65 });
            summary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "摘要", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 100 });
            foreach (DataGridViewColumn column in summary.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            summary.CurrentCellChanged += delegate { ShowSelected(); };
            listGroup.Controls.Add(summary);

            GroupBox details = new WorkspaceGroupBox { Text = "选中帧详细解析", Dock = DockStyle.Fill, Padding = new Padding(7) };
            split.Panel2.Controls.Add(details);
            TableLayoutPanel detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
            details.Controls.Add(detailLayout);
            ConfigureHex(selectedFrame, "batchSelectedFrame", true);
            selectedFrame.WordWrap = false;
            WorkspaceEditor selectedSurface = new WorkspaceEditor();
            selectedSurface.Controls.Add(selectedFrame);
            detailLayout.Controls.Add(selectedSurface, 0, 0);
            fields.Name = "batchFields";
            WorkspaceStyle.ConfigureGrid(fields);
            WorkspaceStyle.AddFieldColumns(fields);
            fields.Columns[2].Width = 185;
            fields.CurrentCellChanged += delegate
            {
                if (fields.CurrentRow == null)
                {
                    return;
                }
                FrameField field = fields.CurrentRow.DataBoundItem as FrameField;
                if (field != null)
                {
                    selectedFrame.Select(field.Offset * 3, Math.Max(0, field.Length * 3 - 1));
                    selectedFrame.ScrollToCaret();
                }
            };
            detailLayout.Controls.Add(fields, 0, 1);
            ConfigureHex(diagnostics, "batchDiagnostics", true);
            diagnostics.Font = Font;
            diagnostics.Tag = "secondary";
            detailLayout.Controls.Add(diagnostics, 0, 2);
            status.Name = "batchStatus";
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 2);
            InvalidateResults();
        }

        public string InputText
        {
            get { return input.Text; }
            set { input.Text = value; }
        }

        private static void ConfigureHex(WorkspaceTextBox box, string name, bool readOnly)
        {
            box.Name = name;
            box.Dock = DockStyle.Fill;
            box.Multiline = true;
            box.ReadOnly = readOnly;
            box.MaxLength = int.MaxValue;
            box.Font = new Font("Consolas", 10F);
            box.BorderStyle = BorderStyle.None;
            box.HideSelection = false;
        }

        private void AddButton(FlowLayoutPanel panel, string text, string name, Action action, bool primary)
        {
            WorkspaceButton button = new WorkspaceButton { Text = text, Name = name, Primary = primary };
            button.Click += delegate { Guard(action); };
            panel.Controls.Add(button);
        }

        public void InvalidateResults()
        {
            parsed = null;
            fields.DataSource = null;
            summary.Rows.Clear();
            selectedFrame.Clear();
            diagnostics.Clear();
            export.Enabled = false;
            status.Tag = "secondary";
            status.ForeColor = WorkspaceStyle.Secondary;
            status.Text = "粘贴日志中的 HEX 后，点击“开始多帧解析”。";
            NotifySelectionChanged();
        }

        public void Parse()
        {
            InvalidateResults();
            try
            {
                parsed = FrameStream.Parse(input.Text, getProtocol());
            }
            catch (FormatException e)
            {
                status.Tag = "error";
                status.ForeColor = WorkspaceStyle.Error;
                status.Text = "输入格式错误，请移除时间戳或收发标签。";
                diagnostics.Text = e.Message;
                return;
            }
            summary.SuspendLayout();
            for (int i = 0; i < parsed.Parts.Count; i++)
            {
                StreamPart part = parsed.Parts[i];
                string state = part.Result != null ? part.Result.IsFrameValid ? "通过" : "错误"
                    : part.Kind == StreamPartKind.Incomplete ? "未完整" : part.Kind == StreamPartKind.Ambiguous ? "歧义" : "未识别";
                int row = summary.Rows.Add(i + 1, state, part.Bytes.Length, ProtocolName(part.Protocol), part.Offset, part.Message);
                summary.Rows[row].Tag = part;
            }
            summary.ResumeLayout();
            int valid = parsed.Parts.Count(p => p.Result != null && p.Result.IsFrameValid);
            int frames = parsed.Parts.Count(p => p.Result != null);
            int pending = parsed.Parts.Count - frames;
            status.Tag = frames > valid ? "error" : pending > 0 || parsed.PendingNibble.Length > 0 ? "warning" : "success";
            status.ForeColor = status.Tag.ToString() == "error" ? WorkspaceStyle.Error : status.Tag.ToString() == "warning" ? WorkspaceStyle.Warning : WorkspaceStyle.Success;
            status.Text = "解析完成：共 " + frames + " 帧，通过 " + valid + "，错误 " + (frames - valid) + "，待定片段 " + pending
                + (parsed.PendingNibble.Length > 0 ? "；末尾半字节 " + parsed.PendingNibble + " 待补全" : "");
            export.Enabled = parsed.Parts.Count > 0 || parsed.PendingNibble.Length > 0;
            if (summary.Rows.Count > 0)
            {
                summary.CurrentCell = summary.Rows[0].Cells[0];
                ShowSelected();
            }
            else if (parsed.PendingNibble.Length > 0)
            {
                diagnostics.Text = "保留末尾半字节：" + parsed.PendingNibble;
            }
        }

        private static string ProtocolName(ProtocolKind? protocol)
        {
            return !protocol.HasValue ? "待定" : protocol.Value == ProtocolKind.Dlt645 ? "645-2007" : protocol.Value == ProtocolKind.CsgLocal ? "南网本地" : "南网上行";
        }

        private void ShowSelected()
        {
            NotifySelectionChanged();
            fields.DataSource = null;
            if (summary.CurrentRow == null)
            {
                return;
            }
            StreamPart part = summary.CurrentRow.Tag as StreamPart;
            if (part == null)
            {
                return;
            }
            selectedFrame.Text = Hex.Format(part.Bytes);
            StringBuilder text = new StringBuilder();
            text.AppendLine(ProtocolName(part.Protocol) + "；源字节偏移 " + part.Offset + "；长度 " + part.Bytes.Length + " 字节。");
            text.AppendLine(part.Message);
            if (part.Result != null)
            {
                fields.ShowFields(part.Result, part.Protocol);
                foreach (string error in part.Result.Errors)
                {
                    text.AppendLine("错误：" + error);
                }
                foreach (string note in part.Result.Notes)
                {
                    text.AppendLine("说明：" + note);
                }
            }
            if (parsed != null && parsed.PendingNibble.Length > 0)
            {
                text.AppendLine("流末尾半字节待补全：" + parsed.PendingNibble);
            }
            diagnostics.Text = text.ToString();
        }

        private void NotifySelectionChanged()
        {
            if (SelectionChanged != null)
            {
                SelectionChanged(this, EventArgs.Empty);
            }
        }

        private void OpenText()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "HEX 文本|*.txt;*.hex;*.log|所有文件|*.*" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    input.Text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                }
            }
        }

        private void Export()
        {
            if (parsed == null)
            {
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "UTF-8 文本|*.txt", FileName = "多帧解析.txt" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                StringBuilder text = new StringBuilder("FrameTools 0.5.0 多帧解析" + Environment.NewLine);
                text.AppendLine(status.Text);
                text.AppendLine(parsed.FormattedText);
                for (int i = 0; i < parsed.Parts.Count; i++)
                {
                    StreamPart part = parsed.Parts[i];
                    text.AppendLine("第 " + (i + 1) + " 条；" + ProtocolName(part.Protocol) + "；源偏移 " + part.Offset);
                    text.AppendLine(part.Message);
                    if (part.Result == null)
                    {
                        continue;
                    }
                    foreach (FrameField field in part.Result.Fields)
                    {
                        text.AppendLine(field.Offset + "\t" + field.Length + "\t" + field.Name + "\t" + field.Raw + "\t" + field.Meaning);
                    }
                    foreach (string error in part.Result.Errors)
                    {
                        text.AppendLine("错误：" + error);
                    }
                    foreach (string note in part.Result.Notes)
                    {
                        text.AppendLine("说明：" + note);
                    }
                }
                File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true));
            }
        }

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (IOException e)
            {
                MessageBox.Show(this, e.Message, "文件操作失败");
            }
            catch (UnauthorizedAccessException e)
            {
                MessageBox.Show(this, e.Message, "无法访问文件");
            }
            catch (ExternalException e)
            {
                MessageBox.Show(this, e.Message, "系统操作失败");
            }
        }
    }
}
