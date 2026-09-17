using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using DesktopPet.Characters;

namespace DesktopPet.Chat
{
    internal sealed class AmbientLines
    {
        public string persona_hash { get; set; }
        public string generated_at { get; set; }
        public Dictionary<string, string> messages { get; set; }
        public string[] idle { get; set; }
    }
    internal static class AmbientDialogue
    {
        internal const string FileName = "ambient-lines.json";
        internal static readonly string[] Keys = { "startup", "pet", "wave", "jump", "run", "normal", "happy", "wink", "surprised", "sad", "angry", "wander_on", "wander_off", "rest_on", "rest_off", "recall", "drag", "drop", "released" };
        internal static readonly string[] Labels = { "启动 / 初次见面", "摸摸头", "挥手", "跳跃", "跑步", "普通表情", "开心", "眨眼", "惊讶", "难过", "生气", "开启散步", "停止散步", "开启安静陪伴", "结束安静陪伴", "找回桌宠", "拖拽", "放下", "松开拖拽" };
        internal static string Resolve(string key, CustomDialogue custom, AmbientLines generated, CharacterDefinition initial)
        {
            string text;
            if (custom != null && custom.messages != null && custom.messages.TryGetValue(key, out text) && !String.IsNullOrWhiteSpace(text)) return text;
            if (generated != null && generated.messages != null && generated.messages.TryGetValue(key, out text) && !String.IsNullOrWhiteSpace(text)) return text;
            if (initial != null && initial.messages != null && initial.messages.TryGetValue(key, out text) && !String.IsNullOrWhiteSpace(text)) return text;
            return Fallback(key);
        }
        internal static string[] ResolveIdle(CustomDialogue custom, AmbientLines generated, CharacterDefinition initial)
        {
            string[] lines = custom == null || custom.idle == null ? new string[0] : custom.idle.Where(s => !String.IsNullOrWhiteSpace(s)).ToArray();
            if (lines.Length > 0) return lines;
            if (generated != null && generated.idle != null && generated.idle.Length > 0) return generated.idle;
            string text;
            if (initial != null && initial.messages != null && initial.messages.TryGetValue("idle", out text) && !String.IsNullOrWhiteSpace(text)) return new[] { text };
            return new[] { Fallback("startup") };
        }
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, SemaphoreSlim> Locks = new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        internal static PersonaDefinition Persona(string folder)
        {
            using (var stub = new CharacterPack { Folder = folder }) return CharacterLoader.LoadCurrentPersona(stub);
        }
        internal static string Fingerprint(string folder) { return ChatArchive.Hash(PersonaPromptBuilder.Build(Persona(folder))); }
        internal static AmbientLines Read(string folder, string fingerprint)
        {
            try
            {
                string path = Path.Combine(folder, FileName); if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
                var lines = new JavaScriptSerializer().Deserialize<AmbientLines>(File.ReadAllText(path, Encoding.UTF8));
                if (lines == null || lines.persona_hash != fingerprint) return null;
                Validate(lines); return lines;
            }
            catch { return null; }
        }
        private static void Validate(AmbientLines lines)
        {
            if (lines.messages == null || Keys.Any(k => !lines.messages.ContainsKey(k)) || lines.idle == null || lines.idle.Length < 3 || lines.idle.Length > 8)
                throw new ChatException("短句格式不完整，请点击重新生成。", "ambient_invalid");
            foreach (string text in lines.messages.Values.Concat(lines.idle))
                if (String.IsNullOrWhiteSpace(text) || text.Length > 40 || text.Contains("\n") || text.Contains("\r"))
                    throw new ChatException("模型生成的短句过长或格式不正确，请重试。", "ambient_invalid");
        }
        internal static async Task<bool> GenerateAsync(string configDirectory, string characterFolder, bool force, CancellationToken cancellation)
        {
            SemaphoreSlim serial;
            lock (Gate) { if (!Locks.TryGetValue(characterFolder, out serial)) Locks[characterFolder] = serial = new SemaphoreSlim(1, 1); }
            await serial.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                return await Task.Run(async delegate
                {
                    var persona = Persona(characterFolder); string prompt = PersonaPromptBuilder.Build(persona), fingerprint = ChatArchive.Hash(prompt);
                    if (!force && Read(characterFolder, fingerprint) != null) return false;
                    ChatConfig config = ChatConfig.Load(configDirectory);
                    var messages = new List<ChatMessage> {
                        new ChatMessage("system", "你是桌宠台词编辑器。为下面的角色制作离线台词，角色要求适用于台词内容，不改变JSON结构。只输出合法JSON。\n" + prompt),
                        new ChatMessage("user", "请按当前人设生成可离线复用的桌宠短句。只输出JSON对象，结构为{\"messages\":{...},\"idle\":[...]}。messages必须包含以下全部键：" + String.Join(",", Keys) + "。键分别表示启动、摸头、挥手、跳跃、跑步、普通、开心、眨眼、惊讶、难过、生气、散步开关、安静开关、召回、拖拽、放下、松开。idle为4句待机陪伴短句。每句1至30个汉字，总长不超过40字符，不换行；必须体现角色身份和说话方式，不提API或设定，不假装看见用户当前活动。")
                    };
                    string response;
                    using (var provider = new OpenAiCompatibleProvider(config)) response = await provider.CompleteAsync(messages, cancellation).ConfigureAwait(false);
                    string text = response.Trim();
                    if (text.StartsWith("```")) { int firstLine = text.IndexOf('\n'); int last = text.LastIndexOf("```", StringComparison.Ordinal); if (firstLine >= 0 && last > firstLine) text = text.Substring(firstLine + 1, last - firstLine - 1).Trim(); }
                    AmbientLines lines;
                    try { lines = new JavaScriptSerializer { MaxJsonLength = 65536 }.Deserialize<AmbientLines>(text); if (lines == null) throw new InvalidDataException(); }
                    catch (Exception error) { throw new ChatException("短句不是有效JSON，请点击重新生成。", "ambient_invalid", error); }
                    Validate(lines); lines.persona_hash = fingerprint; lines.generated_at = DateTime.UtcNow.ToString("o");
                    cancellation.ThrowIfCancellationRequested();
                    if (Fingerprint(characterFolder) != fingerprint) throw new OperationCanceledException("Persona changed during generation.");
                    CharacterLibrary.WriteJson(Path.Combine(characterFolder, FileName), lines);
                    return true;
                }, cancellation).ConfigureAwait(false);
            }
            finally { serial.Release(); }
        }
        internal static string Fallback(string key)
        {
            switch (key)
            {
                case "startup": case "recall": return "我在这里陪你。";
                case "pet": return "收到你的摸摸啦。";
                case "drag": return "我们换个位置。";
                case "drop": case "released": return "就在这里陪你。";
                case "rest_on": return "我会安静陪着你。";
                case "jump": return "跳一下！";
                default: return "我在呢。";
            }
        }
    }
}
