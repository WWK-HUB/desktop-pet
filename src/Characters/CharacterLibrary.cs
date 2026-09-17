using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace DesktopPet.Characters
{
    internal sealed class CharacterEntry
    {
        internal string Folder, Name;
        public override string ToString() { return Name; }
    }

    internal static class CharacterLibrary
    {
        internal static readonly string[] ImageStates = { "normal", "happy", "wink", "surprised", "sad", "angry", "walk", "run", "jump", "wave" };
        internal static readonly string[] ImageLabels = { "主图 / 默认站姿", "开心", "眨眼", "惊讶", "难过", "生气", "走路", "跑步", "跳跃", "挥手" };
        internal static void WriteJson(string path, object value)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static List<CharacterEntry> List(string appDirectory, string selection, out List<string> errors)
        {
            errors = new List<string>();
            var folders = new List<string>();
            string root = Path.Combine(appDirectory, "characters");
            if (Directory.Exists(root)) folders.AddRange(Directory.GetDirectories(root));
            if (!String.IsNullOrEmpty(selection) && !folders.Exists(p => String.Equals(p, selection, StringComparison.OrdinalIgnoreCase))) folders.Insert(0, selection);
            var entries = new List<CharacterEntry>();
            foreach (string folder in folders)
            {
                if (Path.GetFileName(folder).StartsWith(".import-")) continue;
                try
                {
                    using (CharacterPack pack = CharacterLoader.Load(folder))
                        entries.Add(new CharacterEntry { Folder = pack.Folder, Name = pack.Definition.display_name });
                }
                catch (Exception error) { errors.Add(Path.GetFileName(folder) + "：" + error.Message); }
            }
            return entries;
        }

        internal static void SaveSelection(string appDirectory, string folder)
        {
            string root = Path.GetFullPath(appDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string absolute = Path.GetFullPath(folder);
            string selected = absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? absolute.Substring(root.Length).Replace('\\', '/') : absolute;
            WriteJson(Path.Combine(appDirectory, "runner.json"), new { schema_version = 1, character = selected });
        }

        internal static void SavePersona(CharacterPack pack, PersonaDefinition persona)
        {
            CharacterLoader.ValidatePersona(persona);
            WriteJson(CharacterLoader.ResolveFile(pack.Folder, pack.Definition.persona), persona);
            pack.Persona = persona;
        }

        // Import is local: decode, optionally remove a connected flat background, and copy a normalized PNG.
        internal static Bitmap PrepareImage(string file, bool removeBackground)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (Array.IndexOf(new[] { ".png", ".jpg", ".jpeg", ".bmp" }, extension) < 0)
                throw new CharacterLoadException("请选择 PNG、JPG 或 BMP 图片。");
            if (new FileInfo(file).Length > 16777216) throw new CharacterLoadException("图片不能超过 16 MB。");
            using (var source = new Bitmap(file))
            {
                if (source.Width > 8192 || source.Height > 8192 || (long)source.Width * source.Height > 16777216)
                    throw new CharacterLoadException("图片最大边长为 8192，且总像素不能超过 1600 万。");
                double scale = Math.Min(1.0, 512.0 / Math.Max(source.Width, source.Height));
                var normalized = new Bitmap(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)), PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics g = Graphics.FromImage(normalized))
                    using (var attributes = new ImageAttributes())
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(source, new Rectangle(0, 0, normalized.Width, normalized.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                    }
                    if (removeBackground) RemoveBackground(normalized);
                    bool visible = false;
                    for (int y = 0; y < normalized.Height && !visible; y++)
                        for (int x = 0; x < normalized.Width; x++) if (normalized.GetPixel(x, y).A > 0) { visible = true; break; }
                    if (!visible) throw new CharacterLoadException("处理后图片完全透明，请关闭去背景或换一张图片。");
                    return normalized;
                }
                catch { normalized.Dispose(); throw; }
            }
        }

        private static void RemoveBackground(Bitmap bitmap)
        {
            int width = bitmap.Width, height = bitmap.Height;
            Color background = bitmap.GetPixel(0, 0);
            // Transparent PNGs already carry their own mask; never erase their dark outlines.
            if (background.A < 16) return;
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[data.Stride * height]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                var visited = new bool[width * height]; var queue = new Queue<int>();
                Action<int, int> add = (x, y) =>
                {
                    if (x < 0 || y < 0 || x >= width || y >= height) return;
                    int index = y * width + x, offset = y * data.Stride + x * 4;
                    if (visited[index]) return; visited[index] = true;
                    if (pixels[offset + 3] == 0 || (Math.Abs(pixels[offset] - background.B) <= 28 && Math.Abs(pixels[offset + 1] - background.G) <= 28 && Math.Abs(pixels[offset + 2] - background.R) <= 28))
                        queue.Enqueue(index);
                };
                for (int x = 0; x < width; x++) { add(x, 0); add(x, height - 1); }
                for (int y = 0; y < height; y++) { add(0, y); add(width - 1, y); }
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue(), x = index % width, y = index / width;
                    pixels[y * data.Stride + x * 4 + 3] = 0;
                    add(x - 1, y); add(x + 1, y); add(x, y - 1); add(x, y + 1);
                }
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally { bitmap.UnlockBits(data); }
        }

        internal static string Import(string appDirectory, string name, Bitmap image)
        { return Import(appDirectory, name, new Dictionary<string, Bitmap> { { "normal", image } }, null); }

        // Stage immutable files, then atomically switch the manifest. Existing art is never overwritten.
        internal static void Update(CharacterPack original, string name, IDictionary<string, Bitmap> changes, PersonaDefinition persona, CustomDialogue dialogue = null)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 30) throw new CharacterLoadException("角色名称应为 1～30 字符。");
            CharacterLoader.ValidatePersona(persona);
            CustomDialogue.Validate(dialogue);
            foreach (var item in changes)
                if (Array.IndexOf(ImageStates, item.Key) < 0 || (item.Key == "normal" && item.Value == null)) throw new CharacterLoadException("主图不能为空，图片状态必须有效。");
            var json = new JavaScriptSerializer();
            string manifest = Path.Combine(original.Folder, "character.json");
            var document = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest, Encoding.UTF8));
            var d = json.Deserialize<CharacterDefinition>(json.Serialize(original.Definition));
            string token = Guid.NewGuid().ToString("N"), oldMain = original.ResolveSprite(original.Definition.default_expression);
            var written = new List<string>(); var replacements = new Dictionary<string, string>();
            bool committed = false;
            try
            {
                foreach (var item in changes)
                {
                    if (item.Value == null) continue;
                    bool visible = false;
                    for (int y = 0; y < item.Value.Height && !visible; y++) for (int x = 0; x < item.Value.Width; x++) if (item.Value.GetPixel(x, y).A > 0) { visible = true; break; }
                    if (!visible) throw new CharacterLoadException("图片完全透明：" + item.Key);
                    string key = "user_" + token + "_" + item.Key, file = key + ".png", path = Path.Combine(original.Folder, file);
                    written.Add(path); item.Value.Save(path, ImageFormat.Png);
                    replacements[item.Key] = key; d.sprites[key] = new SpriteDefinition { path = file, remove_dark_background = false };
                }
                string main = replacements.ContainsKey("normal") ? replacements["normal"] : oldMain;
                foreach (string state in ImageStates)
                {
                    string before = original.ResolveSprite(state);
                    string sprite = replacements.ContainsKey(state) ? replacements[state] : changes.ContainsKey(state) || before == oldMain ? main : before;
                    if (Array.IndexOf(new[] { "walk", "run", "jump", "wave" }, state) >= 0)
                        d.animations[state] = new AnimationDefinition { sprite = sprite, motion = state == "wave" ? "idle" : state };
                    else d.expressions[state] = sprite;
                }
                d.expressions[d.default_expression] = main;
                if (d.animations.ContainsKey("idle")) d.animations["idle"].sprite = main;
                if (changes.ContainsKey("normal")) d.icon = main;
                var used = new HashSet<string>(d.expressions.Values);
                foreach (var animation in d.animations.Values) used.Add(animation.sprite);
                if (!String.IsNullOrEmpty(d.icon)) used.Add(d.icon);
                foreach (string key in new List<string>(d.sprites.Keys))
                    if (System.Text.RegularExpressions.Regex.IsMatch(key, "^user_[0-9a-f]{32}_(normal|happy|wink|surprised|sad|angry|walk|run|jump|wave)$") && !used.Contains(key)) d.sprites.Remove(key);
                d.display_name = name; d.persona = "persona_" + token + ".json";
                CharacterLoader.Validate(d);
                var personaDocument = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(CharacterLoader.ResolveFile(original.Folder, original.Definition.persona), Encoding.UTF8));
                foreach (var field in json.Deserialize<Dictionary<string, object>>(json.Serialize(persona))) personaDocument[field.Key] = field.Value;
                string personaPath = Path.Combine(original.Folder, d.persona); written.Add(personaPath); WriteJson(personaPath, personaDocument);
                document["display_name"] = d.display_name; document["persona"] = d.persona; document["sprites"] = d.sprites;
                document["expressions"] = d.expressions; document["animations"] = d.animations; document["icon"] = d.icon;
                if (dialogue != null) document["custom_dialogue"] = dialogue;
                WriteJson(manifest, document); committed = true;
            }
            finally { if (!committed) foreach (string path in written) if (File.Exists(path)) File.Delete(path); }
        }

        internal static string Import(string appDirectory, string name, IDictionary<string, Bitmap> images, PersonaDefinition persona, CustomDialogue dialogue = null)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 30) throw new CharacterLoadException("皮肤名称应为 1～30 字符。");
            if (images == null || !images.ContainsKey("normal") || images["normal"] == null) throw new CharacterLoadException("请上传主图，主图是桌宠正常站立时的样子。");
            foreach (var item in images)
                if (Array.IndexOf(ImageStates, item.Key) < 0 || item.Value == null) throw new CharacterLoadException("角色图片状态无效。");
            persona = persona ?? new PersonaDefinition { name = name, personality = "友善、温柔，喜欢陪伴用户。", speech_style = "用自然、简短的中文回答。", background = "用户的桌面小伙伴。", likes = new string[0], dislikes = new string[0] };
            CharacterLoader.ValidatePersona(persona);
            CustomDialogue.Validate(dialogue);
            string id = "custom_" + Guid.NewGuid().ToString("N");
            string root = Path.GetFullPath(Path.Combine(appDirectory, "characters")); Directory.CreateDirectory(root);
            string staging = Path.Combine(root, ".import-" + id), destination = Path.Combine(root, id);
            Directory.CreateDirectory(staging);
            try
            {
                var sprites = new Dictionary<string, SpriteDefinition>();
                foreach (var item in images)
                {
                    string file = item.Key + ".png";
                    item.Value.Save(Path.Combine(staging, file), ImageFormat.Png);
                    sprites[item.Key] = new SpriteDefinition { path = file };
                }
                var expressions = new Dictionary<string, string>();
                foreach (string expression in new[] { "normal", "happy", "wink", "surprised", "sad", "angry" })
                    expressions[expression] = images.ContainsKey(expression) ? expression : "normal";
                var animations = new Dictionary<string, AnimationDefinition>();
                foreach (string motion in new[] { "idle", "walk", "run", "jump", "wave" })
                    animations[motion] = new AnimationDefinition { sprite = images.ContainsKey(motion) ? motion : "normal", motion = motion == "wave" ? "idle" : motion };
                var definition = new CharacterDefinition
                {
                    schema_version = 1, id = id, display_name = name, default_size = 190, size_options = new[] { 140, 190, 250 },
                    default_expression = "normal", default_animation = "idle", persona = "persona.json", icon = "normal",
                    sprites = sprites, expressions = expressions, animations = animations, custom_dialogue = dialogue,
                    messages = new Dictionary<string, string> { { "startup", "我来陪你啦！" }, { "pet", "收到你的摸摸啦。" }, { "jump", "跳一下！" } }
                };
                WriteJson(Path.Combine(staging, "character.json"), definition);
                WriteJson(Path.Combine(staging, "persona.json"), persona);
                using (CharacterPack validated = CharacterLoader.Load(staging)) { }
                // Both paths are immediate, generated children of the same characters directory.
                Directory.Move(staging, destination);
                return destination;
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    // Only remove files created by this import; no recursive deletion of user directories.
                    foreach (string file in Directory.GetFiles(staging)) File.Delete(file);
                    Directory.Delete(staging);
                }
            }
        }
    }
}
