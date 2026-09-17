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
        private static string CopyTestPack(string prefix)
        {
            string source = Path.Combine(projectRoot, "tests", "fixtures", "demo");
            string destination = Path.Combine(output, prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, file.Substring(source.TrimEnd(Path.DirectorySeparatorChar).Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target);
            }
            return destination;
        }
        private static void SaveJson(string path, object data) { File.WriteAllText(path, json.Serialize(data), Encoding.UTF8); }
        private static void CharacterFails(Action action, string name)
        {
            try { action(); } catch (CharacterLoadException) { Check(true, name); return; }
            throw new Exception("Expected a CharacterLoadException: " + name);
        }
        private static async Task CharacterTests(MockServer server)
        {
            string folder = CopyTestPack("character-fixture");
            string manifest = Path.Combine(folder, "character.json"), personaFile = Path.Combine(folder, "persona.json");
            string originalManifest = File.ReadAllText(manifest), originalPersona = File.ReadAllText(personaFile);
            CharacterDefinition definition = json.Deserialize<CharacterDefinition>(originalManifest);
            Check(CharacterLoader.ResolveSelection(projectRoot, new[] { "--character", folder }) == folder, "Explicit --character selects an independent pack directory");
            using (CharacterPack pack = CharacterLoader.Load(folder))
            {
                Check(pack.Sprites.Count == 15 && pack.Persona.name == "小曜", "Loader reads character metadata, persona and all existing assets");
                string prompt = PersonaPromptBuilder.Build(pack.Persona);
                Check(prompt.Contains(pack.Persona.name) && prompt.Contains(pack.Persona.personality) && prompt.Contains(pack.Persona.speech_style) && prompt.Contains(pack.Persona.background) && pack.Persona.likes.All(prompt.Contains) && pack.Persona.dislikes.All(prompt.Contains), "Prompt builder includes all six persona fields");
            }
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(Path.Combine(folder, "missing"))) { } }, "Missing character.json produces a clear loader failure");
            definition.schema_version = 999; SaveJson(manifest, definition);
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Unknown character schema is rejected");
            definition = json.Deserialize<CharacterDefinition>(originalManifest);
            definition.sprites["normal"].region = new[] { 1200, 1200, 1000, 1000 }; SaveJson(manifest, definition);
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Crop outside source bounds is rejected before UI starts");
            definition = json.Deserialize<CharacterDefinition>(originalManifest); definition.sprite_sheet = "assets/missing.png"; SaveJson(manifest, definition);
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Missing artwork is reported by CharacterLoader");
            definition.sprite_sheet = "../outside.png"; SaveJson(manifest, definition);
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Pack-relative asset path cannot escape its directory");
            definition = json.Deserialize<CharacterDefinition>(originalManifest); definition.default_expression = "missing"; SaveJson(manifest, definition);
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Default expression must refer to an existing expression");
            definition = json.Deserialize<CharacterDefinition>(originalManifest); definition.animations["walk"].motion = "unknown"; SaveJson(manifest, definition);
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Unsupported animation motion is rejected");
            definition = json.Deserialize<CharacterDefinition>(originalManifest); definition.persona = "missing-persona.json"; SaveJson(manifest, definition);
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Missing persona file is reported at pack load");
            File.WriteAllText(manifest, originalManifest);
            File.WriteAllText(personaFile, "{bad json");
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Malformed persona JSON is rejected");
            File.WriteAllText(personaFile, "{\"name\":\"\"}");
            CharacterFails(() => { using (var ignored = CharacterLoader.Load(folder)) { } }, "Empty persona name is rejected");
            File.WriteAllText(personaFile, originalPersona);
            var extensible = (Dictionary<string, object>)json.DeserializeObject(originalPersona); extensible["future_extension"] = new { note = "ignored for now" }; SaveJson(personaFile, extensible);
            using (var pack = CharacterLoader.Load(folder)) Check(pack.Persona.name == "小曜", "Additional persona fields are tolerated for future extensions");
            File.WriteAllText(personaFile, originalPersona);
            definition = json.Deserialize<CharacterDefinition>(originalManifest);
            definition.id = "alternate_fixture"; definition.display_name = "角色包测试"; definition.default_size = 170; definition.size_options = new[] { 120, 170, 220 };
            definition.expressions["rest"] = "normal"; definition.default_expression = "rest";
            definition.animations["greeting"] = new AnimationDefinition { sprite = "wave", motion = "idle" }; definition.default_animation = "greeting";
            definition.messages["startup"] = "配置中的问候"; SaveJson(manifest, definition);
            using (Atlas atlas = new Atlas(CharacterLoader.Load(folder)))
            using (PetWindow pet = new PetWindow(atlas, true))
            {
                pet.Show(); Check(pet.Text.StartsWith("角色包测试") && pet.PetHeight == 170 && pet.Pose == "greeting", "Runner derives name, size and default animation from the selected pack");
                pet.Wander = false; pet.Until = 0; pet.Tick(2); Check(pet.Pose == "rest", "Idle state uses configured default expression");
                Check(atlas.Character.Message("startup") == "配置中的问候", "Interaction text comes from character.json");
                pet.Close();
            }
            // Minimal pack with a standalone transparent PNG: no sprite sheet or 15-pose requirement.
            string minimal = Path.Combine(folder, "minimal"); Directory.CreateDirectory(minimal);
            using (Bitmap bitmap = new Bitmap(12, 16, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); using (Brush brush = new SolidBrush(Color.FromArgb(128, 40, 80, 160))) g.FillRectangle(brush, 1, 1, 10, 14); }
                bitmap.SetPixel(1, 1, Color.Black); bitmap.Save(Path.Combine(minimal, "single.png"), ImageFormat.Png);
            }
            var minimalDefinition = new CharacterDefinition { schema_version = 1, id = "minimal", display_name = "最小测试角色", default_size = 120, size_options = new[] { 120 }, default_expression = "neutral", default_animation = "idle", persona = "persona.json", sprites = new Dictionary<string, SpriteDefinition> { { "frame", new SpriteDefinition { path = "single.png" } } }, expressions = new Dictionary<string, string> { { "neutral", "frame" } }, animations = new Dictionary<string, AnimationDefinition> { { "idle", new AnimationDefinition { sprite = "frame", motion = "idle" } } } };
            SaveJson(Path.Combine(minimal, "character.json"), minimalDefinition); File.WriteAllText(Path.Combine(minimal, "persona.json"), "{\"name\":\"最小测试角色\"}");
            using (Atlas atlas = new Atlas(CharacterLoader.Load(minimal)))
            using (PetWindow pet = new PetWindow(atlas, true))
            {
                Check(atlas.Sprites["frame"].GetPixel(0, 0).ToArgb() == Color.Black.ToArgb() && atlas.Sprites["frame"].GetPixel(1, 1).A == 128, "Transparent PNG loading preserves dark outlines and partial alpha when chroma removal is off");
                pet.Show(); pet.Act("happy", "", 1); pet.Act("jump", "", 1); pet.Render();
                Check(pet.Visible && atlas.Character.ResolveSprite("missing-optional-state") == "frame", "A one-sprite character runs with fallback for absent optional actions and icon"); pet.Close();
            }
            File.WriteAllText(manifest, originalManifest);
            string appConfig = Path.Combine(output, "character-api-fixture-" + Guid.NewGuid().ToString("N"));
            ModelSettingsStore.Save(appConfig, new ModelSettings { BaseUrl = server.BaseUrl, ApiKey = FakeKey, Model = FixtureModel, TimeoutSeconds = "3" });
            using (Atlas atlas = new Atlas(CharacterLoader.Load(folder)))
            using (PetWindow pet = new PetWindow(atlas, false, null, appConfig))
            {
                pet.Show(); pet.Wander = false;
                server.Enqueue(200, Answer("嘿，一起慢慢来呀。")); await pet.SubmitChatAsync("测试一句话");
                object[] first = Messages(server.Requests.Last().Body); string cheerfulPrompt = Content(first[0]);
                Check(cheerfulPrompt.Contains("嘿，") && cheerfulPrompt.Contains("小曜") && pet.ChatReply == "嘿，一起慢慢来呀。", "Production PetWindow sends current pack persona via ChatService and displays returned text");
                server.Enqueue(200, Answer("嘿，我记得呢。")); await pet.SubmitChatAsync("你刚才说什么？");
                Check(Messages(server.Requests.Last().Body).Length == 4, "Same persona retains the preceding user/assistant turn");
                File.Copy(Path.Combine(projectRoot, "tests", "fixtures", "personas", "formal.json"), personaFile, true);
                server.Enqueue(200, Answer("建议：先休息五分钟。")); await pet.SubmitChatAsync("测试一句话");
                object[] changed = Messages(server.Requests.Last().Body);
                Check(Content(changed[0]).Contains("建议：") && Content(changed[0]) != cheerfulPrompt && changed.Length == 2, "Persona edit is reloaded on next request and resets old-style history");
                Check(pet.ChatReply == "建议：先休息五分钟。", "Persona reply still renders in the existing bubble");
                int count = server.Requests.Count; File.WriteAllText(personaFile, "invalid");
                Check(!await pet.SubmitChatAsync("测试错误") && pet.ChatReply.Contains("人设读取失败") && server.Requests.Count == count && pet.Visible, "Invalid edited persona shows friendly error without network call or GUI crash");
                File.WriteAllText(personaFile, originalPersona);
                server.Enqueue(200, Answer("嘿，修好啦。")); Check(await pet.SubmitChatAsync("再试一次"), "Fixing persona permits retry without restarting the pet");
                pet.Close();
            }
        }
        private static async Task RunLivePersona()
        {
            try
            {
                ChatConfig config = ChatConfig.Load(projectRoot);
                string folder = CopyTestPack("live-persona-fixture");
                using (CharacterPack pack = CharacterLoader.Load(folder))
                {
                    var service = new ChatService(() => config, c => new OpenAiCompatibleProvider(c), new CharacterPersonaPromptSource(pack), new ChatLog());
                    string question = "今天学习有点累，给我一个小建议吧。请只回答两句话。";
                    string cheerful = await service.SendAsync(question, CancellationToken.None);
                    File.Copy(Path.Combine(projectRoot, "tests", "fixtures", "personas", "formal.json"), CharacterLoader.ResolveFile(pack.Folder, pack.Definition.persona), true);
                    string formal = await service.SendAsync(question, CancellationToken.None);
                    results.Add("真实 API 调用完成：同一配置、同一问题，两种 persona。未修改用户设置或原角色包。");
                    results.Add("轻快人设回复：" + cheerful);
                    results.Add("正式人设回复：" + formal);
                    bool distinct = cheerful.StartsWith("嘿，") && formal.StartsWith("建议：");
                    results.Add(distinct ? "PASS: 真实模型遵循两种不同的开头与说话风格。" : "REVIEW: 两次调用成功，但未同时满足预设开头；需人工判断语气，不能宣称人设对比通过。");
                    Check(service.HistoryCount == 2, "Live persona switch also resets the temporary conversation history");
                }
            }
            catch (ChatException error) { results.Add("未完成真实 API 验证：" + error.UserMessage + " [" + error.Diagnostic + "]"); }
            catch (Exception error) { new ChatLog().Write(error); results.Add("未完成真实 API 验证：" + error.GetType().Name + "，详情见本机日志。"); }
        }
    }
}
