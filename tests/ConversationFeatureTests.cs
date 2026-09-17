using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopPet.Chat;
using DesktopPet.Characters;

namespace DesktopPet
{
    internal static partial class ChatTests
    {
        private sealed class MutablePersona : ISystemPromptSource
        {
            internal string Value = "旧角色：活泼的小猫";
            public string GetSystemPrompt() { return Value; }
        }
        private static string AmbientAnswer(string prefix)
        {
            return Answer(json.Serialize(new AmbientLines {
                messages = AmbientDialogue.Keys.ToDictionary(k => k, k => prefix + "：陪着你。"),
                idle = new[] { prefix + "：慢慢来。", prefix + "：休息一下。", prefix + "：我在这里。", prefix + "：今天也陪着你。" }
            }));
        }
        private static async Task ConversationFeatureTests(MockServer server, string workspace)
        {
            string config = Path.Combine(workspace, "conversation"); Directory.CreateDirectory(config);
            ModelSettingsStore.Save(config, new ModelSettings { BaseUrl = server.BaseUrl, ApiKey = FakeKey, Model = FixtureModel, TimeoutSeconds = "3" });
            var prompt = new MutablePersona();
            var service = new ChatService(() => ChatConfig.Load(config), c => new OpenAiCompatibleProvider(c), prompt, new ChatLog()) { ContextTurns = 2, ContextCharacterLimit = 6000 };
            int requests = server.Requests.Count;
            server.Enqueue(200, Answer("旧猫咪语气"), 350); Task<string> pending = service.SendAsync("你好", CancellationToken.None);
            await WaitUntil(() => server.Requests.Count > requests); prompt.Value = "新角色：严肃的图书管理员";
            await Fails(() => pending, "persona_changed");
            Check(service.HistoryCount == 0, "Persona changes during HTTP discard the stale reply before committing history");
            for (int i = 0; i < 4; i++) { server.Enqueue(200, Answer("新角色回复")); await service.SendAsync("消息" + i, CancellationToken.None); }
            Check(Messages(server.Requests.Last().Body).Length == 6 && Content(Messages(server.Requests.Last().Body)[0]).Contains("图书管理员"), "Every request uses current persona and sends at most selected two context turns");
            service.ContextTurns = 0; server.Enqueue(200, Answer("独立回复")); await service.SendAsync("不带历史", CancellationToken.None);
            Check(Messages(server.Requests.Last().Body).Length == 2, "Zero-history mode still sends persona and only the current user message");

            string role = CopyTestPack("conversation-role");
            string archivePath;
            using (var atlas = new Atlas(CharacterLoader.Load(role)))
            using (var pet = new PetWindow(atlas, false, null, config))
            {
                pet.Show(); pet.Wander = false; pet.OpenChat();
                server.Enqueue(200, Answer("你好，我来陪你。")); await pet.SubmitChatAsync("第一条历史消息");
                server.Enqueue(200, Answer("这是第二条回复。")); await pet.SubmitChatAsync("第二条历史消息");
                Check(pet.ChatInput.Transcript.Text.Contains("第一条历史消息") && pet.ChatInput.Transcript.Text.Contains("你好，我来陪你。") && pet.ChatInput.Transcript.Text.Contains("第二条历史消息") && pet.ChatInput.Transcript.Text.Contains("我 ·"), "Chat window displays both participants across earlier and current turns");
                archivePath = pet.Archive.FilePath;
                pet.ChatInput.ContextTurns.SelectedIndex = 0;
                Check(pet.Archive.ReadContextTurns() == 0, "Context preference saves independently of the transcript");
                pet.ChatInput.Close(); pet.OpenChat();
                Check(pet.ChatInput.Transcript.Text.Contains("第一条历史消息"), "Closing and reopening chat retains the visible conversation");
                pet.ChatInput.TopMost = true; pet.ChatInput.Location = new Point(40, 40); pet.ChatInput.Activate(); await Task.Delay(150);
                CaptureLauncher(pet.ChatInput, "chat-history-window.png"); pet.Close();
            }
            using (var atlas = new Atlas(CharacterLoader.Load(role)))
            using (var pet = new PetWindow(atlas, false, null, config))
            {
                pet.Show(); pet.OpenChat();
                Check(pet.ChatInput.Transcript.Text.Contains("第一条历史消息") && pet.Archive.Read(80).Count == 2, "Conversation archive survives destruction and recreation of the pet");
                server.Enqueue(200, Answer("重启后的回复")); await pet.SubmitChatAsync("新会话");
                Check(Messages(server.Requests.Last().Body).Length == 2, "Persisted display history is never replayed wholesale to the model");
                pet.Close();
            }
            Check(new ChatArchive(config, role + "-different").Read(80).Count == 0, "Conversation histories are isolated between character folders");
            File.AppendAllText(archivePath, "broken incomplete record\n", Encoding.UTF8);
            Check(new ChatArchive(config, role).Read(80).Count == 3, "A damaged archive line does not hide valid past turns");

            requests = server.Requests.Count;
            server.Enqueue(200, AmbientAnswer("旧台词")); await AmbientDialogue.GenerateAsync(config, role, false, CancellationToken.None);
            string initialHash = AmbientDialogue.Fingerprint(role);
            Check(AmbientDialogue.Read(role, initialHash).idle.Length == 4, "One model request creates validated idle and interaction lines on disk");
            await AmbientDialogue.GenerateAsync(config, role, false, CancellationToken.None);
            Check(server.Requests.Count == requests + 1, "Matching persona cache is reused without another model request");
            using (var pack = CharacterLoader.Load(role)) CharacterLibrary.Update(pack, pack.Definition.display_name, new Dictionary<string, Bitmap>(), pack.Persona);
            await AmbientDialogue.GenerateAsync(config, role, false, CancellationToken.None);
            Check(server.Requests.Count == requests + 1, "Saving an unchanged persona or changing only its file reference does not regenerate lines");
            requests = server.Requests.Count;
            server.Enqueue(200, AmbientAnswer("应作废"), 1400); Task<bool> stale = AmbientDialogue.GenerateAsync(config, role, true, CancellationToken.None);
            await WaitUntil(() => server.Requests.Count > requests);
            using (var pack = CharacterLoader.Load(role)) { pack.Persona.speech_style = "严肃、简洁，以建议开头。"; CharacterLibrary.SavePersona(pack, pack.Persona); }
            bool discarded = false; try { await stale; } catch (OperationCanceledException) { discarded = true; }
            Check(discarded && AmbientDialogue.Read(role, AmbientDialogue.Fingerprint(role)) == null, "Persona change invalidates old lines and rejects a generation started with the old persona");
            server.Enqueue(200, Answer("not json"));
            try { await AmbientDialogue.GenerateAsync(config, role, false, CancellationToken.None); throw new Exception("Expected invalid ambient response"); } catch (ChatException error) { Check(error.Diagnostic == "ambient_invalid", "Invalid generated lines are rejected rather than cached"); }

            using (var launcher = new LauncherForm(config, role, true))
            {
                launcher.Show(); requests = server.Requests.Count;
                server.Enqueue(200, AmbientAnswer("建议")); launcher.StartSelectedPet();
                await WaitUntil(() => AmbientDialogue.Read(role, AmbientDialogue.Fingerprint(role)) != null);
                await WaitUntil(() => launcher.GeneratePhrases.Enabled);
                var pet = launcher.RunningPet; pet.Wander = false;
                pet.PetMenu.Items.Cast<ToolStripItem>().First(i => i.Text == "摸摸头").PerformClick();
                Check(pet.CurrentSpeech.StartsWith("建议"), "Generated interaction lines reach the already-running pet and menu callbacks");
                pet.Tick(60); Check(pet.CurrentSpeech.StartsWith("建议"), "Idle speech reuses cached persona lines without invoking the model");
                Check(server.Requests.Count == requests + 1, "Repeated interactions and idle speech consume no further model calls");
                pet.OpenChat(); requests = server.Requests.Count;
                server.Enqueue(200, Answer("这条旧口吻应该被取消"), 600); Task<bool> oldChat = pet.SubmitChatAsync("等会儿再答");
                await WaitUntil(() => server.Requests.Count > requests);
                launcher.ShowLauncher(); launcher.EditButton.PerformClick();
                launcher.EditorPage.SpeechStyle.Text = "轻快，以嘿开头。";
                server.Enqueue(200, AmbientAnswer("嘿")); launcher.EditorPage.CreateButton.PerformClick();
                Check(!await oldChat && !pet.ChatReply.Contains("这条旧口吻"), "Saving current role immediately cancels the pending old-persona chat");
                await WaitUntil(() => AmbientDialogue.Read(role, AmbientDialogue.Fingerprint(role)) != null);
                await WaitUntil(() => launcher.GeneratePhrases.Enabled);
                pet.PetMenu.Items.Cast<ToolStripItem>().First(i => i.Text == "摸摸头").PerformClick();
                Check(pet.CurrentSpeech.StartsWith("嘿"), "Persona edit automatically regenerates and immediately replaces cached interaction voice");
                server.Enqueue(200, Answer("嘿，新的设定已生效。")); await pet.SubmitChatAsync("你好");
                Check(Messages(server.Requests.Last().Body).Length == 2 && Content(Messages(server.Requests.Last().Body)[0]).Contains("轻快，以嘿开头"), "First chat after edit uses new persona and no old context");
                Check(pet.Archive.Read(80).Any(t => t.status == "已取消" || t.status == "人设已更新") && pet.ChatInput.Transcript.Text.Contains("等会儿再答"), "Cancelled or invalidated old-persona requests remain explicitly marked in the conversation archive");
                int beforeUnchangedSave = server.Requests.Count;
                launcher.EditButton.PerformClick(); launcher.EditorPage.CreateButton.PerformClick();
                await WaitUntil(() => launcher.GeneratePhrases.Enabled);
                Check(server.Requests.Count == beforeUnchangedSave, "Unchanged editor saves do not charge for another generation");
                launcher.Close();
            }
        }
    }
}
