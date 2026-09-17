using System;
using System.Text;
using DesktopPet.Characters;

namespace DesktopPet.Chat
{
    internal static class PersonaPromptBuilder
    {
        internal static string Build(PersonaDefinition persona)
        {
            if (persona == null) throw new ArgumentNullException("persona");
            var prompt = new StringBuilder();
            prompt.AppendLine("扮演以下桌面伙伴，遵守当前身份、性格和说话方式，用中文简短作答。不沿用旧设定，不编造现实能力。");
            Add(prompt, "名字", persona.name); Add(prompt, "性格", persona.personality);
            Add(prompt, "说话方式", persona.speech_style); Add(prompt, "背景", persona.background);
            if (persona.likes != null && persona.likes.Length > 0) Add(prompt, "喜欢", String.Join("、", persona.likes));
            if (persona.dislikes != null && persona.dislikes.Length > 0) Add(prompt, "不喜欢", String.Join("、", persona.dislikes));
            return prompt.ToString().TrimEnd();
        }
        private static void Add(StringBuilder prompt, string label, string value)
        { if (!String.IsNullOrWhiteSpace(value)) prompt.AppendLine(label + "：" + value.Trim()); }
    }
    internal sealed class CharacterPersonaPromptSource : ISystemPromptSource
    {
        private readonly CharacterPack character;
        internal CharacterPersonaPromptSource(CharacterPack character) { this.character = character; }
        public string GetSystemPrompt()
        {
            try { return PersonaPromptBuilder.Build(CharacterLoader.LoadCurrentPersona(character)); }
            catch (Exception error) { throw new ChatException("角色人设读取失败，请检查当前角色配置中 persona 指向的人设文件。", "persona_load_failed", error); }
        }
    }
}
