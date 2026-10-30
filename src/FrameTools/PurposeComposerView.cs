using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameTools
{
    internal sealed class PurposeComposerView : UserControl
    {
        private sealed class ChoiceItem
        {
            public string Value;
            public string Name;

            public override string ToString()
            {
                return Name;
            }
        }

        private readonly ListBox categories = new ListBox();
        private readonly ComboBox commands = new ComboBox();
        private readonly Label note = new Label();
        private readonly Panel scroll = new Panel();
        private readonly TableLayoutPanel fields = new TableLayoutPanel();
        private readonly WorkspaceTextBox preview = new WorkspaceTextBox();
        private readonly Label resultLabel = new Label();
        private readonly Label status = new Label();
        private readonly WorkspaceButton generate = new WorkspaceButton();
        private readonly WorkspaceButton copy = new WorkspaceButton();
        private readonly WorkspaceButton toParser = new WorkspaceButton();
        private readonly Dictionary<string, Control> editors = new Dictionary<string, Control>();
        private readonly Action<string> openParser;
        private PurposeCommand[] available = new PurposeCommand[0];
        private PurposeCommand command;
        private Dictionary<string, string> values = new Dictionary<string, string>();
        private bool building;
        private PurposeComposition result;

        public PurposeComposerView(Action<string> openParser)
        {
            this.openParser = openParser;
            Dock = DockStyle.Fill;
            Name = "purposeComposer";
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(layout);

            WorkspaceGroupBox categoryBox = new WorkspaceGroupBox
            {
                Text = "用途分类", Dock = DockStyle.Fill, Padding = new Padding(6, 8, 6, 6), Margin = new Padding(0, 0, 12, 0)
            };
            categories.Name = "purposeCategory";
            categories.Dock = DockStyle.Fill;
            categories.BorderStyle = BorderStyle.None;
            categories.IntegralHeight = false;
            categories.DrawMode = DrawMode.OwnerDrawFixed;
            categories.ItemHeight = 34;
            categories.DrawItem += DrawCategory;
            categoryBox.Controls.Add(categories);
            layout.Controls.Add(categoryBox, 0, 0);
            layout.SetRowSpan(categoryBox, 7);

            TableLayoutPanel heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.Controls.Add(new Label { Text = "操作", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            commands.Name = "purposeCommand";
            commands.Dock = DockStyle.Fill;
            commands.Margin = new Padding(3, 5, 3, 5);
            MainForm.ConfigurePicker(commands);
            heading.Controls.Add(commands, 1, 0);
            layout.Controls.Add(heading, 1, 0);
            note.Name = "purposeNote";
            note.Dock = DockStyle.Fill;
            note.Tag = "secondary";
            note.Padding = new Padding(0, 4, 0, 2);
            layout.Controls.Add(note, 1, 1);

            scroll.Name = "purposeFieldsScroll";
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.Margin = new Padding(0);
            fields.Name = "purposeFields";
            fields.Dock = DockStyle.Top;
            fields.AutoSize = true;
            fields.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            fields.ColumnCount = 3;
            fields.Margin = new Padding(0);
            fields.Padding = new Padding(0, 0, 8, 0);
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            scroll.Controls.Add(fields);
            layout.Controls.Add(scroll, 1, 2);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 5, 0, 0) };
            AddButton(actions, generate, "生成报文", "composePurpose", Generate);
            generate.Primary = true;
            AddButton(actions, copy, "复制报文", "copyPurpose", CopyResult);
            AddButton(actions, toParser, "发送到解析页", "purposeToParser", delegate { this.openParser(preview.Text); });
            WorkspaceButton reset = new WorkspaceButton();
            AddButton(actions, reset, "恢复本用途默认值", "resetPurpose", ResetValues);
            layout.Controls.Add(actions, 1, 3);
            resultLabel.Dock = DockStyle.Fill;
            resultLabel.Font = new Font(Font, FontStyle.Bold);
            resultLabel.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(resultLabel, 1, 4);
            preview.Name = "purposePreview";
            preview.Dock = DockStyle.Fill;
            preview.Multiline = true;
            preview.ReadOnly = true;
            preview.MaxLength = int.MaxValue;
            preview.Font = new Font("Consolas", 10.5F);
            preview.BorderStyle = BorderStyle.None;
            preview.HideSelection = false;
            WorkspaceEditor surface = new WorkspaceEditor();
            surface.Controls.Add(preview);
            layout.Controls.Add(surface, 1, 5);
            status.Name = "purposeStatus";
            status.Dock = DockStyle.Fill;
            status.Padding = new Padding(0, 5, 0, 0);
            status.Tag = "secondary";
            layout.Controls.Add(status, 1, 6);

            categories.SelectedIndexChanged += delegate { SelectCategory(); };
            commands.SelectedIndexChanged += delegate
            {
                if (!building)
                {
                    command = commands.SelectedItem as PurposeCommand;
                    ResetValues();
                }
            };
        }

        private static void AddButton(FlowLayoutPanel panel, WorkspaceButton button, string text, string name, Action action)
        {
            button.Text = text;
            button.Name = name;
            button.AutoSize = true;
            button.MinimumSize = new Size(70, 27);
            button.Margin = new Padding(0, 2, 8, 2);
            button.Click += delegate { action(); };
            panel.Controls.Add(button);
        }

        private void DrawCategory(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (Brush fill = new SolidBrush(selected ? WorkspaceStyle.Selection : WorkspaceStyle.Surface))
            {
                e.Graphics.FillRectangle(fill, e.Bounds);
            }
            Rectangle bounds = e.Bounds;
            bounds.Inflate(-10, 0);
            TextRenderer.DrawText(e.Graphics, categories.Items[e.Index].ToString(), Font, bounds,
                selected && WorkspaceStyle.IsDark ? Color.White : WorkspaceStyle.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        public void SetProtocol(ProtocolKind protocol)
        {
            building = true;
            available = PurposeComposer.GetCommands(protocol);
            categories.Items.Clear();
            categories.Items.AddRange(available.Select(item => item.Category).Distinct().Cast<object>().ToArray());
            building = false;
            categories.SelectedIndex = 0;
        }

        private void SelectCategory()
        {
            if (building || categories.SelectedItem == null)
            {
                return;
            }
            building = true;
            commands.Items.Clear();
            commands.Items.AddRange(available.Where(item => item.Category == categories.SelectedItem.ToString()).Cast<object>().ToArray());
            building = false;
            commands.SelectedIndex = 0;
        }

        private void ResetValues()
        {
            if (command == null)
            {
                return;
            }
            values = new Dictionary<string, string>(PurposeComposer.Defaults(command));
            note.Text = command.Note;
            scroll.AutoScrollPosition = Point.Empty;
            RebuildFields();
        }

        private void ReadEditors()
        {
            foreach (KeyValuePair<string, Control> entry in editors)
            {
                NumericUpDown number = entry.Value as NumericUpDown;
                ComboBox choice = entry.Value as ComboBox;
                DateTimePicker time = entry.Value as DateTimePicker;
                DataGridView grid = entry.Value as DataGridView;
                if (number != null)
                {
                    values[entry.Key] = number.Value.ToString(CultureInfo.InvariantCulture);
                }
                else if (choice != null)
                {
                    values[entry.Key] = ((ChoiceItem)choice.SelectedItem).Value;
                }
                else if (time != null)
                {
                    values[entry.Key] = time.Value.ToString((bool)time.Tag ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:00", CultureInfo.InvariantCulture);
                }
                else if (grid != null)
                {
                    grid.EndEdit();
                    values[entry.Key] = string.Join("\n", grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow)
                        .Select(row => Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture) + ":" + Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture)));
                }
                else
                {
                    values[entry.Key] = entry.Value.Text;
                }
            }
        }

        private void RebuildFields()
        {
            Point position = new Point(-scroll.AutoScrollPosition.X, -scroll.AutoScrollPosition.Y);
            building = true;
            fields.SuspendLayout();
            foreach (Control child in fields.Controls.Cast<Control>().ToArray())
            {
                child.Dispose();
            }
            fields.Controls.Clear();
            fields.RowStyles.Clear();
            fields.RowCount = 0;
            editors.Clear();
            foreach (PurposeField field in PurposeComposer.GetFields(command, values))
            {
                if (!values.ContainsKey(field.Key))
                {
                    values[field.Key] = field.Default;
                }
                int row = fields.RowCount++;
                int height = field.Editor == PurposeEditor.Lines ? 104 : field.Editor == PurposeEditor.Identifiers ? 98 : field.Editor == PurposeEditor.TypeGroups ? 136 : 42;
                fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                fields.Controls.Add(new Label { Text = field.Label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
                Label help = new Label { Text = field.Help, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Tag = "secondary", Padding = new Padding(8, 0, 0, 0) };
                fields.Controls.Add(help, 2, row);
                Control editor = CreateEditor(field, help);
                editor.Dock = DockStyle.Fill;
                editor.Margin = new Padding(3, 6, 3, 6);
                fields.Controls.Add(editor, 1, row);
            }
            fields.ResumeLayout(true);
            building = false;
            scroll.AutoScrollPosition = position;
            generate.Text = command.ParameterOnly ? "生成参数明文" : "生成报文";
            copy.Text = command.ParameterOnly ? "复制参数明文" : "复制报文";
            resultLabel.Text = command.ParameterOnly ? "参数明文 HEX（需安全封装）" : "完整报文 HEX";
            InvalidateResult();
            WorkspaceStyle.Apply(this);
        }

        private Control CreateEditor(PurposeField field, Label help)
        {
            Control editor;
            string value = values[field.Key];
            if (field.Editor == PurposeEditor.Number)
            {
                NumericUpDown number = new NumericUpDown { Minimum = field.Minimum, Maximum = field.Maximum, Value = decimal.Parse(value, CultureInfo.InvariantCulture) };
                number.ValueChanged += delegate { InvalidateResult(); };
                editor = number;
            }
            else if (field.Editor == PurposeEditor.Choice)
            {
                ComboBox choice = new ComboBox();
                MainForm.ConfigurePicker(choice);
                foreach (string option in field.Choices)
                {
                    string[] parts = option.Split('|');
                    choice.Items.Add(new ChoiceItem { Value = parts[0], Name = parts[1] });
                }
                choice.SelectedItem = choice.Items.Cast<ChoiceItem>().First(item => item.Value == value);
                choice.SelectedIndexChanged += delegate
                {
                    if (building)
                    {
                        return;
                    }
                    ReadEditors();
                    if (field.Key == "parameter")
                    {
                        values.Remove("value");
                    }
                    if (new[] { "mode", "parameter", "combined", "allNodes", "rangeMode", "density" }.Contains(field.Key))
                    {
                        RebuildFields();
                    }
                    else
                    {
                        InvalidateResult();
                    }
                };
                editor = choice;
            }
            else if (field.Editor == PurposeEditor.DateTime)
            {
                DateTimePicker time = new DateTimePicker
                {
                    Format = DateTimePickerFormat.Custom,
                    CustomFormat = command.Key == "645.date" ? "yyyy-MM-dd dddd" : command.Key == "645.time" ? "HH:mm:ss"
                        : field.IncludeSeconds ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm",
                    ShowUpDown = command.Key == "645.time",
                    Tag = field.IncludeSeconds,
                    Value = DateTime.ParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                };
                if (field.Key != "from" && field.Key != "to")
                {
                    time.MinDate = new DateTime(2000, 1, 1);
                    time.MaxDate = new DateTime(2099, 12, 31, 23, 59, 59);
                }
                else
                {
                    time.MinDate = new DateTime(1970, 1, 1);
                    time.MaxDate = new DateTime(2099, 12, 31, 23, 59, 59);
                }
                time.ValueChanged += delegate { InvalidateResult(); };
                editor = time;
            }
            else if (field.Editor == PurposeEditor.TypeGroups)
            {
                DataGridView grid = new DataGridView();
                WorkspaceStyle.ConfigureGrid(grid);
                grid.ReadOnly = false;
                grid.AllowUserToAddRows = true;
                grid.AllowUserToDeleteRows = true;
                grid.RowHeadersVisible = true;
                grid.RowHeadersWidth = 24;
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "电表类型", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "用户类型", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
                foreach (string line in value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    grid.Rows.Add(line.Split(new[] { ':' }, 2).Cast<object>().ToArray());
                }
                grid.CellValueChanged += delegate { InvalidateResult(); };
                grid.RowsRemoved += delegate { InvalidateResult(); };
                editor = grid;
            }
            else
            {
                WorkspaceTextBox text = new WorkspaceTextBox
                {
                    Text = value, BorderStyle = BorderStyle.FixedSingle, MaxLength = int.MaxValue,
                    Multiline = field.Editor == PurposeEditor.Lines || field.Editor == PurposeEditor.Identifiers,
                    Font = new Font("Consolas", 10F), HideSelection = false
                };
                text.UseSystemPasswordChar = field.Key == "password";
                text.TextChanged += delegate { InvalidateResult(); };
                editor = text;
                if (field.Editor == PurposeEditor.Identifiers)
                {
                    TableLayoutPanel picker = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
                    picker.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                    picker.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                    text.Dock = DockStyle.Fill;
                    picker.Controls.Add(text, 0, 0);
                    WorkspaceButton select = new WorkspaceButton { Text = "按名称选择数据项…", Dock = DockStyle.Fill, Margin = new Padding(3, 2, 3, 0) };
                    select.Name = "purposeSelectIdentifiers";
                    select.Click += delegate
                    {
                        ReadEditors();
                        using (IdentifierPicker dialog = new IdentifierPicker(command, values, text.Text))
                        {
                            if (dialog.ShowDialog(this) == DialogResult.OK)
                            {
                                text.Text = dialog.Identifiers;
                            }
                        }
                    };
                    picker.Controls.Add(select, 0, 1);
                    text.TextChanged += delegate { DescribeIdentifiers(text.Text, help); };
                    DescribeIdentifiers(text.Text, help);
                    editors[field.Key] = text;
                    text.Name = "purpose_" + field.Key;
                    return picker;
                }
            }
            editor.Name = "purpose_" + field.Key;
            editor.AccessibleName = field.Label;
            editors[field.Key] = editor;
            return editor;
        }

        private void DescribeIdentifiers(string text, Label help)
        {
            try
            {
                string[] ids = PurposeComposer.Identifiers(text);
                help.Text = string.Join("\n", ids.Take(3).Select(id => command.Protocol == ProtocolKind.Dlt645
                    ? BusinessDecoder.DescribeMeterDi(id, false)
                    : id + " " + string.Join(" / ", PurposeComposer.IdentifierAfns(command, values)
                        .Select(afn => ProtocolCatalog.Name(command.Protocol, afn, false, id)).Where(name => name != null).Distinct())))
                    + (ids.Length > 3 ? "\n共" + ids.Length + "项" : "");
            }
            catch (FormatException)
            {
                help.Text = "填写8位DI，多个用逗号分隔；也可按名称选择。";
            }
        }

        private void InvalidateResult()
        {
            if (building)
            {
                return;
            }
            result = null;
            preview.Clear();
            copy.Enabled = false;
            toParser.Enabled = false;
            status.Text = "填写参数后生成；生成结果可复制，完整帧可发送到解析页。";
            status.Tag = "secondary";
            status.ForeColor = WorkspaceStyle.Secondary;
        }

        public void Generate()
        {
            try
            {
                ReadEditors();
                result = PurposeComposer.Compose(command, values);
                preview.Text = Hex.Format(result.Bytes);
                copy.Enabled = true;
                toParser.Enabled = result.IsFrame;
                status.Text = result.Bytes.Length + "字节；" + result.Summary;
                status.Tag = result.IsFrame ? "success" : "warning";
                status.ForeColor = result.IsFrame ? WorkspaceStyle.Success : WorkspaceStyle.Warning;
            }
            catch (FormatException error)
            {
                InvalidateResult();
                status.Text = error.Message;
                status.Tag = "error";
                status.ForeColor = WorkspaceStyle.Error;
            }
        }

        private void CopyResult()
        {
            try
            {
                Clipboard.SetText(preview.Text);
            }
            catch (ExternalException)
            {
                status.Text = "剪贴板正被其他程序占用，请重试复制。";
                status.Tag = "error";
                status.ForeColor = WorkspaceStyle.Error;
            }
        }
    }

    internal sealed class IdentifierPicker : Form
    {
        private readonly TextBox filter = new TextBox();
        private readonly CheckedListBox list = new CheckedListBox();
        private readonly Label selection = new Label();
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly PurposeCommand command;
        private readonly IDictionary<string, string> values;
        private bool loading;

        public string Identifiers { get; private set; }

        public IdentifierPicker(PurposeCommand command, IDictionary<string, string> values, string current)
        {
            this.command = command;
            this.values = values;
            Text = "按数据标识或名称选择";
            Font = new Font("Microsoft YaHei UI", 9F);
            Size = new Size(800, 560);
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            foreach (string id in current.Split(new[] { ',', '，', ' ', '\t', '\r', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (id.Length == 8 && id.All(Uri.IsHexDigit))
                {
                    selected.Add(id.ToUpperInvariant());
                }
            }
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Controls.Add(layout);
            filter.Dock = DockStyle.Fill;
            filter.Name = "identifierFilter";
            layout.Controls.Add(filter, 0, 0);
            layout.Controls.Add(new Label { Text = "输入DI或名称筛选，最多显示200项；勾选结果在切换筛选时保留。", Dock = DockStyle.Fill, Tag = "secondary" }, 0, 1);
            list.Name = "identifierChoices";
            list.Dock = DockStyle.Fill;
            list.IntegralHeight = false;
            list.CheckOnClick = true;
            list.HorizontalScrollbar = true;
            layout.Controls.Add(list, 0, 2);
            selection.Dock = DockStyle.Fill;
            selection.Name = "identifierSelection";
            selection.AutoEllipsis = true;
            selection.Padding = new Padding(0, 4, 0, 0);
            layout.Controls.Add(selection, 0, 3);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            WorkspaceButton accept = new WorkspaceButton { Text = "使用所选项", AutoSize = true, Primary = true };
            accept.Click += delegate
            {
                if (selected.Count > 0)
                {
                    Identifiers = string.Join(", ", selected.OrderBy(id => id, StringComparer.Ordinal));
                    DialogResult = DialogResult.OK;
                }
            };
            WorkspaceButton cancel = new WorkspaceButton { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            WorkspaceButton clear = new WorkspaceButton { Text = "清空选择", AutoSize = true };
            clear.Click += delegate { selected.Clear(); RefreshItems(); };
            actions.Controls.Add(accept);
            actions.Controls.Add(cancel);
            actions.Controls.Add(clear);
            layout.Controls.Add(actions, 0, 4);
            AcceptButton = accept;
            CancelButton = cancel;
            filter.TextChanged += delegate { RefreshItems(); };
            list.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (loading)
                {
                    return;
                }
                ProtocolDataItem item = (ProtocolDataItem)list.Items[e.Index];
                if (e.NewValue == CheckState.Checked)
                {
                    if (command.Protocol == ProtocolKind.Dlt645)
                    {
                        selected.Clear();
                        loading = true;
                        for (int index = 0; index < list.Items.Count; index++)
                        {
                            if (index != e.Index)
                            {
                                list.SetItemChecked(index, false);
                            }
                        }
                        loading = false;
                    }
                    selected.Add(item.Id);
                }
                else
                {
                    selected.Remove(item.Id);
                }
                UpdateSelection();
            };
            RefreshItems();
            WorkspaceStyle.Apply(this);
        }

        private void RefreshItems()
        {
            loading = true;
            list.BeginUpdate();
            list.Items.Clear();
            ProtocolDataItem[] items = ProtocolCatalog.Search(command.Protocol, PurposeComposer.IdentifierAfns(command, values), filter.Text);
            foreach (ProtocolDataItem item in items)
            {
                list.Items.Add(item, selected.Contains(item.Id));
            }
            list.EndUpdate();
            loading = false;
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            selection.Text = "已选" + selected.Count + "项：" + string.Join(", ", selected.OrderBy(id => id, StringComparer.Ordinal));
        }
    }
}
