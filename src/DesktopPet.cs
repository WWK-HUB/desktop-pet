using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopPet.Chat;
using DesktopPet.Characters;

namespace DesktopPet
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool test = Array.IndexOf(args, "--self-test") >= 0;
            string output = test && args.Length > 1 && !args[1].StartsWith("--") ? Path.GetFullPath(args[1]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests", "results");
            try
            {
                if (test)
                {
                    using (Atlas atlas = new Atlas(CharacterLoader.Load(CharacterLoader.ResolveSelection(AppDomain.CurrentDomain.BaseDirectory, args))))
                        return SelfTest.Run(atlas, output);
                }
                bool first;
                using (Mutex mutex = new Mutex(true, "Local\\PixelCompanionDemo_Xiaoye", out first))
                {
                    if (!first)
                    {
                        Native.PostMessage((IntPtr)0xffff, Native.WM_RECALL, IntPtr.Zero, IntPtr.Zero);
                        return 0;
                    }
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    string selection = Array.IndexOf(args, "--character") >= 0 ? CharacterLoader.ResolveSelection(AppDomain.CurrentDomain.BaseDirectory, args) : null;
                    using (LauncherForm launcher = new LauncherForm(AppDomain.CurrentDomain.BaseDirectory, selection, Array.IndexOf(args, "--no-auto-phrases") < 0)) Application.Run(launcher);
                }
                return 0;
            }
            catch (Exception ex)
            {
                string log = Path.Combine(Path.GetTempPath(), "PixelCompanion-error.log");
                File.WriteAllText(log, ex.ToString());
                if (!test) MessageBox.Show("小人暂时没有成功启动。错误记录：\n" + log + "\n\n" + ex.Message, "桌面小人", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "FAILED.txt"), ex.ToString()); }
                return 1;
            }
        }
    }

    // Rendering adapter. All character data and file loading belong to CharacterLoader.
    internal sealed class Atlas : IDisposable
    {
        internal readonly CharacterPack Character;
        internal readonly Dictionary<string, Bitmap> Sprites;
        internal readonly Icon PetIcon;
        internal Atlas(CharacterPack character)
        {
            Character = character; Sprites = character.Sprites;
            using (Bitmap small = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    string icon = String.IsNullOrEmpty(character.Definition.icon) ? character.ResolveSprite(character.Definition.default_expression) : character.Definition.icon;
                    g.DrawImage(Sprites[icon], new Rectangle(0, 0, 32, 32));
                }
                IntPtr handle = small.GetHicon();
                using (Icon temp = Icon.FromHandle(handle)) PetIcon = (Icon)temp.Clone();
                Native.DestroyIcon(handle);
            }
        }
        public void Dispose()
        {
            Character.Dispose();
            PetIcon.Dispose();
        }
    }

    internal static class Painter
    {
        internal static Bitmap Frame(Atlas atlas, string pose, int height, bool mirror, double clock, bool moving, string speech, double jump, bool chat = false, string pageHint = "")
        {
            int width = Math.Max(chat ? 320 : 240, height + 100), canvasHeight = height + 110 + (chat ? BubbleText.ExtraHeight : 0);
            Bitmap frame = new Bitmap(width, canvasHeight, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(frame))
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                Bitmap sprite = atlas.Sprites[atlas.Character.ResolveSprite(pose)];
                // Some expression symbols exceed the hairline; keep their natural proportions.
                float scale = Math.Min((float)height / sprite.Height, (float)(width - 24) / sprite.Width);
                int w = Math.Max(1, (int)Math.Round(sprite.Width * scale)), drawnHeight = Math.Max(1, (int)Math.Round(sprite.Height * scale));
                int bob = moving ? (int)(Math.Abs(Math.Sin(clock * 10)) * 5) : (int)(Math.Sin(clock * 2) * 1.5);
                int top = canvasHeight - drawnHeight - 9 - bob - (int)jump;
                if (mirror)
                    g.DrawImage(sprite, new Point[] { new Point((width + w) / 2, top), new Point((width - w) / 2, top), new Point((width + w) / 2, top + drawnHeight) }, new Rectangle(0, 0, sprite.Width, sprite.Height), GraphicsUnit.Pixel);
                else g.DrawImage(sprite, new Rectangle((width - w) / 2, top, w, drawnHeight), 0, 0, sprite.Width, sprite.Height, GraphicsUnit.Pixel);
                if (!String.IsNullOrEmpty(speech))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point))
                    {
                        SizeF textSize = g.MeasureString(speech, font);
                        int bw = chat ? width - 16 : Math.Min(width - 12, (int)textSize.Width + 28);
                        int bh = chat ? 112 : 33;
                        Rectangle bubble = new Rectangle((width - bw) / 2, Math.Max(4, top - bh - 12), bw, bh);
                        using (GraphicsPath path = Rounded(bubble, 10))
                        using (Brush bg = new SolidBrush(Color.FromArgb(248, 247, 252, 255)))
                        using (Pen border = new Pen(UiTheme.Border, 1))
                        using (Brush ink = new SolidBrush(Color.FromArgb(47, 56, 79)))
                        using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.FillPath(bg, path); g.DrawPath(border, path);
                            if (chat)
                            {
                                using (StringFormat chatFormat = BubbleText.Format())
                                    g.DrawString(speech, font, ink, new RectangleF(bubble.X + 16, bubble.Y + 10, bubble.Width - 32, BubbleText.TextHeight), chatFormat);
                                using (Font hintFont = new Font("Microsoft YaHei UI", 8f))
                                    g.DrawString(pageHint, hintFont, Brushes.SlateGray, new PointF(bubble.X + 16, bubble.Bottom - 21));
                            }
                            else g.DrawString(speech, font, ink, bubble, format);
                        }
                        using (Brush bg = new SolidBrush(Color.FromArgb(248, 247, 252, 255)))
                            g.FillPolygon(bg, new Point[] { new Point(width / 2 - 5, bubble.Bottom - 1), new Point(width / 2 + 5, bubble.Bottom - 1), new Point(width / 2, bubble.Bottom + 5) });
                    }
                }
            }
            return frame;
        }
        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    internal sealed class PetWindow : Form
    {
        private readonly Atlas atlas;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly Random random = new Random();
        internal readonly ContextMenuStrip PetMenu = new ContextMenuStrip();
        private readonly NotifyIcon tray;
        private readonly bool testing;
        internal string Pose;
        internal int PetHeight;
        internal bool Wander = true;
        internal bool Resting;
        internal int Direction = 1;
        internal double AnchorX, AnchorY;
        internal double Until = 4;
        internal double LastTime;
        internal int RenderCount;
        internal bool Dragging;
        private bool pressed, moved;
        private Point mouseStart, windowStart;
        private string speech;
        private double speechUntil = 6;
        private double poseStarted;
        private readonly ChatService chatService;
        internal event Action SettingsSaved;
        internal readonly ChatArchive Archive;
        private AmbientLines ambient;
        private CharacterDefinition dialogueDefinition;
        private string customDialogueSignature;
        private string personaFingerprint, personaName;
        private int personaRevision;
        private double nextIdleSpeech = 30;
        private double nextPersonaRefresh, chatBubbleUntil;
        internal string CharacterFolder { get { return atlas.Character.Folder; } }
        internal string CurrentSpeech { get { return speech; } }
        private CancellationTokenSource chatCancellation;
        internal ChatInputForm ChatInput;
        internal ModelSettingsForm ModelSettingsWindow;
        private readonly string settingsDirectory;
        private readonly Action openLauncher;
        internal bool ChatBusy { get; private set; }
        internal string ChatReply { get; private set; }
        internal List<string> ChatPages = new List<string>();
        internal int ChatPage;
        private bool chatVisible;
        private ToolStripMenuItem chatItem, previousReply, nextReply, dismissReply;
        private ToolStripMenuItem wanderItem, restItem;
        private readonly List<ToolStripMenuItem> sizeItems = new List<ToolStripMenuItem>();
        private double Now { get { return watch.Elapsed.TotalSeconds; } }

        internal PetWindow(Atlas atlas, bool testing, ChatService chatService = null, string settingsDirectory = null, Action openLauncher = null)
        {
            this.atlas = atlas; this.testing = testing;
            this.openLauncher = openLauncher;
            Pose = atlas.Character.Definition.default_animation;
            PetHeight = atlas.Character.Definition.default_size;

            this.settingsDirectory = settingsDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            Archive = new ChatArchive(this.settingsDirectory, atlas.Character.Folder);
            this.chatService = chatService ?? new ChatService(this.settingsDirectory, new CharacterPersonaPromptSource(atlas.Character)) { ContextTurns = Archive.ReadContextTurns(), ContextCharacterLimit = 6000 };
            Text = atlas.Character.Definition.display_name + " · 桌宠"; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Icon = atlas.PetIcon;
            ClientSize = new Size(Math.Max(240, PetHeight + 100), PetHeight + 110); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.StandardDoubleClick, true);
            BuildMenu();
            RefreshPersona(); speech = Say("startup");
            if (!testing)
            {
                tray = new NotifyIcon { Icon = atlas.PetIcon, Text = atlas.Character.Definition.display_name + "：右键菜单 / 双击找回", ContextMenuStrip = PetMenu, Visible = true };
                tray.DoubleClick += delegate { Recall(); };
            }
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            AnchorX = area.Right - Width - 65; AnchorY = area.Bottom - Height;
            Location = new Point((int)AnchorX, (int)AnchorY);
            timer.Interval = 33;
            timer.Tick += delegate { Tick(Now); };
            Shown += delegate { Render(); if (!testing) timer.Start(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += PreferencesChanged;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80000 | 0x80 | 0x08000000; return p; }
        }
        private void BuildMenu()
        {
            PetMenu.Font = new Font("Microsoft YaHei UI", 10f);
            PetMenu.Renderer = new ToolStripProfessionalRenderer(new PetMenuColors()); PetMenu.ForeColor = UiTheme.Ink; PetMenu.Padding = new Padding(4, 6, 4, 6);
            PetMenu.Items.Add(new ToolStripMenuItem("✦  " + atlas.Character.Definition.display_name) { Enabled = false });
            PetMenu.Items.Add(new ToolStripSeparator());
            AddAction("摸摸头", "happy", "pet", 3);
            AddAction("挥挥手", "wave", "wave", 3);
            AddAction("跳一下", "jump", "jump", 0.9);
            AddAction("跑两步", "run", "run", 2.4);
            ToolStripMenuItem expressions = new ToolStripMenuItem("换个表情");
            string[] keys = { "normal", "happy", "wink", "surprised", "sad", "angry" };
            string[] labels = { "普通", "开心", "眨眼", "惊讶", "难过", "生气" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                expressions.DropDownItems.Add(labels[i], null, delegate { Act(key, Say(key), 3.5); });
            }
            PetMenu.Items.Add(expressions);
            PetMenu.Items.Add(new ToolStripSeparator());
            wanderItem = new ToolStripMenuItem("自动散步") { Checked = true, CheckOnClick = true };
            wanderItem.Click += delegate { Wander = wanderItem.Checked; Act(atlas.Character.Definition.default_expression, Say(Wander ? "wander_on" : "wander_off"), 2); };
            PetMenu.Items.Add(wanderItem);
            restItem = new ToolStripMenuItem("安静陪伴") { CheckOnClick = true };
            restItem.Click += delegate { Resting = restItem.Checked; Act(atlas.Character.Definition.default_expression, Say(Resting ? "rest_on" : "rest_off"), 2); };
            PetMenu.Items.Add(restItem);
            ToolStripMenuItem sizes = new ToolStripMenuItem("小人大小");
            int[] heights = atlas.Character.Definition.size_options;
            for (int i = 0; i < heights.Length; i++)
            {
                int h = heights[i];
                ToolStripMenuItem item = new ToolStripMenuItem(h + " px" + (h == atlas.Character.Definition.default_size ? " · 默认" : "")) { Checked = h == PetHeight, Tag = h };
                item.Click += delegate { ResizePet(h); };
                sizes.DropDownItems.Add(item); sizeItems.Add(item);
            }
            PetMenu.Items.Add(sizes);
            PetMenu.Items.Add("回到屏幕右下角", null, delegate { Recall(); });
            PetMenu.Items.Add(new ToolStripSeparator());
            PetMenu.Items.Add("使用方法", null, delegate { MessageBox.Show("拖动：按住桌宠移动\n单击：摸摸头\n双击：跳一下\n右键：动作、大小、安静陪伴、AI 聊天\n\n找不到桌宠？双击通知区域的桌宠头像。\n更换皮肤或编辑人设：右键 → 打开启动器。\n收回桌宠：返回启动器；关闭启动器退出整个程序。\n\n自定义单图皮肤沿用同一张图，通过位移和翻转呈现动作。", "桌面伙伴 · 使用方法", MessageBoxButtons.OK, MessageBoxIcon.Information); });
            chatItem = new ToolStripMenuItem("AI 聊天…") { Name = "chat" };
            chatItem.Click += delegate { OpenChat(); };
            PetMenu.Items.Add(chatItem);
            PetMenu.Items.Add("聊天记录…", null, delegate { OpenChat(); });
            var settingsItem = new ToolStripMenuItem("设置模型…") { Name = "modelSettings" };
            settingsItem.Click += delegate { OpenModelSettings(); };
            PetMenu.Items.Add(settingsItem);
            if (openLauncher != null) PetMenu.Items.Add(new ToolStripMenuItem("打开启动器 / 更换皮肤…", null, delegate { openLauncher(); }) { Name = "launcher" });
            previousReply = new ToolStripMenuItem("上一页回复") { Name = "chatPrevious", Enabled = false };
            nextReply = new ToolStripMenuItem("下一页回复") { Name = "chatNext", Enabled = false };
            dismissReply = new ToolStripMenuItem("收起 AI 气泡") { Name = "chatDismiss", Enabled = false };
            previousReply.Click += delegate { TurnChatPage(-1); };
            nextReply.Click += delegate { TurnChatPage(1); };
            dismissReply.Click += delegate { chatVisible = false; UpdateCanvas(); UpdateChatMenu(); Render(); };
            PetMenu.Items.Add(previousReply); PetMenu.Items.Add(nextReply); PetMenu.Items.Add(dismissReply);
            PetMenu.Items.Add(openLauncher == null ? "退出" : "收回桌宠（返回启动器）", null, delegate { Close(); });
        }
        internal void OpenModelSettings()
        {
            if (ModelSettingsWindow == null || ModelSettingsWindow.IsDisposed)
            {
                ModelSettingsWindow = new ModelSettingsForm(settingsDirectory, delegate
                {
                    if (!ChatBusy) ShowChatText("模型设置已保存。右键 → AI 聊天，就可以开始啦。");
                    if (SettingsSaved != null) SettingsSaved();
                });
                Rectangle area = Screen.FromPoint(new Point((int)AnchorX, (int)AnchorY)).WorkingArea;
                ModelSettingsWindow.Location = new Point(Math.Max(area.Left, Math.Min((int)AnchorX - ModelSettingsWindow.Width, area.Right - ModelSettingsWindow.Width)), Math.Max(area.Top, Math.Min((int)AnchorY, area.Bottom - ModelSettingsWindow.Height)));
                ModelSettingsWindow.Show(this);
            }
            ModelSettingsWindow.Activate();
        }
        internal void OpenChat()
        {
            if (ChatInput == null || ChatInput.IsDisposed)
            {
                ChatInput = new ChatInputForm(SubmitChatAsync, CancelChat, Archive, chatService.ContextTurns, delegate(int turns) { chatService.ContextTurns = turns; Archive.SaveContextTurns(turns); });
                ChatInput.Text = "和 " + personaName + " 聊天";
                Rectangle area = Screen.FromPoint(new Point((int)AnchorX, (int)AnchorY)).WorkingArea;
                ChatInput.Location = new Point(Math.Max(area.Left, Math.Min((int)AnchorX - ChatInput.Width, area.Right - ChatInput.Width)), Math.Max(area.Top, Math.Min((int)AnchorY + 50, area.Bottom - ChatInput.Height)));
                ChatInput.Show(this);
            }
            ChatInput.SetBusy(ChatBusy); ChatInput.Activate();
        }
        internal async Task<bool> SubmitChatAsync(string input)
        {
            if (ChatBusy || IsDisposed) return false;
            input = (input ?? "").Trim();
            if (input.Length == 0 || input.Length > ChatService.MaxInputCharacters) { ShowChatText("请输入 1～1000 字的内容。"); return false; }
            RefreshPersona(); int revision = personaRevision; string fingerprint = personaFingerprint, speaker = personaName;
            ChatBusy = true;
            var cancellation = new CancellationTokenSource(); chatCancellation = cancellation;
            if (ChatInput != null) { ChatInput.SetPending(input); ChatInput.SetBusy(true); }
            ShowChatText("……正在想一想");
            string archivedReply = "", outcome = "未完成";
            try
            {
                string answer = await chatService.SendAsync(input, cancellation.Token);
                if (revision != personaRevision) throw new OperationCanceledException();
                archivedReply = answer; outcome = "success";
                if (IsDisposed || Disposing) return false;
                ShowChatText(answer); return true;
            }
            catch (OperationCanceledException)
            {
                archivedReply = revision != personaRevision ? "人设已更新，本次旧设定请求已取消。请重新发送。" : "已取消这次请求。"; outcome = "已取消";
                if (!IsDisposed && !Disposing) ShowChatText(archivedReply); return false;
            }
            catch (ChatException error)
            {
                archivedReply = error.UserMessage; outcome = error.Diagnostic == "persona_changed" ? "人设已更新" : "发送失败";
                if (!IsDisposed && !Disposing) ShowChatText(archivedReply); return false;
            }
            catch (Exception error)
            {
                new ChatLog().Write(error); archivedReply = "聊天暂时出了点问题，请再试一次。"; outcome = "发送失败";
                if (!IsDisposed && !Disposing) ShowChatText(archivedReply); return false;
            }
            finally
            {
                Archive.Append(new ArchivedTurn { time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), user = input, assistant = archivedReply, speaker = speaker, status = outcome, persona = fingerprint });
                ChatBusy = false; chatCancellation = null; cancellation.Dispose();
                if (!IsDisposed && !Disposing)
                {
                    if (ChatInput != null) { ChatInput.SetPending(null); ChatInput.SetBusy(false); if (Archive.Error != null) ChatInput.Notify(Archive.Error); }
                    UpdateChatMenu(); Render();
                }
            }
        }
        internal void RefreshPersona()
        {
            try
            {
                var persona = AmbientDialogue.Persona(CharacterFolder); string fingerprint = ChatArchive.Hash(PersonaPromptBuilder.Build(persona));
                var definition = CharacterLoader.LoadCurrentDefinition(CharacterFolder);
                string signature = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(definition.custom_dialogue);
                bool customChanged = customDialogueSignature != null && customDialogueSignature != signature;
                customDialogueSignature = signature; dialogueDefinition = definition;
                bool changed = personaFingerprint != null && fingerprint != personaFingerprint;
                personaFingerprint = fingerprint; personaName = persona.name;
                ambient = AmbientDialogue.Read(CharacterFolder, fingerprint);
                if (customChanged) { speech = ""; speechUntil = 0; if (IsHandleCreated) Render(); }
                if (changed)
                {
                    personaRevision++; chatService.InvalidateContext(); CancelChat();
                    speech = ""; speechUntil = 0; chatVisible = false; ChatReply = null;
                    if (IsHandleCreated) { UpdateCanvas(); UpdateChatMenu(); Render(); }
                    if (ChatInput != null) { ChatInput.Text = "和 " + personaName + " 聊天"; ChatInput.Notify("人设已更新，旧上下文已清空。历史记录仍可查看。"); }
                }
            }
            catch { ambient = null; }
        }
        private string Say(string key)
        {
            if (testing) return atlas.Character.Message(key);
            var definition = dialogueDefinition ?? atlas.Character.Definition;
            return AmbientDialogue.Resolve(key, definition.custom_dialogue, ambient, definition);
        }
        private void CancelChat() { if (chatCancellation != null) chatCancellation.Cancel(); }
        internal void ShowChatText(string text)
        {
            ChatReply = text; chatVisible = true; ChatPage = 0;
            chatBubbleUntil = Now + 60;
            UpdateCanvas(); UpdateChatMenu(); Render();
        }
        internal void TurnChatPage(int direction)
        {
            ChatPage = Math.Max(0, Math.Min(ChatPages.Count - 1, ChatPage + direction));
            UpdateChatMenu(); Render();
        }
        private void UpdateChatMenu()
        {
            previousReply.Enabled = chatVisible && ChatPage > 0;
            nextReply.Enabled = chatVisible && ChatPage + 1 < ChatPages.Count;
            dismissReply.Enabled = chatVisible && !ChatBusy;
        }
        private void UpdateCanvas()
        {
            double foot = AnchorY + Height, center = AnchorX + Width / 2;
            ClientSize = new Size(Math.Max(chatVisible ? 320 : 240, PetHeight + 100), PetHeight + 110 + (chatVisible ? BubbleText.ExtraHeight : 0));
            AnchorY = foot - Height; AnchorX = center - Width / 2;
            if (chatVisible)
            {
                ChatPages = BubbleText.Pages(ChatReply ?? "", Width);
                ChatPage = Math.Max(0, Math.Min(ChatPage, ChatPages.Count - 1));
            }
            Clamp();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (chatVisible) TurnChatPage(e.Delta < 0 ? 1 : -1);
        }
        private void AddAction(string label, string pose, string text, double duration)
        {
            PetMenu.Items.Add(label, null, delegate { Act(pose, Say(text), duration); });
        }
        internal void Act(string pose, string text, double duration)
        {
            Pose = pose; poseStarted = Now; Until = poseStarted + duration;
            speech = text; speechUntil = poseStarted + Math.Min(3, duration + 0.5);
            Render();
        }
        internal void Tick(double time)
        {
            if (!testing && time >= nextPersonaRefresh) { nextPersonaRefresh = time + 2; RefreshPersona(); }
            if (chatVisible && !ChatBusy && time > chatBubbleUntil && (ChatInput == null || ChatInput.IsDisposed || !ChatInput.Visible))
            { chatVisible = false; UpdateCanvas(); UpdateChatMenu(); }
            double dt = Math.Max(0, Math.Min(0.08, time - LastTime)); LastTime = time;
            if (pressed || PetMenu.Visible) { Until += dt; poseStarted += dt; speechUntil += dt; return; }
            if (time > Until)
            {
                poseStarted = time;
                if (Resting || !Wander) { Pose = atlas.Character.Definition.default_expression; Until = time + 4; }
                else if (CurrentMotion == "walk" || CurrentMotion == "run") { Pose = atlas.Character.Definition.default_expression; Until = time + random.Next(3, 7); }
                else
                {
                    if (random.Next(4) == 0) { Pose = "wink"; Until = time + 2; }
                    else { Pose = "walk"; Direction = random.Next(2) == 0 ? -1 : 1; Until = time + random.Next(3, 6); }
                }
            }
            if (CurrentMotion == "walk" || CurrentMotion == "run")
            {
                AnchorX += Direction * (CurrentMotion == "run" ? 125 : 42) * dt;
                Rectangle area = Screen.FromPoint(new Point((int)AnchorX + Width / 2, (int)AnchorY + Height / 2)).WorkingArea;
                if (AnchorX < area.Left) { AnchorX = area.Left; Direction = 1; }
                if (AnchorX + Width > area.Right) { AnchorX = area.Right - Width; Direction = -1; }
            }
            if (!testing && !Resting && !ChatBusy && !chatVisible && time >= nextIdleSpeech)
            {
                var definition = dialogueDefinition ?? atlas.Character.Definition;
                string[] idle = AmbientDialogue.ResolveIdle(definition.custom_dialogue, ambient, definition);
                speech = idle[random.Next(idle.Length)];
                speechUntil = time + 5; nextIdleSpeech = time + random.Next(30, 51);
            }
            RenderAt(time);
        }
        internal void ResizePet(int height)
        {
            PetHeight = height; UpdateCanvas(); UpdateChatMenu();
            foreach (ToolStripMenuItem item in sizeItems) item.Checked = (int)item.Tag == height;
            Clamp(); Render();
        }
        internal void Recall()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            AnchorX = area.Right - Width - 35; AnchorY = area.Bottom - Height;
            Clamp(); Show(); Act("wave", Say("recall"), 3);
        }
        internal void Clamp()
        {
            Rectangle area = Screen.FromPoint(new Point((int)AnchorX + Width / 2, (int)AnchorY + Height / 2)).WorkingArea;
            AnchorX = Math.Max(area.Left, Math.Min(AnchorX, area.Right - Width));
            AnchorY = Math.Max(area.Top, Math.Min(AnchorY, area.Bottom - Height));
        }
        private void DisplayChanged(object sender, EventArgs e) { QueueClamp(); }
        private void PreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) { QueueClamp(); }
        private void QueueClamp()
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { if (!IsDisposed) { Clamp(); Render(); } });
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_RECALL) { if (openLauncher == null) Recall(); return; }
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
            base.WndProc(ref m);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                pressed = true; moved = false; Dragging = false;
                mouseStart = PointToScreen(e.Location); windowStart = new Point((int)AnchorX, (int)AnchorY); Capture = true;
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressed) return;
            Point current = PointToScreen(e.Location);
            int dx = current.X - mouseStart.X, dy = current.Y - mouseStart.Y;
            if (!moved && Math.Abs(dx) + Math.Abs(dy) < 6) return;
            moved = true; Dragging = true;
            Pose = "surprised"; speech = Say("drag"); speechUntil = Now + 2;
            AnchorX = windowStart.X + dx; AnchorY = windowStart.Y + dy;
            Render();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { PetMenu.Show(PointToScreen(e.Location)); return; }
            if (e.Button != MouseButtons.Left || !pressed) return;
            bool didMove = moved;
            pressed = false; Dragging = false; Capture = false;
            Clamp();
            if (didMove) Act("wave", Say("drop"), 2.5);
            else if (CurrentMotion != "jump" || Now > Until) Act("happy", Say("pet"), 2.8);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && pressed) { pressed = false; Dragging = false; Clamp(); Act(atlas.Character.Definition.default_expression, Say("released"), 2); }
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) Act("jump", Say("jump"), 0.9);
        }
        private string CurrentMotion { get { return atlas.Character.MotionFor(Pose); } }
        internal void Render() { RenderAt(Now); }
        private void RenderAt(double time)
        {
            double t = Math.Max(0, Math.Min(1, (time - poseStarted) / 0.9));
            double jump = CurrentMotion == "jump" ? Math.Sin(t * Math.PI) * 48 : 0;
            string shown = chatVisible && ChatPages.Count > 0 ? ChatPages[ChatPage] : time < speechUntil ? speech : "";
            string hint = ChatBusy ? "等待中 · 可以继续拖动小人" : ChatPages.Count > 1 ? (ChatPage + 1) + "/" + ChatPages.Count + " · 滚轮或右键翻页" : "右键继续聊天或收起气泡";
            using (Bitmap frame = Painter.Frame(atlas, Pose, PetHeight, (CurrentMotion == "walk" || CurrentMotion == "run") && Direction < 0, Resting ? 0 : time, CurrentMotion == "walk" || CurrentMotion == "run", shown, jump, chatVisible, hint))
                Native.Present(Handle, frame, (int)Math.Round(AnchorX), (int)Math.Round(AnchorY));
            RenderCount++;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelChat();
                if (ChatInput != null) ChatInput.Dispose();
                if (ModelSettingsWindow != null) ModelSettingsWindow.Dispose();
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= PreferencesChanged;
                timer.Stop(); timer.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                PetMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Native
    {
        internal static readonly int WM_RECALL = RegisterWindowMessage("PixelCompanionDemo.Recall.1");
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; internal POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct SIZE { internal int X, Y; internal SIZE(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct BLEND { internal byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr screen, ref POINT dst, ref SIZE size, IntPtr src, ref POINT origin, int key, ref BLEND blend, int flags);
        [DllImport("user32.dll")] internal static extern uint GetGuiResources(IntPtr process, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
        internal static void Present(IntPtr handle, Bitmap frame, int x, int y)
        {
            IntPtr screen = GetDC(IntPtr.Zero), memory = CreateCompatibleDC(screen);
            IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                bitmap = frame.GetHbitmap(Color.FromArgb(0)); previous = SelectObject(memory, bitmap);
                POINT dst = new POINT(x, y), origin = new POINT(0, 0); SIZE size = new SIZE(frame.Width, frame.Height);
                BLEND blend = new BLEND { Operation = 0, Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(handle, screen, ref dst, ref size, memory, ref origin, 0, ref blend, 2)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    internal static class SelfTest
    {
        private static readonly List<string> Results = new List<string>();
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            Results.Add("PASS: " + name);
        }
        private static IntPtr XY(int x, int y) { return (IntPtr)((y << 16) | (x & 0xffff)); }
        internal static int Run(Atlas atlas, string output)
        {
            Directory.CreateDirectory(output);
            Check(atlas.Sprites.Count == 15, "Demo character pack loads all 15 original sprite regions");
            string[] poses = { "normal", "happy", "wink", "surprised", "sad", "angry", "walk", "run", "jump", "wave", "front", "left", "back", "right" };
            using (Bitmap sheet = new Bitmap(1000, 4 * 290))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font font = new Font("Microsoft YaHei UI", 12))
            {
                g.Clear(Color.FromArgb(235, 239, 245));
                for (int i = 0; i < poses.Length; i++)
                {
                    using (Bitmap frame = Painter.Frame(atlas, poses[i], 160, false, 0, false, "", 0))
                    {
                        Check(frame.GetPixel(0, 0).A == 0, poses[i] + " has transparent canvas");
                        int opaque = 0;
                        for (int y = 0; y < frame.Height; y++) for (int x = 0; x < frame.Width; x++) if (frame.GetPixel(x, y).A > 200) opaque++;
                        Check(opaque > 3000 && opaque < 30000, poses[i] + " retains visible character without rectangular background");
                        g.DrawImageUnscaled(frame, (i % 4) * 250 - 5, (i / 4) * 290 - 12);
                        g.DrawString(poses[i], font, Brushes.SlateGray, (i % 4) * 250 + 85, (i / 4) * 290 + 262);
                    }
                }
                sheet.Save(Path.Combine(output, "sprite-review.png"), ImageFormat.Png);
            }
            using (Form background = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, BackColor = Color.FromArgb(231, 238, 246), StartPosition = FormStartPosition.Manual, Size = new Size(440, 440), TopMost = true })
            using (PetWindow pet = new PetWindow(atlas, true))
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                background.Location = new Point(area.Left + 100, area.Top + 100);
                background.Show(); pet.Show();
                pet.AnchorX = background.Left + 75; pet.AnchorY = background.Top + 80; pet.Render();
                Application.DoEvents();
                Check(pet.Visible && pet.TopMost && !pet.ShowInTaskbar, "Native frameless topmost window starts");
                Check(Native.SendMessage(pet.Handle, 0x21, IntPtr.Zero, IntPtr.Zero) == (IntPtr)3, "Mouse activation does not steal keyboard focus");
                Check(Native.WindowFromPoint(new Native.POINT((int)pet.AnchorX + 2, (int)pet.AnchorY + 2)) != pet.Handle, "Transparent corner passes hit testing to underlying window");
                using (Bitmap screenshot = new Bitmap(440, 440))
                {
                    using (Graphics capture = Graphics.FromImage(screenshot)) capture.CopyFromScreen(background.Location, Point.Empty, screenshot.Size);
                    screenshot.Save(Path.Combine(output, "desktop-window.png"), ImageFormat.Png);
                    Check(screenshot.GetPixel(77, 82).ToArgb() == background.BackColor.ToArgb(), "Transparent corner is composited with the actual desktop background");
                }
                Native.SendMessage(pet.Handle, 0x201, (IntPtr)1, XY(140, 170));
                Native.SendMessage(pet.Handle, 0x202, IntPtr.Zero, XY(140, 170));
                Check(pet.Pose == "happy", "Native left click triggers happy expression");
                double oldX = pet.AnchorX, oldY = pet.AnchorY;
                Native.SendMessage(pet.Handle, 0x201, (IntPtr)1, XY(140, 170));
                Native.SendMessage(pet.Handle, 0x200, (IntPtr)1, XY(180, 195));
                Check(pet.Dragging && pet.Pose == "surprised", "Mouse drag enters carried pose");
                Native.SendMessage(pet.Handle, 0x202, IntPtr.Zero, XY(140, 170));
                Check(Math.Abs(pet.AnchorX - oldX - 40) < 1 && Math.Abs(pet.AnchorY - oldY - 25) < 1 && !pet.Dragging, "Drag moves 40 x 25 pixels and releases capture");
                Native.SendMessage(pet.Handle, 0x203, (IntPtr)1, XY(140, 170));
                Native.SendMessage(pet.Handle, 0x202, IntPtr.Zero, XY(140, 170));
                Check(pet.Pose == "jump", "Native double click triggers jump");
                Native.SendMessage(pet.Handle, 0x205, IntPtr.Zero, XY(140, 170));
                Application.DoEvents(); Check(pet.PetMenu.Visible, "Native right click opens action menu");
                pet.PetMenu.Close();
                ((ToolStripMenuItem)pet.PetMenu.Items[3]).PerformClick();
                Check(pet.Pose == "wave", "Wave menu action works");
                ToolStripMenuItem expressions = (ToolStripMenuItem)pet.PetMenu.Items[6];
                for (int i = 0; i < 6; i++) { ((ToolStripMenuItem)expressions.DropDownItems[i]).PerformClick(); Check(pet.Pose == poses[i], "Expression menu: " + poses[i]); }
                double foot = pet.AnchorY + pet.Height;
                pet.ResizePet(250); Check(pet.PetHeight == 250 && Math.Abs(pet.AnchorY + pet.Height - foot) < 1, "Resize preserves feet position");
                pet.ResizePet(140); pet.ResizePet(190);
                pet.Pose = "walk"; pet.Direction = 1; pet.Until = 100; pet.LastTime = 0; oldX = pet.AnchorX;
                pet.Tick(0.05); Check(pet.AnchorX > oldX, "Walking advances position by elapsed time");
                area = Screen.FromPoint(new Point((int)pet.AnchorX, (int)pet.AnchorY)).WorkingArea;
                pet.AnchorX = area.Right - pet.Width; pet.Direction = 1; pet.Tick(0.1);
                Check(pet.Direction == -1 && pet.AnchorX + pet.Width <= area.Right, "Walking reverses at screen edge");
                ((ToolStripMenuItem)pet.PetMenu.Items[8]).PerformClick();
                Check(!pet.Wander, "Automatic walking toggle works");
                pet.Until = 0; pet.Tick(2); Check(pet.Pose == "normal", "Disabled wandering returns to idle");
                ((ToolStripMenuItem)pet.PetMenu.Items[9]).PerformClick();
                pet.Until = 0; pet.Tick(3); Check(pet.Resting && pet.Pose == "normal", "Quiet companionship stops automatic actions");
                pet.AnchorX = -50000; pet.AnchorY = -50000; pet.Clamp();
                Check(Screen.FromPoint(new Point((int)pet.AnchorX, (int)pet.AnchorY)).WorkingArea.Contains(new Rectangle((int)pet.AnchorX, (int)pet.AnchorY, pet.Width, pet.Height)), "Off-screen position is recovered");
                Native.SendMessage(pet.Handle, Native.WM_RECALL, IntPtr.Zero, IntPtr.Zero);
                Check(pet.Pose == "wave", "Second-launch recall message recovers pet");
                uint before = Native.GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                for (int i = 0; i < 360; i++) pet.Render();
                uint after = Native.GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                Check(after <= before + 4, "360 frames do not leak GDI handles (" + before + " -> " + after + ")");
                ((ToolStripMenuItem)pet.PetMenu.Items[pet.PetMenu.Items.Count - 1]).PerformClick();
                Application.DoEvents(); Check(pet.IsDisposed, "Exit menu closes and disposes the window");
                background.Close();
            }
            using (FileStream icon = File.Create(Path.Combine(output, "pet.ico"))) atlas.PetIcon.Save(icon);
            Results.Add("Completed: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            File.WriteAllLines(Path.Combine(output, "test-results.txt"), Results.ToArray());
            return 0;
        }
    }
}
