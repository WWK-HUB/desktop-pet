using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopPet.Chat;

namespace DesktopPet
{
    internal sealed class ModelSettingsForm : Form
    {
        internal readonly TextBox BaseUrl = new TextBox();
        internal readonly TextBox ApiKey = new TextBox();
        internal readonly ComboBox Model = new ComboBox();
        internal readonly Button FetchModels = new PetButton();
        internal bool IsFetchingModels { get; private set; }
        internal readonly NumericUpDown Timeout = new NumericUpDown();
        internal readonly CheckBox ShowKey = new CheckBox();
        internal readonly Button SaveButton = new PetButton();
        internal readonly Label Status = new Label();
        private readonly string folder;
        private readonly Action saved;
        private readonly ModelCatalogService catalog = new ModelCatalogService();
        private CancellationTokenSource catalogCancellation;
        private int connectionVersion;
        internal ModelSettingsForm(string folder, Action saved)
        {
            this.folder = folder; this.saved = saved;
            Text = "设置模型"; FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 505); UiTheme.Window(this);
            AddLabel("连接你的 AI 模型", 24, 18, 472, 30).Font = new Font(Font.FontFamily, 16f, FontStyle.Bold);
            AddLabel("填写地址和密钥，再获取并选择模型。", 24, 56, 472, 24).ForeColor = UiTheme.Muted;
            AddLabel("接口地址（Base URL）", 24, 90, 472, 24);
            BaseUrl.SetBounds(24, 116, 472, 28); BaseUrl.MaxLength = 2048;
            AddLabel("例如 https://api.openai.com/v1", 24, 147, 472, 22).ForeColor = UiTheme.Muted;
            AddLabel("API Key", 24, 179, 300, 24);
            ApiKey.SetBounds(24, 205, 360, 28); ApiKey.UseSystemPasswordChar = true; ApiKey.MaxLength = 4096;
            ShowKey.Text = "显示密钥"; ShowKey.SetBounds(400, 205, 100, 28);
            ShowKey.CheckedChanged += delegate { ApiKey.UseSystemPasswordChar = !ShowKey.Checked; };
            AddLabel("选择模型", 24, 251, 200, 24);
            FetchModels.Text = "获取模型列表"; FetchModels.SetBounds(342, 245, 154, 32);
            Model.SetBounds(24, 283, 472, 28); Model.MaxLength = 256;
            Model.DropDownStyle = ComboBoxStyle.DropDown; Model.MaxDropDownItems = 10; Model.IntegralHeight = false;
            Model.DropDownHeight = 220; Model.AutoCompleteMode = AutoCompleteMode.SuggestAppend; Model.AutoCompleteSource = AutoCompleteSource.ListItems;
            AddLabel("从列表选择，也可以手动输入模型 ID。", 24, 316, 472, 22).ForeColor = UiTheme.Muted;
            AddLabel("等待超时", 24, 352, 90, 25);
            Timeout.SetBounds(119, 349, 78, 28); Timeout.Minimum = 1; Timeout.Maximum = 180; Timeout.Value = 45;
            AddLabel("秒", 205, 352, 40, 25);
            Status.SetBounds(24, 388, 472, 44); Status.ForeColor = UiTheme.Muted;
            Status.Text = "获取后选择一个文字聊天模型，再保存设置。";
            SaveButton.Text = "保存设置"; SaveButton.SetBounds(378, 439, 118, 32);
            var cancel = new PetButton { Text = "取消", DialogResult = DialogResult.Cancel }; cancel.SetBounds(282, 439, 84, 32);
            Controls.AddRange(new Control[] { BaseUrl, ApiKey, ShowKey, Model, FetchModels, Timeout, Status, SaveButton, cancel });
            BaseUrl.TabIndex = 0; ApiKey.TabIndex = 1; ShowKey.TabIndex = 2; FetchModels.TabIndex = 3; Model.TabIndex = 4; Timeout.TabIndex = 5; SaveButton.TabIndex = 6; cancel.TabIndex = 7;
            AcceptButton = SaveButton; CancelButton = cancel;
            cancel.Click += delegate { Close(); };
            SaveButton.Click += delegate { SaveSettings(); };
            FetchModels.Click += async delegate
            {
                if (IsFetchingModels) { if (catalogCancellation != null) catalogCancellation.Cancel(); }
                else await FetchModelsAsync();
            };
            try
            {
                ModelSettings settings = ModelSettingsStore.Read(folder);
                BaseUrl.Text = settings.BaseUrl; ApiKey.Text = settings.ApiKey; Model.Text = settings.Model;
                int seconds; if (Int32.TryParse(settings.TimeoutSeconds, out seconds) && seconds >= 1 && seconds <= 180) Timeout.Value = seconds;
            }
            catch (Exception error)
            {
                new ChatLog().Write(error);
                Status.ForeColor = Color.Firebrick; Status.Text = "原设置无法读取，请重新填写后保存。";
            }
            BaseUrl.TextChanged += delegate { ConnectionChanged(); };
            ApiKey.TextChanged += delegate { ConnectionChanged(); };
            var card = new PetCard { Location = new Point(12, 84), Size = new Size(496, 409), Padding = new Padding(6), ColumnCount = 1, RowCount = 1 };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var fields = new System.Collections.Generic.List<Control>(); foreach (Control field in Controls) if (field.Top >= 90) fields.Add(field);
            var surface = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0) };
            foreach (Control field in fields) { var position = new Point(field.Left - 18, field.Top - 90); Controls.Remove(field); surface.Controls.Add(field); field.Location = position; }
            card.Controls.Add(surface); Controls.Add(card); card.SendToBack();
            UiTheme.Apply(this); UiTheme.Primary(SaveButton);
        }
        private void ConnectionChanged()
        {
            connectionVersion++;
            if (catalogCancellation != null) catalogCancellation.Cancel();
            string text = Model.Text; Model.Items.Clear(); Model.Text = text;
            FetchModels.Text = IsFetchingModels ? "取消获取" : "获取模型列表";
            Status.ForeColor = UiTheme.Muted; Status.Text = "连接信息已更改，请重新获取模型列表。";
        }
        internal async Task FetchModelsAsync()
        {
            if (IsFetchingModels || IsDisposed) return;
            if (String.IsNullOrWhiteSpace(BaseUrl.Text)) { Status.ForeColor = Color.Firebrick; Status.Text = "请先填写接口地址（Base URL）。"; BaseUrl.Focus(); return; }
            var settings = new ModelSettings { BaseUrl = BaseUrl.Text, ApiKey = ApiKey.Text, TimeoutSeconds = Timeout.Value.ToString() };
            int version = connectionVersion;
            var cancellation = new CancellationTokenSource(); catalogCancellation = cancellation;
            IsFetchingModels = true; FetchModels.Text = "取消获取"; SaveButton.Enabled = false;
            Status.ForeColor = UiTheme.Muted; Status.Text = "正在获取模型列表…窗口和桌宠仍可操作。";
            try
            {
                var ids = await catalog.ListAsync(settings, cancellation.Token);
                if (IsDisposed || Disposing || cancellation.IsCancellationRequested || version != connectionVersion) return;
                string current = Model.Text;
                Model.BeginUpdate();
                try { Model.Items.Clear(); foreach (string id in ids) Model.Items.Add(id); Model.Text = current; }
                finally { Model.EndUpdate(); }
                Status.ForeColor = Color.FromArgb(45, 105, 71);
                Status.Text = "已获取 " + ids.Count + " 个模型，请下拉选择支持文字聊天的模型。";
                Model.Focus();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !Disposing && version == connectionVersion) { Status.ForeColor = UiTheme.Muted; Status.Text = "已取消获取；可重新获取或手动填写模型 ID。"; }
            }
            catch (ChatException error)
            {
                if (!IsDisposed && !Disposing && version == connectionVersion) { Status.ForeColor = Color.Firebrick; Status.Text = error.UserMessage; }
            }
            catch (Exception error)
            {
                new ChatLog().Write(error);
                if (!IsDisposed && !Disposing && version == connectionVersion) { Status.ForeColor = Color.Firebrick; Status.Text = "未能加载列表，可重新获取或手动填写模型 ID。"; }
            }
            finally
            {
                IsFetchingModels = false; catalogCancellation = null; cancellation.Dispose();
                if (!IsDisposed && !Disposing) { FetchModels.Text = Model.Items.Count > 0 ? "刷新模型列表" : "获取模型列表"; SaveButton.Enabled = true; }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && catalogCancellation != null) catalogCancellation.Cancel();
            base.Dispose(disposing);
        }
        private Label AddLabel(string text, int x, int y, int width, int height)
        {
            var label = new Label { Text = text }; label.SetBounds(x, y, width, height); Controls.Add(label); return label;
        }
        private void SaveSettings()
        {
            if (String.IsNullOrWhiteSpace(BaseUrl.Text)) { Status.ForeColor = Color.Firebrick; Status.Text = "请填写接口地址（Base URL）。"; BaseUrl.Focus(); return; }
            if (String.IsNullOrWhiteSpace(Model.Text)) { Status.ForeColor = Color.Firebrick; Status.Text = "请先获取列表并选择模型，或手动填写模型 ID。"; Model.Focus(); return; }
            try
            {
                ModelSettingsStore.Save(folder, new ModelSettings { BaseUrl = BaseUrl.Text, ApiKey = ApiKey.Text, Model = Model.Text, TimeoutSeconds = Timeout.Value.ToString() });
            }
            catch (ChatException error)
            {
                Status.ForeColor = Color.Firebrick;
                Status.Text = error.Diagnostic == "config_missing_key" ? "请填写 API Key；本机免密模型服务可以留空。" : error.UserMessage;
                return;
            }
            catch (Exception error)
            {
                new ChatLog().Write(error);
                Status.ForeColor = Color.Firebrick; Status.Text = "未能保存，请确认程序文件夹可写，或稍后重试。"; return;
            }
            saved(); Close();
        }
    }
}
