using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopPet.Chat;
using DesktopPet.Characters;

namespace DesktopPet
{
    internal sealed class SkinPreview : Control
    {
        internal Image Skin;
        internal bool Showroom;
        internal SkinPreview() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.FromArgb(247, 252, 255));
            if (Showroom)
            {
                using (var gradient = new LinearGradientBrush(ClientRectangle, Color.FromArgb(234, 246, 252), Color.FromArgb(248, 253, 255), 90f)) e.Graphics.FillRectangle(gradient, ClientRectangle);
                using (var cloud = new SolidBrush(Color.White)) { e.Graphics.FillEllipse(cloud, Width - 120, 23, 82, 24); e.Graphics.FillEllipse(cloud, Width - 100, 6, 43, 44); }
                using (var floor = new SolidBrush(Color.FromArgb(215, 236, 247))) e.Graphics.FillEllipse(floor, Width * .18f, Height - 41, Width * .64f, 24);
                UiTheme.Paw(e.Graphics, new RectangleF(20, 24, 29, 30), Color.FromArgb(198, 230, 244));
            }
            else using (Brush tile = new SolidBrush(Color.FromArgb(229, 240, 247)))
                for (int y = 0; y < Height; y += 16) for (int x = 0; x < Width; x += 16)
                    if ((x / 16 + y / 16) % 2 == 0) e.Graphics.FillRectangle(tile, x, y, 16, 16);
            if (Skin == null)
            {
                UiTheme.Paw(e.Graphics, new RectangleF(Width / 2 - 30, Height / 2 - 50, 60, 62), Color.FromArgb(155, 204, 226));
                TextRenderer.DrawText(e.Graphics, Showroom ? "选择一个角色，开始陪伴" : "上传一张喜欢的角色图片", Font, new Rectangle(10, Height / 2 + 27, Width - 20, 30), UiTheme.Muted, TextFormatFlags.HorizontalCenter); return;
            }
            float scale = Math.Min((Width - 40f) / Skin.Width, (Height - 40f) / Skin.Height);
            if (scale <= 0) return;
            int w = Math.Max(1, (int)(Skin.Width * scale)), h = Math.Max(1, (int)(Skin.Height * scale));
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(Skin, new Rectangle((Width - w) / 2, (Height - h) / 2, w, h));
        }
    }

    internal sealed class LauncherForm : Form
    {
        private readonly string folder;
        internal readonly ListBox Skins = new ListBox();
        internal readonly Button LaunchButton = new PetButton(), EditButton = new PetButton();
        internal CharacterEditorPage EditorPage { get; private set; }
        private Control homePage;
        private PageTransition pageTransition;
        internal bool IsPageTransitionRunning { get { return pageTransition != null && pageTransition.IsRunning; } }
        private readonly bool automaticPhrases;
        internal readonly Label PhraseStatus = new Label();
        internal readonly Button GeneratePhrases = new PetButton();
        private readonly Dictionary<string, CancellationTokenSource> phraseJobs = new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> phraseJobVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> phraseStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private ChatInputForm historyWindow;
        private readonly Button stop = new PetButton(), import = new PetButton();
        private readonly SkinPreview preview = new SkinPreview();
        private readonly Label status = new Label(), apiStatus = new Label(), skinTitle = new Label(), previewHint = new Label();
        private CharacterPack selected;
        private CharacterEntry selectedEntry;
        internal PetWindow RunningPet { get; private set; }
        private Atlas runningAtlas;
        private bool loading, closing;
        internal LauncherForm(string folder, string selection = null, bool automaticPhrases = true)
        {
            this.folder = folder; this.automaticPhrases = automaticPhrases;
            Text = "桌宠工坊 · 启动器"; Font = new Font("Microsoft YaHei UI", 10f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1120, 760); MinimumSize = new Size(1010, 720);
            StartPosition = FormStartPosition.CenterScreen; UiTheme.Window(this);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 16), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); Controls.Add(root); homePage = root;
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
            brand.Controls.Add(UiTheme.Label("桌宠工坊", 22, true)); var subtitle = UiTheme.Label("让桌面，有更多陪伴", 10, false); subtitle.ForeColor = UiTheme.Muted; brand.Controls.Add(subtitle);
            heading.Controls.Add(brand, 0, 0); heading.Controls.Add(new Label { Text = "小小的陪伴，点亮每一天  ♡", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.Muted }, 1, 0); root.Controls.Add(heading, 0, 0);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 216)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.Controls.Add(body, 0, 1);
            var library = new PetCard { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 0, 18, 0) };
            library.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); library.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); library.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); library.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            library.Controls.Add(UiTheme.Label("我的角色", 13, true), 0, 0);
            Skins.Dock = DockStyle.Fill; Skins.BorderStyle = BorderStyle.None; Skins.IntegralHeight = false; Skins.ItemHeight = 52; Skins.DrawMode = DrawMode.OwnerDrawFixed;
            Skins.DrawItem += DrawSkin; Skins.SelectedIndexChanged += delegate { ChangeSelection(); }; library.Controls.Add(Skins, 0, 1);
            library.Controls.Add(new Label { Text = "独一份模样，\n独一份陪伴。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted }, 0, 2);
            import.Text = "+ 新建角色"; import.Name = "createCharacter"; import.Dock = DockStyle.Fill; import.Click += delegate { OpenEditor(false); }; library.Controls.Add(import, 0, 3); body.Controls.Add(library, 0, 0);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 144)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.Controls.Add(content, 1, 0);
            content.Controls.Add(new WelcomeBanner { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 18), Font = Font }, 0, 0);
            body.SizeChanged += delegate { content.RowStyles[0].Height = body.ClientSize.Height < 555 ? 88 : 144; };
            var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285)); content.Controls.Add(workspace, 0, 1);
            var detail = new PetCard { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 0, 16, 0) };
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 53)); detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            skinTitle.Text = "角色预览"; skinTitle.Font = new Font(Font.FontFamily, 14, FontStyle.Bold); skinTitle.Dock = DockStyle.Fill; skinTitle.AutoEllipsis = true;
            preview.Dock = DockStyle.Fill; preview.Margin = new Padding(0); preview.Showroom = true;
            previewHint.Dock = DockStyle.Fill; previewHint.ForeColor = UiTheme.Muted; previewHint.TextAlign = ContentAlignment.MiddleLeft; previewHint.Font = new Font(Font.FontFamily, 9);
            EditButton.Text = "编辑角色  /  图片 · 人设 · 台词"; EditButton.Name = "editCharacter"; EditButton.Dock = DockStyle.Fill; EditButton.Margin = new Padding(0); EditButton.Click += delegate { OpenEditor(true); };
            detail.Controls.Add(skinTitle, 0, 0); detail.Controls.Add(preview, 0, 1); detail.Controls.Add(previewHint, 0, 2); detail.Controls.Add(EditButton, 0, 3); workspace.Controls.Add(detail, 0, 0);
            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            tools.RowStyles.Add(new RowStyle(SizeType.Percent, 43)); tools.RowStyles.Add(new RowStyle(SizeType.Percent, 57)); workspace.Controls.Add(tools, 1, 0);
            var api = new PetCard { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 0, 14) };
            api.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); api.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); api.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); api.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            api.Controls.Add(UiTheme.Label("AI 连接", 12, true), 0, 0); apiStatus.Dock = DockStyle.Fill; apiStatus.ForeColor = UiTheme.Muted; apiStatus.AutoEllipsis = true;
            var settings = new PetButton { Text = "设置 API / 模型", Dock = DockStyle.Fill, Margin = new Padding(0) };
            settings.Click += delegate
            {
                using (var dialog = new ModelSettingsForm(folder, delegate { RefreshApiStatus(); TriggerPhrases(selected == null ? null : selected.Folder); }))
                { dialog.TopMost = false; dialog.StartPosition = FormStartPosition.CenterParent; dialog.ShowDialog(this); }
            };
            api.Controls.Add(apiStatus, 0, 1); api.Controls.Add(settings, 0, 2); tools.Controls.Add(api, 0, 0);
            var daily = new PetCard { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            daily.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); daily.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); daily.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); daily.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); daily.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            daily.Controls.Add(UiTheme.Label("日常陪伴", 12, true), 0, 0); PhraseStatus.Dock = DockStyle.Fill; PhraseStatus.ForeColor = UiTheme.Muted; PhraseStatus.AutoEllipsis = true; daily.Controls.Add(PhraseStatus, 0, 1);
            GeneratePhrases.Text = "重新生成短句"; GeneratePhrases.Dock = DockStyle.Fill; GeneratePhrases.Margin = new Padding(0, 0, 0, 5);
            GeneratePhrases.Click += async delegate { if (selected != null) await EnsurePhrasesAsync(selected.Folder, true); };
            var history = new PetButton { Text = "查看聊天记录", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 0) };
            history.Click += delegate
            {
                if (selected == null) return;
                if (historyWindow != null) historyWindow.Dispose();
                historyWindow = new ChatInputForm(null, delegate { }, new ChatArchive(folder, selected.Folder));
                historyWindow.Text = selected.Definition.display_name + " · 聊天记录"; historyWindow.StartPosition = FormStartPosition.CenterParent; historyWindow.Show(this);
            };
            daily.Controls.Add(GeneratePhrases, 0, 2); daily.Controls.Add(history, 0, 3); tools.Controls.Add(daily, 0, 1);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 16, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.Font = new Font(Font.FontFamily, 9);
            stop.Text = "收回桌宠"; stop.Dock = DockStyle.Fill; stop.Margin = new Padding(0, 3, 12, 3); stop.Enabled = false; stop.Click += delegate { StopPet(); SetStatus("桌宠已收回，可以继续选择皮肤。", false); };
            LaunchButton.Text = "启动桌宠  →"; LaunchButton.Dock = DockStyle.Fill; LaunchButton.Margin = new Padding(0, 3, 0, 3); LaunchButton.Font = new Font(Font.FontFamily, 12, FontStyle.Bold); UiTheme.Primary(LaunchButton);
            LaunchButton.Click += delegate { StartSelectedPet(); };
            footer.Controls.Add(status, 0, 0); footer.Controls.Add(stop, 1, 0); footer.Controls.Add(LaunchButton, 2, 0); root.Controls.Add(footer, 0, 2); UiTheme.Apply(this);
            RefreshApiStatus();
            string selectionWarning = null;
            if (selection == null) try { selection = CharacterLoader.ResolveSelection(folder, new string[0]); } catch (Exception error) { selectionWarning = error.Message; }
            RefreshLibrary(selection);
            if (selectionWarning != null) SetStatus("默认选择读取失败，请重新选择皮肤。" + selectionWarning, true);
        }

        private void DrawSkin(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool active = (e.State & DrawItemState.Selected) != 0;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillRectangle(Brushes.White, e.Bounds);
            using (var path = UiTheme.Round(Rectangle.Inflate(e.Bounds, -1, -3), 10)) using (Brush brush = new SolidBrush(active ? UiTheme.Soft : Color.White)) e.Graphics.FillPath(brush, path);
            UiTheme.Paw(e.Graphics, new RectangleF(e.Bounds.X + 10, e.Bounds.Y + 14, 23, 24), active ? UiTheme.Accent : UiTheme.Muted);
            TextRenderer.DrawText(e.Graphics, Skins.Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 43, e.Bounds.Y, e.Bounds.Width - 48, e.Bounds.Height), active ? UiTheme.Accent : UiTheme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }
        private void RefreshApiStatus()
        {
            try { ChatConfig config = ChatConfig.Load(folder); apiStatus.Text = "已配置模型\n" + config.Model; }
            catch { apiStatus.Text = "尚未完成配置\n也可以先启动桌宠体验。"; }
        }
        private void SetStatus(string text, bool error) { status.Text = text; status.ForeColor = error ? Color.Firebrick : UiTheme.Muted; }
        internal void RefreshLibrary(string selection)
        {
            List<string> errors;
            var entries = CharacterLibrary.List(folder, selection, out errors);
            loading = true;
            try { Skins.Items.Clear(); foreach (var entry in entries) Skins.Items.Add(entry); }
            finally { loading = false; }
            CharacterEntry target = entries.Find(p => String.Equals(p.Folder, selection, StringComparison.OrdinalIgnoreCase));
            if (entries.Count > 0) Skins.SelectedItem = target ?? entries[0];
            else { ClearSelection(); SetStatus("还没有可用角色，点击“新建角色”添加一个。", false); }
            if (errors.Count > 0) SetStatus("部分角色无法加载：" + String.Join("；", errors.ToArray()), true);
        }
        private void ClearSelection()
        {
            preview.Skin = null; preview.Invalidate(); if (selected != null) { selected.Dispose(); selected = null; }
            selectedEntry = null; EditButton.Enabled = false; LaunchButton.Enabled = false;
        }
        private void ChangeSelection()
        {
            if (loading) return;
            var entry = Skins.SelectedItem as CharacterEntry; if (entry == null) return;
            CharacterPack next;
            try { next = CharacterLoader.Load(entry.Folder); }
            catch (Exception error) { SetStatus(error.Message, true); loading = true; Skins.SelectedItem = selectedEntry; loading = false; return; }
            ClearSelection(); selected = next; selectedEntry = entry; loading = true;
            try
            {
                preview.Skin = selected.Sprites[selected.ResolveSprite(selected.Definition.default_expression)]; preview.Invalidate();
                skinTitle.Text = selected.Definition.display_name;
                previewHint.Text = selected.Sprites.Count == 1 ? "单图角色 · 使用主图呈现互动\n图片与人设一起切换" : "多表情角色 · 保留动作与表情\n图片与人设一起切换";
                EditButton.Enabled = true; LaunchButton.Enabled = true;
                SetStatus(RunningPet == null ? "准备好了。点击“启动桌宠”后，它才会出现在桌面。" : "点击“应用并切换桌宠”，使用所选皮肤和人设。", false);
            }
            finally { loading = false; }
            RefreshPhraseStatus();
        }
        private void RefreshPhraseStatus()
        {
            GeneratePhrases.Enabled = selected != null && !phraseJobs.ContainsKey(selected.Folder);
            if (selected == null) { PhraseStatus.Text = "日常短句 · 请选择角色"; return; }
            string state;
            if (phraseStates.TryGetValue(selected.Folder, out state)) { PhraseStatus.Text = "日常短句 · " + state; return; }
            try { PhraseStatus.Text = AmbientDialogue.Read(selected.Folder, AmbientDialogue.Fingerprint(selected.Folder)) != null ? "日常短句 · 已生成，自定义台词优先" : "日常短句 · 待生成，优先自定义，其次初始设定"; }
            catch { PhraseStatus.Text = "日常短句 · 人设读取失败"; }
        }
        private async void TriggerPhrases(string characterFolder)
        {
            if (automaticPhrases && characterFolder != null) await EnsurePhrasesAsync(characterFolder, false);
        }
        internal async Task EnsurePhrasesAsync(string characterFolder, bool force)
        {
            string fingerprint;
            try { fingerprint = AmbientDialogue.Fingerprint(characterFolder); }
            catch { phraseStates[characterFolder] = "人设读取失败"; RefreshPhraseStatus(); return; }
            CancellationTokenSource previous;
            if (phraseJobs.TryGetValue(characterFolder, out previous))
            {
                if (!force && phraseJobVersions[characterFolder] == fingerprint) return;
                previous.Cancel();
            }
            var cancellation = new CancellationTokenSource(); phraseJobs[characterFolder] = cancellation;
            phraseJobVersions[characterFolder] = fingerprint;
            phraseStates[characterFolder] = "正在生成，完成后自动生效…"; RefreshPhraseStatus();
            try
            {
                await AmbientDialogue.GenerateAsync(folder, characterFolder, force, cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                phraseStates[characterFolder] = "已生成，自定义台词优先，播放不消耗 token";
                if (RunningPet != null && String.Equals(RunningPet.CharacterFolder, characterFolder, StringComparison.OrdinalIgnoreCase)) RunningPet.RefreshPersona();
            }
            catch (OperationCanceledException) { }
            catch (ChatException error)
            {
                if (!cancellation.IsCancellationRequested) phraseStates[characterFolder] = error.Diagnostic.StartsWith("config_") ? "请先配置 API；自定义和初始台词仍可用" : "生成失败，可重试；自定义和已有台词仍可用";
            }
            catch (Exception error) { new ChatLog().Write(error); if (!cancellation.IsCancellationRequested) phraseStates[characterFolder] = "生成失败，可重试；自定义和已有台词仍可用"; }
            finally
            {
                CancellationTokenSource current;
                if (phraseJobs.TryGetValue(characterFolder, out current) && current == cancellation) { phraseJobs.Remove(characterFolder); phraseJobVersions.Remove(characterFolder); }
                cancellation.Dispose(); if (!IsDisposed && !Disposing) RefreshPhraseStatus();
            }
        }
        internal void OpenEditor(bool edit)
        {
            if (EditorPage != null || (edit && selected == null)) return;
            CharacterEditorPage page;
            try { page = new CharacterEditorPage(folder, edit ? selected.Folder : null); }
            catch (Exception error) { SetStatus("无法打开角色编辑页：" + error.Message, true); return; }
            page.Cancelled += BackToHome;
            page.Saved += delegate
            {
                string saved = page.CreatedFolder;
                BackToHome(); RefreshLibrary(saved);
                if (RunningPet != null && String.Equals(RunningPet.CharacterFolder, saved, StringComparison.OrdinalIgnoreCase)) RunningPet.RefreshPersona();
                TriggerPhrases(saved);
                SetStatus(RunningPet == null ? "角色已保存。点击“启动桌宠”让它来到桌面。" : String.Equals(RunningPet.CharacterFolder, saved, StringComparison.OrdinalIgnoreCase) ? "人设和默认台词已更新；图片点击“应用并切换桌宠”更新。" : "已保存所选角色；点击“应用并切换桌宠”后使用这个角色。", false);
            };
            if (pageTransition != null) pageTransition.Dispose();
            Bitmap before = PageTransition.Capture(homePage);
            EditorPage = page; Controls.Add(page); homePage.Hide(); page.BringToFront();
            pageTransition = PageTransition.Begin(this, page, before, 1);
        }
        internal void BackToHome()
        {
            if (EditorPage == null) return;
            if (pageTransition != null) pageTransition.Dispose();
            Bitmap before = PageTransition.Capture(EditorPage);
            CharacterEditorPage page = EditorPage; EditorPage = null; Controls.Remove(page); page.Dispose(); homePage.Show(); homePage.BringToFront();
            pageTransition = PageTransition.Begin(this, homePage, before, -1);
        }
        internal bool StartSelectedPet()
        {
            if (selected == null || EditorPage != null) return false;
            Atlas nextAtlas = null; PetWindow nextPet = null;
            try
            {
                nextAtlas = new Atlas(CharacterLoader.Load(selected.Folder));
                nextPet = new PetWindow(nextAtlas, false, null, folder, ShowLauncher);
                // Validate the new window before replacing the running pet.
                IntPtr handle = nextPet.Handle;
                CharacterLibrary.SaveSelection(folder, selected.Folder);
                StopPet(); runningAtlas = nextAtlas; RunningPet = nextPet;
                nextPet.FormClosed += PetClosed; nextPet.SettingsSaved += delegate { TriggerPhrases(nextPet.CharacterFolder); }; nextPet.Show();
                TriggerPhrases(selected.Folder);
                LaunchButton.Text = "应用并切换桌宠"; stop.Enabled = true; Hide(); return true;
            }
            catch (Exception error)
            {
                if (RunningPet == nextPet) { RunningPet = null; runningAtlas = null; }
                if (nextPet != null) nextPet.Dispose(); if (nextAtlas != null) nextAtlas.Dispose();
                ShowLauncher(); SetStatus("启动失败：" + error.Message, true); return false;
            }
        }
        internal void ShowLauncher()
        {
            if (closing || IsDisposed) return;
            Show(); WindowState = FormWindowState.Normal; Activate(); RefreshApiStatus();
        }
        private void PetClosed(object sender, FormClosedEventArgs e)
        {
            if (sender != RunningPet) return;
            // FormClosed precedes Form.Dispose: release the pet's windows before the atlas.
            PetWindow previous = RunningPet; Atlas atlas = runningAtlas; RunningPet = null; runningAtlas = null;
            previous.Dispose(); if (atlas != null) atlas.Dispose();
            stop.Enabled = false; LaunchButton.Text = "启动桌宠  →"; ShowLauncher();
            if (!closing) SetStatus("桌宠已收回，可以选择另一套皮肤后重新启动。", false);
        }
        internal void StopPet()
        {
            if (RunningPet == null) return;
            PetWindow previous = RunningPet; Atlas atlas = runningAtlas; RunningPet = null; runningAtlas = null;
            previous.FormClosed -= PetClosed; previous.Close(); previous.Dispose(); atlas.Dispose();
            stop.Enabled = false; LaunchButton.Text = "启动桌宠  →";
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_RECALL) { ShowLauncher(); return; }
            base.WndProc(ref m);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true; foreach (var job in phraseJobs.Values) job.Cancel(); StopPet(); base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && pageTransition != null) { pageTransition.Dispose(); pageTransition = null; }
            if (disposing) { closing = true; foreach (var job in phraseJobs.Values) job.Cancel(); if (historyWindow != null) historyWindow.Dispose(); StopPet(); if (EditorPage != null) { EditorPage.Dispose(); EditorPage = null; } ClearSelection(); }
            base.Dispose(disposing);
        }
    }
}
