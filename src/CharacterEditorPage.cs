using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DesktopPet.Characters;
using DesktopPet.Chat;

namespace DesktopPet
{
    internal sealed class CharacterEditorPage : UserControl
    {
        private readonly string folder;
        private CharacterPack original;
        private readonly HashSet<string> changedImages = new HashSet<string>();
        internal event Action Saved;
        internal event Action Cancelled;
        internal readonly Button BackButton = new PetButton();
        internal readonly TabControl Tabs = new PetTabs();
        internal readonly TextBox PersonaName = new TextBox(), SpeechStyle = new TextBox(), BackgroundStory = new TextBox(), Likes = new TextBox(), Dislikes = new TextBox();
        internal readonly TextBox CharacterName = new TextBox(), PersonaText = new TextBox();
        internal readonly Dictionary<string, TextBox> DefaultMessages = new Dictionary<string, TextBox>();
        internal readonly TextBox IdleLines = new TextBox();
        internal readonly ListBox States = new ListBox();
        internal readonly Button CreateButton = new PetButton(), ClearButton = new PetButton();
        internal readonly CheckBox RemoveBackground = new CheckBox();
        internal readonly Dictionary<string, Bitmap> Images = new Dictionary<string, Bitmap>();
        private readonly Dictionary<string, string> paths = new Dictionary<string, string>();
        private readonly Dictionary<string, bool> backgroundOptions = new Dictionary<string, bool>();
        private readonly SkinPreview preview = new SkinPreview();
        private readonly Label imageTitle = new Label(), imageHint = new Label(), status = new Label();
        private readonly Button upload = new PetButton();
        private bool updating;
        private string previousCharacterName = "";
        internal string CreatedFolder { get; private set; }
        private string SelectedState { get { return States.SelectedIndex < 0 ? "normal" : CharacterLibrary.ImageStates[States.SelectedIndex]; } }

        internal CharacterEditorPage(string folder, string characterFolder = null)
        {
            this.folder = folder; Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi; Dock = DockStyle.Fill; BackColor = UiTheme.Background;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); Controls.Add(root);
            var heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label { Text = characterFolder == null ? "新建角色" : "编辑角色", AutoSize = true, Font = new Font(Font.FontFamily, 21f, FontStyle.Bold), Location = new Point(0, 0) });
            heading.Controls.Add(new Label { Text = "主图必填，表情和动作选填。没有上传的状态会自动使用主图。", AutoSize = true, ForeColor = UiTheme.Muted, Location = new Point(2, 40) }); root.Controls.Add(heading, 0, 0);
            var identity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            identity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            identity.Controls.Add(new Label { Text = "角色名称 *", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            CharacterName.MaxLength = 30; CharacterName.Dock = DockStyle.Fill; CharacterName.Margin = new Padding(3, 6, 3, 6); identity.Controls.Add(CharacterName, 1, 0); root.Controls.Add(identity, 0, 1);
            Tabs.Dock = DockStyle.Fill;
            var artworkTab = new TabPage("状态图片") { BackColor = BackColor, Padding = new Padding(8) };
            var personaTab = new TabPage("角色人设") { BackColor = Color.White, AutoScroll = true, Padding = new Padding(16) };
            Tabs.TabPages.Add(artworkTab); Tabs.TabPages.Add(personaTab); root.Controls.Add(Tabs, 0, 2);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 6, 0, 0) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 255)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); artworkTab.Controls.Add(body);
            States.Dock = DockStyle.Fill; States.IntegralHeight = false; States.DrawMode = DrawMode.OwnerDrawFixed; States.ItemHeight = 34;
            States.BorderStyle = BorderStyle.None; States.Margin = new Padding(0, 0, 14, 0);
            foreach (string state in CharacterLibrary.ImageStates) States.Items.Add(state);
            States.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool active = (e.State & DrawItemState.Selected) != 0;
                using (Brush fill = new SolidBrush(active ? UiTheme.Soft : Color.White)) e.Graphics.FillRectangle(fill, e.Bounds);
                string key = CharacterLibrary.ImageStates[e.Index];
                string label = CharacterLibrary.ImageLabels[e.Index] + (Images.ContainsKey(key) ? "  · 已上传" : key == "normal" ? "  * 必填" : "  · 选填");
                TextRenderer.DrawText(e.Graphics, label, Font, new Rectangle(e.Bounds.X + 9, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height), active ? UiTheme.Accent : UiTheme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            States.SelectedIndexChanged += delegate { RefreshSelectedImage(); }; body.Controls.Add(States, 0, 0);
            var artwork = new PetCard { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16) };
            artwork.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); artwork.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            artwork.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); artwork.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); artwork.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); body.Controls.Add(artwork, 1, 0);
            imageTitle.Dock = DockStyle.Fill; imageTitle.Font = new Font(Font, FontStyle.Bold); artwork.Controls.Add(imageTitle, 0, 0);
            preview.Dock = DockStyle.Fill; artwork.Controls.Add(preview, 0, 1);
            imageHint.Dock = DockStyle.Fill; imageHint.ForeColor = UiTheme.Muted; imageHint.TextAlign = ContentAlignment.MiddleLeft; imageHint.AutoEllipsis = true; artwork.Controls.Add(imageHint, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            upload.Text = "上传图片"; upload.Size = new Size(120, 30); upload.Click += delegate { PickImage(); };
            ClearButton.Text = "移除图片"; ClearButton.Size = new Size(105, 30); ClearButton.Click += delegate { ClearImage(SelectedState); };
            actions.Controls.Add(upload); actions.Controls.Add(ClearButton); artwork.Controls.Add(actions, 0, 3);
            RemoveBackground.Text = "去除当前图片的边缘纯色背景"; RemoveBackground.Dock = DockStyle.Fill;
            RemoveBackground.CheckedChanged += delegate
            {
                if (updating) return;
                string path;
                if (paths.TryGetValue(SelectedState, out path)) SetImage(SelectedState, path, RemoveBackground.Checked);
            };
            artwork.Controls.Add(RemoveBackground, 0, 4);
            var persona = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            persona.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); persona.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            var basics = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 22, 0) };
            basics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var preferences = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            preferences.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(basics, "称呼 *（AI 使用的名字）", PersonaName, 64, 29);
            AddField(basics, "性格与角色要求", PersonaText, 2000, 86);
            AddField(basics, "说话方式", SpeechStyle, 2000, 70);
            AddField(basics, "背景故事", BackgroundStory, 4000, 86);
            AddField(preferences, "喜欢的事物（每行一项）", Likes, 3898, 135);
            AddField(preferences, "不喜欢的事物（每行一项）", Dislikes, 3898, 135);
            preferences.Controls.Add(new Label { Text = "每类最多 30 项，每项最多 128 字。", AutoSize = true, ForeColor = UiTheme.Muted });
            persona.Controls.Add(basics, 0, 0); persona.Controls.Add(preferences, 1, 0); personaTab.Controls.Add(persona);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = UiTheme.Muted; footer.Controls.Add(status, 0, 0);
            BackButton.Text = "取消并返回"; BackButton.Dock = DockStyle.Fill; BackButton.Margin = new Padding(4, 12, 4, 10); BackButton.Click += delegate { if (Cancelled != null) Cancelled(); }; footer.Controls.Add(BackButton, 1, 0);
            CreateButton.Text = characterFolder == null ? "创建并返回" : "保存并返回"; CreateButton.Dock = DockStyle.Fill; CreateButton.Margin = new Padding(4, 12, 0, 10);
            CreateButton.FlatStyle = FlatStyle.Flat; CreateButton.FlatAppearance.BorderSize = 0; CreateButton.BackColor = UiTheme.Accent; CreateButton.ForeColor = Color.White;
            CreateButton.Click += delegate { if (TryCreate() && Saved != null) Saved(); }; footer.Controls.Add(CreateButton, 2, 0); root.Controls.Add(footer, 0, 3);
            CharacterName.TextChanged += delegate { if (original == null && (String.IsNullOrWhiteSpace(PersonaName.Text) || PersonaName.Text == previousCharacterName)) PersonaName.Text = CharacterName.Text; previousCharacterName = CharacterName.Text; RefreshValidation(); };
            PersonaName.TextChanged += delegate { RefreshValidation(); };
            if (characterFolder != null)
            {
                original = CharacterLoader.Load(characterFolder);
                string main = original.ResolveSprite(original.Definition.default_expression);
                foreach (string state in CharacterLibrary.ImageStates)
                {
                    string sprite = state == "normal" ? main : original.ResolveSprite(state);
                    if (state == "normal" || sprite != main) { Images[state] = (Bitmap)original.Sprites[sprite].Clone(); backgroundOptions[state] = false; }
                }
                CharacterName.Text = original.Definition.display_name; PersonaName.Text = original.Persona.name;
                PersonaText.Text = original.Persona.personality; SpeechStyle.Text = original.Persona.speech_style; BackgroundStory.Text = original.Persona.background;
                Likes.Text = String.Join(Environment.NewLine, original.Persona.likes ?? new string[0]); Dislikes.Text = String.Join(Environment.NewLine, original.Persona.dislikes ?? new string[0]);
            }
            else { PersonaText.Text = "友善、温柔，喜欢陪伴用户。"; SpeechStyle.Text = "用自然、简短的中文回答。"; BackgroundStory.Text = "用户的桌面小伙伴。"; }
            BuildDialogueTab(); UiTheme.Apply(this); UiTheme.Primary(CreateButton); States.SelectedIndex = 0; RefreshValidation();
        }
        private void BuildDialogueTab()
        {
            var tab = new TabPage("默认台词") { BackColor = Color.White, AutoScroll = true, Padding = new Padding(16) };
            Tabs.TabPages.Add(tab);
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            tab.Controls.Add(grid);
            var intro = new Label { Text = "优先级：用户自定义 > 模型生成 > 初始设定。留空使用下一优先级。\n每句最多 40 字符；保存立即生效。修改人设、重新生成都不会覆盖自定义内容。", AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
            grid.Controls.Add(intro, 0, 0); grid.SetColumnSpan(intro, 3);
            CustomDialogue custom = original == null ? null : original.Definition.custom_dialogue;
            AmbientLines generated = original == null ? null : AmbientDialogue.Read(original.Folder, AmbientDialogue.Fingerprint(original.Folder));
            grid.Controls.Add(new Label { Text = "待机台词\n每行一句，最多 20 句\n有填写时只轮换这些句子", AutoSize = true }, 0, 1);
            IdleLines.Multiline = true; IdleLines.AcceptsReturn = true; IdleLines.ScrollBars = ScrollBars.Vertical; IdleLines.MaxLength = 840; IdleLines.Height = 108; IdleLines.Dock = DockStyle.Top;
            IdleLines.Text = custom == null || custom.idle == null ? "" : String.Join(Environment.NewLine, custom.idle); grid.Controls.Add(IdleLines, 1, 1);
            grid.Controls.Add(new Label { Text = "留空时：\n" + String.Join("\n", AmbientDialogue.ResolveIdle(null, generated, original == null ? null : original.Definition)), AutoSize = true, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Margin = new Padding(12, 3, 0, 12) }, 2, 1);
            for (int i = 0; i < AmbientDialogue.Keys.Length; i++)
            {
                string key = AmbientDialogue.Keys[i], value;
                var field = new TextBox { MaxLength = 40, Dock = DockStyle.Top, Margin = new Padding(3, 9, 3, 9) };
                if (custom != null && custom.messages != null && custom.messages.TryGetValue(key, out value)) field.Text = value;
                DefaultMessages[key] = field;
                grid.Controls.Add(new Label { Text = AmbientDialogue.Labels[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i + 2);
                grid.Controls.Add(field, 1, i + 2);
                grid.Controls.Add(new Label { Text = "留空时：" + (original == null ? "创建后使用初始台词" : AmbientDialogue.Resolve(key, null, generated, original.Definition)), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = UiTheme.Muted, Margin = new Padding(12, 0, 0, 0) }, 2, i + 2);
            }
        }
        private CustomDialogue ReadDialogue()
        {
            // Preserve unknown custom interaction keys from imported character packs.
            var messages = original != null && original.Definition.custom_dialogue != null && original.Definition.custom_dialogue.messages != null
                ? new Dictionary<string, string>(original.Definition.custom_dialogue.messages) : new Dictionary<string, string>();
            foreach (var field in DefaultMessages) { string text = field.Value.Text.Trim(); if (text.Length == 0) messages.Remove(field.Key); else messages[field.Key] = text; }
            var idle = new List<string>(); foreach (string line in ReadList(IdleLines.Text)) if (!String.IsNullOrWhiteSpace(line)) idle.Add(line.Trim());
            var dialogue = new CustomDialogue { messages = messages, idle = idle.ToArray() }; CustomDialogue.Validate(dialogue); return dialogue;
        }
        private static string[] ReadList(string value) { return value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); }
        private static void AddField(TableLayoutPanel panel, string label, TextBox field, int max, int height)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 0, 5) });
            field.Dock = DockStyle.Top; field.MaxLength = max; field.Height = height; field.Margin = new Padding(0, 0, 0, 8);
            if (height > 29) { field.Multiline = true; field.ScrollBars = ScrollBars.Vertical; field.AcceptsReturn = true; }
            panel.Controls.Add(field);
        }
        private void PickImage()
        {
            using (var picker = new OpenFileDialog { Title = "上传" + CharacterLibrary.ImageLabels[States.SelectedIndex], Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true, Multiselect = false })
                if (picker.ShowDialog(FindForm()) == DialogResult.OK) SetImage(SelectedState, picker.FileName, false);
        }
        internal bool SetImage(string state, string path, bool removeBackground)
        {
            try
            {
                if (Array.IndexOf(CharacterLibrary.ImageStates, state) < 0) throw new CharacterLoadException("图片状态无效。");
                Bitmap prepared = CharacterLibrary.PrepareImage(path, removeBackground);
                Bitmap previous; Images.TryGetValue(state, out previous);
                // Replace only after processing succeeds, so a bad file never erases the previous upload.
                preview.Skin = null; changedImages.Add(state); Images[state] = prepared; paths[state] = path; backgroundOptions[state] = removeBackground;
                if (previous != null) previous.Dispose();
                if (state == "normal" && String.IsNullOrWhiteSpace(CharacterName.Text))
                {
                    string name = Path.GetFileNameWithoutExtension(path); CharacterName.Text = name.Substring(0, Math.Min(30, name.Length));
                }
                RefreshSelectedImage(); RefreshValidation(); return true;
            }
            catch (Exception error)
            {
                RefreshSelectedImage(); status.ForeColor = Color.Firebrick;
                status.Text = error is CharacterLoadException ? error.Message : "图片读取失败，请选择有效的 PNG、JPG 或 BMP。"; return false;
            }
        }
        internal void ClearImage(string state)
        {
            Bitmap previous;
            if (Images.TryGetValue(state, out previous)) { preview.Skin = null; Images.Remove(state); previous.Dispose(); }
            changedImages.Add(state); paths.Remove(state); backgroundOptions.Remove(state); RefreshSelectedImage(); RefreshValidation();
        }
        private void RefreshSelectedImage()
        {
            updating = true;
            try
            {
                string state = SelectedState; Bitmap image; bool hasImage = Images.TryGetValue(state, out image);
                if (!hasImage) Images.TryGetValue("normal", out image);
                preview.Skin = image; preview.Invalidate(); States.Invalidate();
                imageTitle.Text = CharacterLibrary.ImageLabels[Array.IndexOf(CharacterLibrary.ImageStates, state)] + (state == "normal" ? "  · 必填" : "  · 选填");
                imageHint.Text = hasImage ? "已上传 · 推荐透明 PNG，每个状态使用一张完整角色图" : state == "normal" ? "上传桌宠正常站立、不做动作时的完整图片" : "未上传 · 将使用主图（当前预览为主图）";
                upload.Text = hasImage ? "替换图片" : "上传图片"; ClearButton.Enabled = hasImage; RemoveBackground.Enabled = hasImage && paths.ContainsKey(state);
                RemoveBackground.Checked = hasImage && backgroundOptions[state];
            }
            finally { updating = false; }
        }
        private void RefreshValidation()
        {
            bool main = Images.ContainsKey("normal"); CreateButton.Enabled = main && !String.IsNullOrWhiteSpace(CharacterName.Text) && !String.IsNullOrWhiteSpace(PersonaName.Text);
            status.ForeColor = UiTheme.Muted;
            status.Text = !main ? "请先上传主图，其他图片可以留空。" : String.IsNullOrWhiteSpace(CharacterName.Text) ? "请填写角色名称。" : String.IsNullOrWhiteSpace(PersonaName.Text) ? "请在“角色人设”中填写称呼。" : "主图已就绪，另有 " + (Images.Count - 1) + " 张表情 / 动作图。修改保存后生效。";
        }
        internal bool TryCreate()
        {
            try
            {
                string name = CharacterName.Text.Trim();
                if (!Images.ContainsKey("normal")) throw new CharacterLoadException("请上传主图。");
                var persona = new PersonaDefinition { name = PersonaName.Text.Trim(), personality = PersonaText.Text.Trim(), speech_style = SpeechStyle.Text.Trim(), background = BackgroundStory.Text.Trim(), likes = ReadList(Likes.Text), dislikes = ReadList(Dislikes.Text) };
                var dialogue = ReadDialogue();
                if (original == null) CreatedFolder = CharacterLibrary.Import(folder, name, Images, persona, dialogue);
                else
                {
                    var changes = new Dictionary<string, Bitmap>();
                    foreach (string state in changedImages) { Bitmap image; Images.TryGetValue(state, out image); changes[state] = image; }
                    CharacterLibrary.Update(original, name, changes, persona, dialogue); CreatedFolder = original.Folder;
                }
                return true;
            }
            catch (Exception error) { status.ForeColor = Color.Firebrick; status.Text = "保存失败：" + error.Message; return false; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { preview.Skin = null; foreach (Bitmap image in Images.Values) image.Dispose(); Images.Clear(); if (original != null) { original.Dispose(); original = null; } }
            base.Dispose(disposing);
        }
    }
}
