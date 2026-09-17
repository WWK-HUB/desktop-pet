using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DesktopPet.Characters
{
    internal static class CharacterLoader
    {
        private sealed class RunnerSettings
        {
            public int schema_version { get; set; }
            public string character { get; set; }
        }
        internal static string ResolveSelection(string appDirectory, string[] arguments)
        {
            for (int i = 0; i < arguments.Length; i++)
                if (arguments[i] == "--character")
                {
                    if (i + 1 >= arguments.Length || arguments[i + 1].StartsWith("--")) throw new CharacterLoadException("--character 后需要填写角色目录。");
                    return Path.GetFullPath(Path.Combine(appDirectory, arguments[i + 1]));
                }
            RunnerSettings runner = ReadJson<RunnerSettings>(Path.Combine(appDirectory, "runner.json"));
            if (runner == null || runner.schema_version != 1 || String.IsNullOrWhiteSpace(runner.character)) throw new CharacterLoadException("runner.json 需要 schema_version: 1 和 character 角色目录。");
            return Path.GetFullPath(Path.Combine(appDirectory, runner.character));
        }
        internal static CharacterPack LoadSelected(string appDirectory)
        { return Load(ResolveSelection(appDirectory, new string[0])); }

        internal static CharacterPack Load(string folder)
        {
            CharacterPack pack = new CharacterPack { Folder = Path.GetFullPath(folder) };
            try
            {
                pack.Definition = ReadJson<CharacterDefinition>(Path.Combine(pack.Folder, "character.json"));
                Validate(pack.Definition);
                pack.Persona = LoadPersona(pack);
                LoadSprites(pack);
                return pack;
            }
            catch (CharacterLoadException) { pack.Dispose(); throw; }
            catch (Exception error) { pack.Dispose(); throw new CharacterLoadException("角色包加载失败，请检查角色目录及素材格式：" + pack.Folder, error); }
        }
        internal static PersonaDefinition LoadPersona(CharacterPack pack)
        {
            PersonaDefinition persona = ReadJson<PersonaDefinition>(ResolveFile(pack.Folder, pack.Definition.persona));
            ValidatePersona(persona);
            return persona;
        }
        internal static PersonaDefinition LoadCurrentPersona(CharacterPack pack)
        {
            var definition = ReadJson<CharacterDefinition>(Path.Combine(pack.Folder, "character.json"));
            var persona = ReadJson<PersonaDefinition>(ResolveFile(pack.Folder, definition.persona));
            ValidatePersona(persona); return persona;
        }
        internal static CharacterDefinition LoadCurrentDefinition(string folder)
        {
            var definition = ReadJson<CharacterDefinition>(Path.Combine(folder, "character.json"));
            if (definition == null) throw new CharacterLoadException("角色配置不能为空。");
            CustomDialogue.Validate(definition.custom_dialogue); return definition;
        }
        internal static void ValidatePersona(PersonaDefinition persona)
        {
            if (persona == null || String.IsNullOrWhiteSpace(persona.name) || persona.name.Length > 64) throw new CharacterLoadException("persona.json 的 name 必须为 1～64 字符。");
            ValidateText(persona.personality, 2000, "personality"); ValidateText(persona.speech_style, 2000, "speech_style"); ValidateText(persona.background, 4000, "background");
            ValidateList(persona.likes, "likes"); ValidateList(persona.dislikes, "dislikes");
        }
        private static void ValidateText(string text, int max, string field)
        { if (text != null && text.Length > max) throw new CharacterLoadException("persona.json 的 " + field + " 太长。"); }
        private static void ValidateList(string[] items, string field)
        {
            if (items == null) return;
            if (items.Length > 30) throw new CharacterLoadException("persona.json 的 " + field + " 最多 30 项。");
            foreach (string item in items) if (item == null || item.Length > 128) throw new CharacterLoadException("persona.json 的 " + field + " 每项应为不超过 128 字符的字符串。");
        }
        private static T ReadJson<T>(string path)
        {
            if (!File.Exists(path)) throw new CharacterLoadException("缺少必要文件：" + path);
            if (new FileInfo(path).Length > 262144) throw new CharacterLoadException("角色 JSON 文件过大：" + Path.GetFileName(path));
            try { return new JavaScriptSerializer { MaxJsonLength = 262144, RecursionLimit = 32 }.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception error) { throw new CharacterLoadException("JSON 格式或字段类型不正确：" + Path.GetFileName(path), error); }
        }
        internal static string ResolveFile(string folder, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new CharacterLoadException("角色包内的素材和人设路径必须是相对路径。");
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new CharacterLoadException("角色素材路径不能超出角色包目录。");
            if (!File.Exists(path)) throw new CharacterLoadException("角色包缺少文件：" + relative);
            for (string check = path; !String.Equals(check, root, StringComparison.OrdinalIgnoreCase); check = Path.GetDirectoryName(check))
                if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new CharacterLoadException("角色包内不支持符号链接或目录联接：" + relative);
            return path;
        }
        internal static void Validate(CharacterDefinition d)
        {
            if (d == null || d.schema_version != 1) throw new CharacterLoadException("character.json 的 schema_version 必须为 1。");
            if (d.id == null || !Regex.IsMatch(d.id, "^[A-Za-z0-9_-]{1,64}$")) throw new CharacterLoadException("角色 id 应为 1～64 位字母、数字、下划线或短横线。");
            if (String.IsNullOrWhiteSpace(d.display_name) || d.display_name.Length > 30) throw new CharacterLoadException("角色 display_name 应为 1～30 字符。");
            if (d.default_size < 80 || d.default_size > 300) throw new CharacterLoadException("default_size 必须在 80～300 像素之间。");
            if (d.size_options == null || d.size_options.Length == 0 || d.size_options.Length > 6 || Array.IndexOf(d.size_options, d.default_size) < 0) throw new CharacterLoadException("size_options 需包含默认尺寸，最多 6 项。");
            var sizes = new HashSet<int>(); foreach (int size in d.size_options) if (size < 80 || size > 300 || !sizes.Add(size)) throw new CharacterLoadException("size_options 应为 80～300 范围内的不重复尺寸。");
            if (d.sprites == null || d.sprites.Count == 0 || d.sprites.Count > 64) throw new CharacterLoadException("sprites 需要包含 1～64 张素材定义。");
            if (d.background_threshold < 0 || d.background_threshold > 255) throw new CharacterLoadException("background_threshold 应在 0～255 之间。");
            if (d.expressions == null || String.IsNullOrWhiteSpace(d.default_expression) || !d.expressions.ContainsKey(d.default_expression)) throw new CharacterLoadException("default_expression 必须指向 expressions 中的一个表情。");
            if (d.animations == null || String.IsNullOrWhiteSpace(d.default_animation) || !d.animations.ContainsKey(d.default_animation)) throw new CharacterLoadException("default_animation 必须指向 animations 中的一个动作。");
            foreach (var sprite in d.sprites)
                if (String.IsNullOrWhiteSpace(sprite.Key) || sprite.Value == null) throw new CharacterLoadException("sprites 的名称和定义不能为空。");
            foreach (var expression in d.expressions) ValidateReference(d, expression.Value);
            foreach (var animation in d.animations)
            {
                if (d.expressions.ContainsKey(animation.Key)) throw new CharacterLoadException("表情和动作名称不能重复：" + animation.Key);
                if (animation.Value == null) throw new CharacterLoadException("动作定义不能为空。");
                ValidateReference(d, animation.Value.sprite);
                if (Array.IndexOf(new[] { "idle", "walk", "run", "jump" }, animation.Value.motion) < 0) throw new CharacterLoadException("motion 只支持 idle、walk、run、jump。");
            }
            if (!String.IsNullOrEmpty(d.icon)) ValidateReference(d, d.icon);
            if (d.messages != null) foreach (string text in d.messages.Values) if (text != null && text.Length > 40) throw new CharacterLoadException("固定互动短句每条最多 40 字符。");
            CustomDialogue.Validate(d.custom_dialogue);
        }
        private static void ValidateReference(CharacterDefinition d, string key)
        { if (key == null || !d.sprites.ContainsKey(key)) throw new CharacterLoadException("角色配置引用了不存在的素材：" + key); }
        private static void LoadSprites(CharacterPack pack)
        {
            var sources = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var item in pack.Definition.sprites)
                {
                    SpriteDefinition sprite = item.Value;
                    string file = ResolveFile(pack.Folder, String.IsNullOrWhiteSpace(sprite.path) ? pack.Definition.sprite_sheet : sprite.path);
                    Bitmap sheet;
                    if (!sources.TryGetValue(file, out sheet))
                    {
                        if (new FileInfo(file).Length > 16777216) throw new CharacterLoadException("素材文件最大 16 MB。");
                        sheet = new Bitmap(file); sources.Add(file, sheet);
                        if (sheet.Width > 8192 || sheet.Height > 8192 || (long)sheet.Width * sheet.Height > 16777216) throw new CharacterLoadException("素材图片尺寸过大。");
                    }
                    Rectangle region = new Rectangle(0, 0, sheet.Width, sheet.Height);
                    if (sprite.region != null)
                    {
                        int[] r = sprite.region;
                        if (r.Length != 4 || r[0] < 0 || r[1] < 0 || r[2] <= 0 || r[3] <= 0 || (long)r[0] + r[2] > sheet.Width || (long)r[1] + r[3] > sheet.Height) throw new CharacterLoadException("素材裁切区域超出图片范围：" + item.Key);
                        region = new Rectangle(r[0], r[1], r[2], r[3]);
                    }
                    pack.Sprites.Add(item.Key, Extract(sheet, region, sprite.remove_dark_background ?? pack.Definition.remove_dark_background, pack.Definition.background_threshold, item.Key));
                }
            }
            finally { foreach (Bitmap sheet in sources.Values) sheet.Dispose(); }
        }
        private static Bitmap Extract(Bitmap sheet, Rectangle r, bool removeDark, int threshold, string name)
        {
            using (Bitmap cut = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                int left = r.Width, top = r.Height, right = -1, bottom = -1;
                for (int y = 0; y < r.Height; y++) for (int x = 0; x < r.Width; x++)
                {
                    Color pixel = sheet.GetPixel(r.X + x, r.Y + y);
                    if (pixel.A == 0 || (removeDark && Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) < threshold)) continue;
                    cut.SetPixel(x, y, pixel); left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
                if (right < 0) throw new CharacterLoadException("素材裁切后完全透明：" + name);
                return cut.Clone(new Rectangle(left, top, right - left + 1, bottom - top + 1), PixelFormat.Format32bppArgb);
            }
        }
    }
}
