using System;
using System.Collections.Generic;
using System.Drawing;

namespace DesktopPet.Characters
{
    internal sealed class CharacterDefinition
    {
        public int schema_version { get; set; }
        public string id { get; set; }
        public string display_name { get; set; }
        public int default_size { get; set; }
        public int[] size_options { get; set; }
        public string default_expression { get; set; }
        public string default_animation { get; set; }
        public string persona { get; set; }
        public string icon { get; set; }
        public string sprite_sheet { get; set; }
        public bool remove_dark_background { get; set; }
        public int background_threshold { get; set; }
        public Dictionary<string, SpriteDefinition> sprites { get; set; }
        public Dictionary<string, string> expressions { get; set; }
        public Dictionary<string, AnimationDefinition> animations { get; set; }
        public Dictionary<string, string> messages { get; set; }
        public CustomDialogue custom_dialogue { get; set; }
    }
    internal sealed class CustomDialogue
    {
        public Dictionary<string, string> messages { get; set; }
        public string[] idle { get; set; }
        internal static void Validate(CustomDialogue dialogue)
        {
            if (dialogue == null) return;
            if (dialogue.idle != null && dialogue.idle.Length > 20) throw new CharacterLoadException("自定义待机台词最多 20 句。");
            var lines = new List<string>();
            if (dialogue.messages != null) lines.AddRange(dialogue.messages.Values);
            if (dialogue.idle != null) lines.AddRange(dialogue.idle);
            foreach (string line in lines)
                if (line != null && (line.Length > 40 || line.Contains("\n") || line.Contains("\r"))) throw new CharacterLoadException("自定义台词每句最多 40 字符，单句不能换行。");
        }
    }
    internal sealed class SpriteDefinition
    {
        public string path { get; set; }
        public int[] region { get; set; }
        public bool? remove_dark_background { get; set; }
    }
    internal sealed class AnimationDefinition
    {
        public string sprite { get; set; }
        public string motion { get; set; }
    }
    internal sealed class PersonaDefinition
    {
        public string name { get; set; }
        public string personality { get; set; }
        public string speech_style { get; set; }
        public string background { get; set; }
        public string[] likes { get; set; }
        public string[] dislikes { get; set; }
    }
    internal sealed class CharacterPack : IDisposable
    {
        internal string Folder;
        internal CharacterDefinition Definition;
        internal PersonaDefinition Persona;
        internal readonly Dictionary<string, Bitmap> Sprites = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        internal string ResolveSprite(string state)
        {
            string sprite;
            AnimationDefinition animation;
            if (Definition.expressions.TryGetValue(state, out sprite)) return sprite;
            if (Definition.animations.TryGetValue(state, out animation)) return animation.sprite;
            if (Sprites.ContainsKey(state)) return state;
            return Definition.expressions[Definition.default_expression];
        }
        internal string MotionFor(string state)
        {
            AnimationDefinition animation;
            return Definition.animations.TryGetValue(state, out animation) ? animation.motion : "idle";
        }
        internal string Message(string key)
        {
            string text;
            return Definition.messages != null && Definition.messages.TryGetValue(key, out text) ? text ?? "" : "";
        }
        public void Dispose() { foreach (Bitmap image in Sprites.Values) image.Dispose(); Sprites.Clear(); }
    }
    internal sealed class CharacterLoadException : Exception
    {
        internal CharacterLoadException(string message, Exception inner = null) : base(message, inner) { }
    }
}
