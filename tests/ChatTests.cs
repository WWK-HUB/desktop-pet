using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DesktopPet.Chat;
using DesktopPet.Characters;

namespace DesktopPet
{
    internal static partial class ChatTests
    {
        private const string FakeKey = "TEST-ONLY-NOT-A-REAL-KEY";
        private const string FixtureModel = "local-test-model";
        private static readonly List<string> results = new List<string>();
        private static string output;
        private static string projectRoot;
        private static readonly JavaScriptSerializer json = new JavaScriptSerializer();
        [STAThread]
        private static int Main(string[] args)
        {
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            output = AppDomain.CurrentDomain.BaseDirectory;
            projectRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : AppDomain.CurrentDomain.BaseDirectory;
            bool livePersona = Array.IndexOf(args, "--live-persona") >= 0;
            bool launcherOnly = Array.IndexOf(args, "--launcher-only") >= 0;
            bool dialogueOnly = Array.IndexOf(args, "--dialogue-only") >= 0;
            bool uiPreview = Array.IndexOf(args, "--ui-preview") >= 0;
            bool transitionOnly = Array.IndexOf(args, "--transition-only") >= 0;
            if (!livePersona) foreach (string name in new[] { "AI_BASE_URL", "AI_API_KEY", "AI_MODEL", "AI_TIMEOUT_SECONDS" }) Environment.SetEnvironmentVariable(name, null);
            int code = 0;
            using (Form host = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1) })
            {
                host.Shown += async delegate
                {
                    try
                    {
                        if (livePersona) await RunLivePersona();
                        else if (uiPreview) await UiPreviewTests();
                        else if (transitionOnly) await TransitionTests();
                        else if (dialogueOnly) { using (var server = new MockServer()) await CustomDialogueTests(server, Path.Combine(output, "dialogue-fixture-" + Guid.NewGuid().ToString("N"))); }
                        else if (launcherOnly) { using (var server = new MockServer()) await LauncherTests(server); }
                        else await Run();
                    }
                    catch (Exception e) { results.Add("FAIL: " + e); code = 1; }
                    finally
                    {
                        results.Add(livePersona ? "Live persona test used only the configured API and synthetic test questions; see result above." : "These are LOCAL MOCK API tests. No real model or real API key was used.");
                        results.Add("Completed: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        File.WriteAllLines(Path.Combine(output, livePersona ? "persona-live-results.txt" : transitionOnly ? "transition-test-results.txt" : uiPreview ? "ui-preview-results.txt" : dialogueOnly ? "dialogue-test-results.txt" : launcherOnly ? "launcher-test-results.txt" : "chat-test-results.txt"), results, Encoding.UTF8);
                        host.Close();
                    }
                };
                Application.Run(host);
            }
            return code;
        }
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception(name); results.Add("PASS: " + name); }
        private static async Task<ChatException> Fails(Func<Task<string>> action, string diagnostic)
        {
            try { await action(); }
            catch (ChatException e) { Check(e.Diagnostic == diagnostic, diagnostic + " maps to a friendly ChatException"); return e; }
            throw new Exception("Expected error: " + diagnostic);
        }
        private static async Task WaitUntil(Func<bool> condition, int timeout = 6000)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!condition()) { if (clock.ElapsedMilliseconds > timeout) throw new Exception("Test wait timed out"); await Task.Delay(25); }
        }
        private static IntPtr XY(int x, int y) { return (IntPtr)((y << 16) | (x & 0xffff)); }
        private static object[] Messages(Dictionary<string, object> request) { return (object[])request["messages"]; }
        private static string Content(object message) { return (string)((Dictionary<string, object>)message)["content"]; }
        private static string Answer(string text) { return json.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = text } } } }); }
        private static async Task Run()
        {
            string configDir = Path.Combine(output, "isolated-config"); Directory.CreateDirectory(configDir);
            string envFile = Path.Combine(configDir, ".env");
            // All fixtures live below ignored tests/results; never read/overwrite the user's .env.
            File.WriteAllText(envFile, "AI_BASE_URL=\nAI_API_KEY=\nAI_MODEL=\n", Encoding.UTF8);
            var missing = new ChatService(configDir);
            await Fails(() => missing.SendAsync("你好", CancellationToken.None), "config_missing");
            var noFile = new ChatService(Path.Combine(configDir, "no-dotenv-file"));
            await Fails(() => noFile.SendAsync("你好", CancellationToken.None), "config_missing");
            using (MockServer server = new MockServer())
            {
                File.WriteAllText(envFile, "# test\nAI_BASE_URL='" + server.BaseUrl + "/' # comment\nAI_API_KEY=\"" + FakeKey + "#=\"\nAI_MODEL=" + FixtureModel + "\nAI_TIMEOUT_SECONDS=2\n", Encoding.UTF8);
                ChatConfig cfg = ChatConfig.Load(configDir);
                Check(cfg.Endpoint.AbsoluteUri == server.BaseUrl + "/chat/completions" && cfg.ApiKey == FakeKey + "#=", "Dotenv supports UTF-8, quotes, comments, trailing slash, and key punctuation");
                File.WriteAllText(envFile, "AI_BASE_URL=" + server.BaseUrl + "/chat/completions\nAI_API_KEY=\nAI_MODEL=" + FixtureModel + "\n", Encoding.UTF8);
                cfg = ChatConfig.Load(configDir);
                Check(cfg.Endpoint.AbsoluteUri == server.BaseUrl + "/chat/completions" && cfg.ApiKey == "", "Complete endpoint is not duplicated; loopback server permits an empty key");
                Environment.SetEnvironmentVariable("AI_MODEL", "override-test");
                Check(ChatConfig.Load(configDir).Model == "override-test", "Explicit AI_* environment variables override dotenv");
                Environment.SetEnvironmentVariable("AI_MODEL", null);
                File.WriteAllText(envFile, "AI_BASE_URL=invalid-url\nAI_MODEL=" + FixtureModel, Encoding.UTF8);
                await Fails(() => missing.SendAsync("hi", CancellationToken.None), "config_invalid_url");
                File.WriteAllText(envFile, "AI_BASE_URL=https://example.invalid/v1\nAI_MODEL=" + FixtureModel, Encoding.UTF8);
                await Fails(() => missing.SendAsync("hi", CancellationToken.None), "config_missing_key");
                cfg = new ChatConfig { Endpoint = new Uri(server.BaseUrl + "/chat/completions"), ApiKey = FakeKey, Model = FixtureModel, TimeoutSeconds = 2 };
                ChatLog log = new ChatLog(Path.Combine(output, "sanitized-test.log"));
                var service = new ChatService(() => cfg, c => new OpenAiCompatibleProvider(c), new DefaultSystemPromptSource(), log);
                server.Enqueue(200, Answer("你好，我是桌面 AI 助手。"));
                Check(await service.SendAsync("你好", CancellationToken.None) == "你好，我是桌面 AI 助手。", "Real HTTP transport decodes a UTF-8 mock completion");
                MockRequest first = server.Requests.Last();
                Check(first.Path == "/v1/chat/completions" && first.Authorization == "Bearer " + FakeKey && first.ContentType.StartsWith("application/json"), "Configured endpoint, bearer header and JSON content type are sent");
                Check((string)first.Body["model"] == FixtureModel && (bool)first.Body["stream"] == false, "Model comes from configuration and request uses non-streaming text protocol");
                Check(Messages(first.Body).Length == 2 && Content(Messages(first.Body)[0]) == new DefaultSystemPromptSource().GetSystemPrompt(), "Simple system prompt precedes the current user message");
                server.Enqueue(200, Answer("我刚才说：你好，我是桌面 AI 助手。"));
                await service.SendAsync("你刚才说什么？", CancellationToken.None);
                object[] second = Messages(server.Requests.Last().Body);
                Check(second.Length == 4 && Content(second[1]) == "你好" && Content(second[2]) == "你好，我是桌面 AI 助手。", "Second HTTP request includes the actual first user/assistant turn");
                for (int i = 0; i < 8; i++) { server.Enqueue(200, Answer("reply-" + i)); await service.SendAsync("turn-" + i, CancellationToken.None); }
                Check(service.HistoryCount == 12 && Messages(server.Requests.Last().Body).Length <= 14, "History is bounded to six complete turns plus system/current input");
                object[] recent = Messages(server.Requests.Last().Body);
                Check(Content(recent[1]) == "turn-1" && Content(recent[2]) == "reply-1", "Oldest turns are evicted in pairs, newest context retained");
                int history = service.HistoryCount;
                foreach (int status in new[] { 400, 401, 403, 404, 422, 429, 500, 302 })
                {
                    server.Enqueue(status, "{\"error\":\"" + FakeKey + " private-user-text\"}");
                    await Fails(() => service.SendAsync("private-user-text", CancellationToken.None), "http_status_" + status);
                    Check(service.HistoryCount == history, "HTTP " + status + " does not commit a failed turn");
                }
                server.Enqueue(200, "not-json"); await Fails(() => service.SendAsync("test", CancellationToken.None), "response_invalid_json");
                server.Enqueue(200, "{\"choices\":[]}"); await Fails(() => service.SendAsync("test", CancellationToken.None), "response_missing_content");
                server.Enqueue(200, new string('x', 524300)); await Fails(() => service.SendAsync("test", CancellationToken.None), "response_too_large");
                server.Enqueue(200, Answer(new string('a', 12001))); await Fails(() => service.SendAsync("test", CancellationToken.None), "reply_too_long");
                cfg.TimeoutSeconds = 1;
                server.Enqueue(200, Answer("too late"), 2200);
                await Fails(() => service.SendAsync("slow-headers", CancellationToken.None), "request_timeout");
                server.Enqueue(200, Answer("too late body"), 0, 2200);
                await Fails(() => service.SendAsync("slow-body", CancellationToken.None), "request_timeout");
                cfg.TimeoutSeconds = 5;
                server.Enqueue(200, Answer("delayed"), 1800);
                using (var cancel = new CancellationTokenSource())
                {
                    Task<string> pending = service.SendAsync("cancel-me", cancel.Token);
                    await Task.Delay(120);
                    await Fails(() => service.SendAsync("duplicate", CancellationToken.None), "request_already_running");
                    cancel.Cancel();
                    bool cancelled = false; try { await pending; } catch (OperationCanceledException) { cancelled = true; }
                    Check(cancelled && service.HistoryCount == history, "Cancellation stops request without corrupting history");
                }
                server.Enqueue(200, Answer("recovered"));
                Check(await service.SendAsync("retry", CancellationToken.None) == "recovered", "A successful retry works after failures and cancellation");
                server.Enqueue(-1, "");
                await Fails(() => service.SendAsync("reset connection", CancellationToken.None), "network_error");
                int beforeEmpty = server.Requests.Count;
                await Fails(() => service.SendAsync("  ", CancellationToken.None), "input_empty");
                await Fails(() => service.SendAsync(new string('x', 1001), CancellationToken.None), "input_too_long");
                Check(server.Requests.Count == beforeEmpty, "Invalid input never reaches HTTP transport");
                cfg.Model = "changed-model-for-test";
                server.Enqueue(200, Answer("new target")); await service.SendAsync("new model", CancellationToken.None);
                Check(Messages(server.Requests.Last().Body).Length == 2, "Changing endpoint/model starts fresh context");
                var alternatePrompt = new ChatService(() => cfg, c => new OpenAiCompatibleProvider(c), new TestPrompt(), log);
                server.Enqueue(200, Answer("injected")); await alternatePrompt.SendAsync("hi", CancellationToken.None);
                Check(Content(Messages(server.Requests.Last().Body)[0]) == "injected-system-prompt", "Future persona prompt can be injected independently of UI/provider");
                var bounded = new ChatService(() => cfg, c => new OpenAiCompatibleProvider(c), new DefaultSystemPromptSource(), log);
                for (int i = 0; i < 3; i++) { server.Enqueue(200, Answer(new string('a', 11000))); await bounded.SendAsync("large turn", CancellationToken.None); }
                Check(bounded.HistoryCount == 4, "History character budget also evicts complete oldest turns");
                string logged = File.ReadAllText(log.FilePath);
                Check(logged.Contains("http_status_401") && logged.Contains("request_timeout") && !logged.Contains(FakeKey) && !logged.Contains("private-user-text"), "Diagnostic log contains status/stack details without credentials or chat bodies");
                using (Atlas atlas = new Atlas(CharacterLoader.Load(Path.Combine(projectRoot, "tests", "fixtures", "demo"))))
                {
                    await LauncherTests(server);
                    await CharacterTests(server);
                    await CatalogTests(atlas, server);
                    await SettingsGuiTests(atlas, server);
                    await GuiTests(atlas, service, noFile, server);
                }
            }
            // Reserve then close a local port; no external hosts or credentials are used.
            var closed = new TcpListener(IPAddress.Loopback, 0); closed.Start(); int port = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();
            var offline = new ChatService(() => new ChatConfig { Endpoint = new Uri("http://127.0.0.1:" + port + "/v1/chat/completions"), Model = FixtureModel, ApiKey = "", TimeoutSeconds = 2 }, c => new OpenAiCompatibleProvider(c), new DefaultSystemPromptSource(), new ChatLog(Path.Combine(output, "sanitized-test.log")));
            bool unavailable = false;
            try { await offline.SendAsync("hi", CancellationToken.None); }
            catch (ChatException e) { unavailable = e.Diagnostic == "network_error" || e.Diagnostic == "request_timeout"; }
            Check(unavailable, "Stopped local server reports connection failure or OS connection timeout safely");
        }
        private static async Task SettingsGuiTests(Atlas atlas, MockServer server)
        {
            string folder = Path.Combine(output, "settings-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            using (PetWindow pet = new PetWindow(atlas, false, null, folder))
            {
                pet.Show(); pet.Wander = false;
                Native.SendMessage(pet.Handle, 0x205, IntPtr.Zero, XY(140, 170));
                ((ToolStripMenuItem)pet.PetMenu.Items["modelSettings"]).PerformClick(); pet.PetMenu.Close();
                ModelSettingsForm form = pet.ModelSettingsWindow;
                Check(form != null && form.Visible && form.ApiKey.UseSystemPasswordChar, "Right-click model settings opens real UI with masked API key");
                pet.OpenModelSettings(); Check(Object.ReferenceEquals(form, pet.ModelSettingsWindow), "Repeated settings entry reuses the existing window");
                int renders = pet.RenderCount; await Task.Delay(180);
                Check(pet.RenderCount > renders + 2, "Settings window is modeless and pet animation continues");
                form.SaveButton.PerformClick();
                Check(!form.IsDisposed && form.Status.ForeColor == Color.Firebrick && !File.Exists(Path.Combine(folder, ModelSettingsStore.FileName)), "Empty settings show inline validation and do not save");
                form.BaseUrl.Text = "bad-url"; form.Model.Text = FixtureModel; form.ApiKey.Text = FakeKey;
                form.SaveButton.PerformClick(); Check(form.Status.Text.Contains("Base URL") && !form.IsDisposed, "Invalid API address is rejected in settings UI");
                form.BaseUrl.Text = server.BaseUrl; form.Timeout.Value = 3;
                form.ShowKey.Checked = true; Check(!form.ApiKey.UseSystemPasswordChar, "Show-key checkbox works"); form.ShowKey.Checked = false;
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                form.Location = new Point(area.Left + 70, area.Top + 70);
                form.Status.Text = "设置保存在本机，下次启动仍会记住。"; form.Status.ForeColor = Color.DimGray;
                await Task.Delay(150);
                using (Bitmap screenshot = new Bitmap(form.Width, form.Height))
                {
                    using (Graphics g = Graphics.FromImage(screenshot)) g.CopyFromScreen(form.Location, Point.Empty, screenshot.Size);
                    screenshot.Save(Path.Combine(output, "model-settings-window.png"), ImageFormat.Png);
                }
                form.SaveButton.PerformClick(); Check(form.IsDisposed && pet.ChatReply.Contains("已保存"), "Save button persists configuration and confirms success on pet");
                string path = Path.Combine(folder, ModelSettingsStore.FileName);
                string persisted = File.ReadAllText(path);
                Check(!persisted.Contains(FakeKey) && persisted.Contains("EncryptedApiKey"), "Saved settings contain encrypted key, not plaintext credential");
                ChatConfig loaded = ChatConfig.Load(folder);
                Check(loaded.ApiKey == FakeKey && loaded.Model == FixtureModel && loaded.TimeoutSeconds == 3, "Saved settings round-trip through the production ChatConfig loader");
                File.WriteAllText(Path.Combine(folder, ".env"), "AI_MODEL=legacy-ignored\n");
                Environment.SetEnvironmentVariable("AI_MODEL", "environment-ignored");
                Check(ChatConfig.Load(folder).Model == FixtureModel, "UI-saved settings take precedence over legacy dotenv/environment values");
                Environment.SetEnvironmentVariable("AI_MODEL", null);
                pet.OpenModelSettings(); form = pet.ModelSettingsWindow;
                Check(form.BaseUrl.Text == server.BaseUrl && form.Model.Text == FixtureModel && form.ApiKey.Text == FakeKey && form.ApiKey.UseSystemPasswordChar, "Reopening settings pre-fills saved fields while keeping key masked");
                form.Model.Text = "unsaved"; form.Close();
                Check(File.ReadAllText(path) == persisted, "Closing without saving preserves the previous configuration");
                pet.OpenModelSettings(); form = pet.ModelSettingsWindow;
                form.Model.Text = "updated-model-fixture"; form.SaveButton.PerformClick();
                server.Enqueue(200, Answer("设置已生效"));
                Check(await pet.SubmitChatAsync("test settings") && (string)server.Requests.Last().Body["model"] == "updated-model-fixture", "Next chat uses model saved through UI without restart or rebuild");
                pet.OpenModelSettings(); form = pet.ModelSettingsWindow; form.BaseUrl.Text = "invalid"; form.SaveButton.PerformClick();
                Check(ChatConfig.Load(folder).Model == "updated-model-fixture", "Invalid edit cannot overwrite working settings");
                pet.Close(); Check(form.IsDisposed, "Pet exit disposes its settings window");
            }
            using (var form = new ModelSettingsForm(folder, delegate { }))
            {
                form.Show(); Check(form.Model.Text == "updated-model-fixture", "New settings window instance restores persisted configuration"); form.Close();
            }
        }
        private static async Task GuiTests(Atlas atlas, ChatService service, ChatService missing, MockServer server)
        {
            using (Form background = new Form { Text = "Local mock API test", FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, BackColor = Color.FromArgb(231, 238, 246), StartPosition = FormStartPosition.Manual, Size = new Size(840, 580), TopMost = true })
            using (PetWindow pet = new PetWindow(atlas, false, service))
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                background.Location = new Point(area.Left + 40, area.Top + 50); background.Show(); pet.Show();
                pet.Wander = false; pet.AnchorX = background.Left + 450; pet.AnchorY = background.Top + 190; pet.Render();
                Native.SendMessage(pet.Handle, 0x205, IntPtr.Zero, XY(140, 170));
                ((ToolStripMenuItem)pet.PetMenu.Items["chat"]).PerformClick(); pet.PetMenu.Close();
                Check(pet.ChatInput != null && pet.ChatInput.Visible && pet.ChatInput.Input.CanFocus, "Actual right-click AI chat entry opens a focusable modeless input");
                server.Enqueue(200, Answer("这是本地模拟接口的回复，用来验证桌宠的气泡显示。"), 1500);
                pet.ChatInput.Input.Text = "你好，桌宠"; pet.ChatInput.Send.PerformClick();
                await WaitUntil(() => pet.ChatBusy);
                Check(pet.ChatReply.Contains("正在想") && !pet.ChatInput.Send.Enabled, "Submitting via the send button shows waiting bubble and prevents duplicate send");
                int renders = pet.RenderCount; await Task.Delay(300);
                Check(pet.ChatBusy && pet.RenderCount > renders + 3, "Real WinForms animation timer keeps rendering during delayed HTTP request");
                double x = pet.AnchorX, y = pet.AnchorY;
                Native.SendMessage(pet.Handle, 0x201, (IntPtr)1, XY(160, 270));
                Native.SendMessage(pet.Handle, 0x200, (IntPtr)1, XY(190, 290));
                Check(pet.Dragging, "Native mouse drag works while HTTP request is pending");
                Native.SendMessage(pet.Handle, 0x202, IntPtr.Zero, XY(160, 270));
                Check(Math.Abs(pet.AnchorX - x - 30) < 1 && Math.Abs(pet.AnchorY - y - 20) < 1, "Dragging during request changes position correctly");
                Check(pet.ChatReply.Contains("正在想"), "Drag/action speech does not overwrite AI waiting state");
                await WaitUntil(() => !pet.ChatBusy);
                Check(pet.ChatReply.StartsWith("这是本地模拟") && pet.ChatInput.Send.Enabled && pet.ChatInput.Input.Text == "", "HTTP completion replaces bubble, restores send button and clears successful input");
                string longReply = String.Concat(Enumerable.Repeat("这是一段较长的模拟回复，用来检查自动换行和分页。桌宠仍然可以拖动，也保留了原来的动作。", 6));
                server.Enqueue(200, Answer(longReply)); await pet.SubmitChatAsync("请给长回复");
                Check(pet.ChatPages.Count > 1, "Long reply is paginated rather than clipped to a single-line bubble");
                string reconstructed = String.Concat(pet.ChatPages).Replace("\n", "");
                Check(reconstructed == longReply, "All characters of the long reply survive pagination");
                ((ToolStripMenuItem)pet.PetMenu.Items["chatNext"]).PerformClick(); Check(pet.ChatPage == 1, "Next reply page menu works");
                Native.SendMessage(pet.Handle, 0x20A, (IntPtr)(120 << 16), IntPtr.Zero); Check(pet.ChatPage == 0, "Mouse wheel can return to previous page");
                double foot = pet.AnchorY + pet.Height;
                pet.ResizePet(140); Check(Math.Abs(pet.AnchorY + pet.Height - foot) < 1 && pet.ChatPages.Count > 1, "Resizing with AI bubble preserves feet and repaginates");
                pet.ResizePet(190);
                pet.AnchorX = background.Left + 455; pet.AnchorY = background.Top + 70; pet.Render();
                pet.ChatInput.Location = new Point(background.Left + 45, background.Top + 225);
                pet.ChatInput.Input.Text = "你刚才说什么？";
                await Task.Delay(120);
                using (Bitmap shot = new Bitmap(background.Width, background.Height))
                {
                    using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(background.Location, Point.Empty, shot.Size);
                    shot.Save(Path.Combine(output, "chat-window.png"), ImageFormat.Png);
                }
                using (Bitmap frame = Painter.Frame(atlas, "normal", 190, false, 0, false, pet.ChatPages[0], 0, true, "1/" + pet.ChatPages.Count + " · 滚轮或右键翻页"))
                    frame.Save(Path.Combine(output, "chat-bubble.png"), ImageFormat.Png);
                ((ToolStripMenuItem)pet.PetMenu.Items["chatDismiss"]).PerformClick(); Check(pet.Height == 300, "Dismissing AI reply restores original transparent window size");
                server.Enqueue(401, "denied"); bool sent = await pet.SubmitChatAsync("error");
                Check(!sent && pet.Visible && pet.ChatReply.Contains("API Key"), "API auth failure is shown in the pet bubble without crashing GUI");
                server.Enqueue(200, Answer("cancelled reply"), 2000);
                Task<bool> closingComposer = pet.SubmitChatAsync("close composer");
                await Task.Delay(120); pet.ChatInput.Close();
                Check(await closingComposer && pet.Visible, "Closing modeless composer during request does not crash or lose reply");
                server.Enqueue(200, Answer("too late"), 2000);
                Task<bool> closingPet = pet.SubmitChatAsync("close pet");
                await Task.Delay(120); pet.Close();
                Check(!await closingPet && pet.IsDisposed, "Exiting pet cancels pending HTTP and does not touch disposed controls");
                background.Close();
            }
            using (PetWindow pet = new PetWindow(atlas, false, missing))
            {
                pet.Show(); Check(pet.Visible, "Pet starts normally without usable AI configuration");
                pet.OpenChat(); bool sent = await pet.SubmitChatAsync("你好");
                Check(!sent && pet.Visible && pet.ChatReply.Contains("API Key"), "Unconfigured chat shows actionable error while pet remains alive");
                pet.ChatInput.Close();
                // The expanded reply places the pet below the bubble, at y=270.
                Native.SendMessage(pet.Handle, 0x203, (IntPtr)1, XY(140, 270));
                Native.SendMessage(pet.Handle, 0x202, IntPtr.Zero, XY(140, 270));
                Check(pet.Pose == "jump", "Original double-click jump still works after a chat error");
                pet.Close();
            }
        }
        private sealed class TestPrompt : ISystemPromptSource { public string GetSystemPrompt() { return "injected-system-prompt"; } }
    }

    internal sealed class MockRequest
    {
        internal string Method, Path, Authorization, ContentType;
        internal Dictionary<string, object> Body;
    }
    internal sealed class MockServer : IDisposable
    {
        private sealed class Reply { internal int Status, Delay, BodyDelay; internal string Body; }
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly Queue<Reply> replies = new Queue<Reply>();
        private readonly List<MockRequest> requests = new List<MockRequest>();
        private readonly List<TcpClient> connections = new List<TcpClient>();
        private readonly object gate = new object();
        private bool disposed;
        internal volatile bool ResetConnections;
        internal string BaseUrl;
        internal List<MockRequest> Requests { get { lock (gate) return new List<MockRequest>(requests); } }
        internal MockServer()
        {
            listener.Start(); BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/v1";
            AcceptLoop();
        }
        internal void Enqueue(int status, string body, int delay = 0, int bodyDelay = 0)
        { lock (gate) replies.Enqueue(new Reply { Status = status, Body = body, Delay = delay, BodyDelay = bodyDelay }); }
        private async void AcceptLoop()
        {
            try { while (!disposed) { TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); lock (gate) connections.Add(client); Serve(client); } }
            catch (ObjectDisposedException) { }
            catch (SocketException) { if (!disposed) throw; }
        }
        private async void Serve(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    var header = new List<byte>(); byte[] one = new byte[1];
                    while (header.Count < 16000)
                    {
                        if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) == 0) return;
                        header.Add(one[0]); int n = header.Count;
                        if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
                    }
                    string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                    int length = 0; var request = new MockRequest { Method = lines[0].Split(' ')[0], Path = lines[0].Split(' ')[1], Authorization = "", ContentType = "" };
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = Int32.Parse(line.Substring(15).Trim());
                        if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) request.Authorization = line.Substring(14).Trim();
                        if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase)) request.ContentType = line.Substring(13).Trim();
                        if (line.StartsWith("Expect: 100-continue", StringComparison.OrdinalIgnoreCase))
                        {
                            byte[] interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"); await stream.WriteAsync(interim, 0, interim.Length).ConfigureAwait(false);
                        }
                    }
                    byte[] body = new byte[length]; int offset = 0;
                    while (offset < length) { int n = await stream.ReadAsync(body, offset, length - offset).ConfigureAwait(false); if (n == 0) return; offset += n; }
                    request.Body = length == 0 ? new Dictionary<string, object>() : (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(body));
                    Reply reply;
                    lock (gate) { requests.Add(request); reply = ResetConnections ? new Reply { Status = -1, Body = "" } : replies.Count > 0 ? replies.Dequeue() : new Reply { Status = 500, Body = "no mock response queued" }; }
                    if (reply.Status == -1) { client.Client.LingerState = new LingerOption(true, 0); return; }
                    await Task.Delay(reply.Delay).ConfigureAwait(false);
                    byte[] payload = Encoding.UTF8.GetBytes(reply.Body);
                    byte[] responseHeader = Encoding.ASCII.GetBytes("HTTP/1.1 " + reply.Status + " Test\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(responseHeader, 0, responseHeader.Length).ConfigureAwait(false);
                    await Task.Delay(reply.BodyDelay).ConfigureAwait(false);
                    await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally { lock (gate) connections.Remove(client); }
        }
        public void Dispose()
        {
            disposed = true; listener.Stop();
            lock (gate) foreach (TcpClient client in connections.ToArray()) client.Close();
        }
    }
}
