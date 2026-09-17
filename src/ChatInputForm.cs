using System;
using System.Drawing;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopPet.Chat;

namespace DesktopPet
{
    // Modeless composer and local transcript; request history stays in ChatService.
    internal sealed class ChatInputForm : Form
    {
        internal readonly TextBox Input = new TextBox();
        internal readonly Button Send = new PetButton();
        private readonly Button cancel = new PetButton();
        private readonly Label status = new Label();
        private readonly Func<string, Task<bool>> submit;
        private readonly ChatArchive archive;
        private int visibleTurns = 80;
        private string pendingInput;
        internal readonly RichTextBox Transcript = new RichTextBox();
        internal readonly ComboBox ContextTurns = new ComboBox();
        private readonly Button older = new PetButton();
        internal ChatInputForm(Func<string, Task<bool>> submit, Action cancelRequest, ChatArchive archive = null, int contextTurns = 2, Action<int> changeContext = null)
        {
            this.submit = submit; this.archive = archive;
            Text = "和桌宠聊聊"; FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual; TopMost = true; ShowInTaskbar = false;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(550, 700); MinimumSize = new Size(500, 570); UiTheme.Window(this);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 10, 20, 14), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); Controls.Add(root);
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            var headingText = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown }; headingText.Controls.Add(UiTheme.Label(submit == null ? "陪伴的足迹" : "和桌宠聊聊", 16, true)); var hint = UiTheme.Label("双方发言 · 保存在本机", 9, false); hint.ForeColor = UiTheme.Muted; headingText.Controls.Add(hint); heading.Controls.Add(headingText, 0, 0);
            older.Text = "更早记录"; older.Dock = DockStyle.Fill; older.Margin = new Padding(0, 12, 0, 14); older.Click += delegate { visibleTurns += 80; RefreshTranscript(false); }; heading.Controls.Add(older, 1, 0); root.Controls.Add(heading, 0, 0);
            var transcriptCard = new PetCard { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Padding = new Padding(16) }; transcriptCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); transcriptCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Transcript.Dock = DockStyle.Fill; Transcript.ReadOnly = true; Transcript.DetectUrls = false; Transcript.BackColor = Color.White; Transcript.ScrollBars = RichTextBoxScrollBars.Vertical; transcriptCard.Controls.Add(Transcript); root.Controls.Add(transcriptCard, 0, 1);
            var memory = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            memory.Controls.Add(new Label { Text = "发送时带入上下文", AutoSize = true, Margin = new Padding(0, 9, 5, 0) });
            ContextTurns.DropDownStyle = ComboBoxStyle.DropDownList; ContextTurns.Width = 120;
            ContextTurns.Items.AddRange(new object[] { "不带历史", "最近 2 轮", "最近 4 轮", "最近 6 轮" });
            ContextTurns.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { 0, 2, 4, 6 }, contextTurns));
            ContextTurns.SelectedIndexChanged += delegate { if (changeContext != null) changeContext(new[] { 0, 2, 4, 6 }[ContextTurns.SelectedIndex]); };
            memory.Controls.Add(ContextTurns); root.Controls.Add(memory, 0, 2);
            Input.Dock = DockStyle.Fill; Input.Multiline = true; Input.ScrollBars = ScrollBars.Vertical; Input.MaxLength = ChatService.MaxInputCharacters; root.Controls.Add(Input, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Send.Text = "发送"; Send.Size = new Size(92, 38); cancel.Text = "取消请求"; cancel.Size = new Size(100, 38); cancel.Enabled = false;
            actions.Controls.Add(Send); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 4);
            status.Text = "Enter 发送 · Shift+Enter 换行"; status.Dock = DockStyle.Fill; status.Font = new Font(Font.FontFamily, 8.5f); root.Controls.Add(status, 0, 5);
            if (submit == null) { Input.Visible = false; memory.Visible = false; actions.Visible = false; root.RowStyles[2].Height = 0; root.RowStyles[3].Height = 0; root.RowStyles[4].Height = 0; status.Text = "历史记录仅供查看，不会发送给模型。"; }
            RefreshTranscript(true);
            Send.Click += async delegate { await SubmitAsync(); };
            cancel.Click += delegate { cancelRequest(); };
            Input.KeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    if (Send.Enabled) await SubmitAsync();
                }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
            };
            Shown += delegate { Input.Focus(); };
            UiTheme.Apply(this); UiTheme.Primary(Send); Transcript.BorderStyle = BorderStyle.None;
        }
        internal async Task SubmitAsync()
        {
            if (!Send.Enabled || submit == null) return;
            string message = Input.Text.Trim();
            if (message.Length == 0) { status.Text = "先输入一点文字吧"; Input.Focus(); return; }
            bool success = await submit(message);
            if (IsDisposed) return;
            if (success) Input.Clear();
            else status.Text = "未发送成功，可修改后重试";
            Input.Focus();
        }
        internal void SetPending(string text) { pendingInput = text; RefreshTranscript(true); }
        internal void Notify(string text) { if (!IsDisposed) status.Text = text; }
        internal void RefreshTranscript(bool scrollToEnd)
        {
            if (IsDisposed) return;
            Transcript.Clear(); var turns = archive == null ? new List<ArchivedTurn>() : archive.Read(visibleTurns);
            older.Enabled = turns.Count >= visibleTurns;
            string previousPersona = null;
            foreach (var turn in turns)
            {
                if (previousPersona != null && previousPersona != turn.persona) AppendText("—— 人设已更新，新的对话段落 ——\n", Color.Gray);
                AppendText("我 · " + turn.time + "\n", UiTheme.Accent); AppendText(turn.user + "\n\n", UiTheme.Ink);
                AppendText((turn.speaker ?? "桌宠") + (turn.status == "success" ? "" : " · " + turn.status) + "\n", Color.FromArgb(42, 110, 86));
                AppendText(turn.assistant + "\n\n", UiTheme.Ink); previousPersona = turn.persona;
            }
            if (!String.IsNullOrEmpty(pendingInput)) { AppendText("我 · 本次发送\n" + pendingInput + "\n\n桌宠正在回复…\n", UiTheme.Muted); }
            if (turns.Count == 0 && String.IsNullOrEmpty(pendingInput)) AppendText("还没有聊天记录。双方的发言会显示在这里。", Color.Gray);
            if (scrollToEnd) { Transcript.SelectionStart = Transcript.TextLength; Transcript.ScrollToCaret(); }
            else { Transcript.SelectionStart = 0; Transcript.ScrollToCaret(); }
            if (archive != null && archive.Error != null) status.Text = archive.Error;
        }
        private void AppendText(string text, Color color) { Transcript.SelectionColor = color; Transcript.AppendText(text); }
        internal void SetBusy(bool busy)
        {
            if (IsDisposed) return;
            Send.Enabled = !busy; Input.ReadOnly = busy; cancel.Enabled = busy;
            status.Text = busy ? "正在等待 AI 回复…" : "Enter 发送 · Shift+Enter 换行";
        }
    }
}
