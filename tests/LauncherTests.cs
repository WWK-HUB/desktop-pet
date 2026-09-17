using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DesktopPet.Chat;
using DesktopPet.Characters;

namespace DesktopPet
{
    internal static partial class ChatTests
    {
        private static async Task LauncherTests(MockServer server)
        {
            string workspace = Path.Combine(output, "launcher-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            TestCharacterCreation(workspace);
            await ConversationFeatureTests(server, workspace);
            await CustomDialogueTests(server, workspace);
            using (var empty = new LauncherForm(workspace, null, false))
            {
                empty.Show();
                Check(empty.Visible && empty.RunningPet == null && !empty.LaunchButton.Enabled, "Empty library and missing runner open a usable launcher without spawning a pet");
                empty.Close();
            }
            string original = Path.Combine(workspace, "uploaded.png");
            using (var source = new Bitmap(100, 100))
            {
                using (Graphics g = Graphics.FromImage(source)) { g.Clear(Color.White); g.FillRectangle(Brushes.Black, 20, 15, 60, 70); g.FillRectangle(Brushes.White, 40, 40, 20, 20); }
                source.Save(original, ImageFormat.Png);
            }
            string first, second;
            using (Bitmap plain = CharacterLibrary.PrepareImage(original, false))
                Check(plain.GetPixel(0, 0).A == 255, "Background removal is opt-in and preserves ordinary pictures by default");
            using (Bitmap prepared = CharacterLibrary.PrepareImage(original, true))
            {
                Check(prepared.GetPixel(0, 0).A == 0 && prepared.GetPixel(45, 45).ToArgb() == Color.White.ToArgb() && prepared.GetPixel(22, 20).ToArgb() == Color.Black.ToArgb(), "Background removal preserves enclosed same-color details and dark outlines");
                first = CharacterLibrary.Import(workspace, "棉花", prepared);
                second = CharacterLibrary.Import(workspace, "星夜", prepared);
            }
            File.Delete(original);
            using (CharacterPack pack = CharacterLoader.Load(first))
            {
                Check(pack.Sprites.Count == 1 && pack.Persona.name == "棉花" && pack.MotionFor("jump") == "jump", "Imported pack works after source deletion and binds its own persona and motions");
                string before = File.ReadAllText(Path.Combine(first, "persona.json"));
                CharacterFails(() => CharacterLibrary.SavePersona(pack, new PersonaDefinition { name = "" }), "Invalid persona is rejected before writing");
                Check(File.ReadAllText(Path.Combine(first, "persona.json")) == before, "Invalid persona cannot corrupt the saved role");
            }
            string transparent = Path.Combine(workspace, "alpha.png");
            using (var source = new Bitmap(100, 40))
            {
                source.SetPixel(20, 20, Color.FromArgb(128, 20, 40, 60)); source.Save(transparent, ImageFormat.Png);
            }
            using (var prepared = CharacterLibrary.PrepareImage(transparent, true))
                Check(prepared.GetPixel(0, 0).A == 0 && prepared.GetPixel(20, 20).A == 128, "PNG import preserves partial alpha even when optional background removal is enabled");
            string wideFile = Path.Combine(workspace, "wide.jpg");
            using (var source = new Bitmap(1800, 180))
            {
                using (Graphics g = Graphics.FromImage(source)) g.Clear(Color.Coral);
                source.Save(wideFile, ImageFormat.Jpeg);
            }
            string wide;
            using (var prepared = CharacterLibrary.PrepareImage(wideFile, false))
            {
                Check(prepared.Width == 512 && prepared.Height == 51, "Large JPG import downsizes to a bounded texture with original proportions");
                wide = CharacterLibrary.Import(workspace, "宽图", prepared);
            }
            using (var atlas = new Atlas(CharacterLoader.Load(wide)))
            using (var frame = Painter.Frame(atlas, "normal", 190, false, 0, false, "", 0))
            {
                int minX = frame.Width, maxX = -1, minY = frame.Height, maxY = -1;
                for (int y = 0; y < frame.Height; y++) for (int x = 0; x < frame.Width; x++) if (frame.GetPixel(x, y).A > 0) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                Check(minX >= 10 && maxX < frame.Width - 10 && (double)(maxX - minX + 1) / (maxY - minY + 1) > 9, "Wide skins fit the desktop canvas without clipping or stretching");
            }
            string blank = Path.Combine(workspace, "blank.png");
            using (var source = new Bitmap(10, 10)) source.Save(blank, ImageFormat.Png);
            CharacterFails(() => { using (var ignored = CharacterLibrary.PrepareImage(blank, false)) { } }, "Fully transparent uploads receive a validation error");
            Directory.CreateDirectory(Path.Combine(workspace, "characters", "broken"));
            System.Collections.Generic.List<string> errors;
            var entries = CharacterLibrary.List(workspace, null, out errors);
            Check(entries.Count == 3 && errors.Count == 1, "A broken character pack does not prevent valid skins from appearing");
            CharacterLibrary.SaveSelection(workspace, first);
            ModelSettingsStore.Save(workspace, new ModelSettings { BaseUrl = server.BaseUrl, ApiKey = FakeKey, Model = FixtureModel, TimeoutSeconds = "3" });
            using (var launcher = new LauncherForm(workspace, null, false))
            {
                launcher.Show(); await Task.Delay(100);
                IntPtr handle = launcher.Handle; int forms = Application.OpenForms.Count;
                Check(launcher.RunningPet == null && launcher.EditorPage == null && launcher.Visible && ((CharacterEntry)launcher.Skins.SelectedItem).Name == "棉花", "Home shows selected character without a pet or inline persona editor");
                Check(!Descendants(launcher).OfType<TextBox>().Any(), "Launcher home has no persona text fields");
                launcher.EditButton.PerformClick();
                Check(launcher.EditorPage != null && launcher.EditorPage.FindForm() == launcher && launcher.Handle == handle && Application.OpenForms.Count == forms, "Edit button navigates inside the same native window without creating another Form");
                launcher.EditorPage.PersonaText.Text = "你是一只爱读书的小猫。";
                launcher.EditorPage.SpeechStyle.Text = "每次以喵开头，用温柔口吻回答。";
                launcher.EditorPage.CreateButton.PerformClick();
                Check(launcher.EditorPage == null, "Saving existing role returns to the launcher home");
                using (var saved = CharacterLoader.Load(first)) Check(saved.Persona.speech_style.Contains("喵"), "Editing existing role persists its persona without creating a duplicate role");
                launcher.Skins.SelectedItem = launcher.Skins.Items.Cast<CharacterEntry>().First(p => p.Folder == second);
                launcher.EditButton.PerformClick();
                Check(launcher.EditorPage.PersonaName.Text == "星夜" && !launcher.EditorPage.SpeechStyle.Text.Contains("喵"), "Edit page loads the selected role's independent saved persona");
                launcher.EditorPage.SpeechStyle.Text = "以晚上好开头，语气正式。"; launcher.EditorPage.CreateButton.PerformClick();
                Check(launcher.StartSelectedPet() && !launcher.Visible && launcher.RunningPet.Visible, "Only Start creates the pet after returning from the edit page");
                PetWindow old = launcher.RunningPet; old.Wander = false;
                server.Enqueue(200, Answer("晚上好。")); await old.SubmitChatAsync("你好");
                Check(Content(Messages(server.Requests.Last().Body)[0]).Contains("以晚上好开头"), "Launched pet's HTTP request uses the edited role persona");
                old.PetMenu.Items.Find("launcher", false)[0].PerformClick();
                Check(launcher.Visible && launcher.RunningPet == old, "Pet menu reopens launcher without creating another pet");
                launcher.Skins.SelectedItem = launcher.Skins.Items.Cast<CharacterEntry>().First(p => p.Folder == first);
                Check(launcher.StartSelectedPet() && old.IsDisposed && launcher.RunningPet != old, "Applying another role replaces the previous pet");
                launcher.RunningPet.Wander = false;
                server.Enqueue(200, Answer("喵，你好。")); await launcher.RunningPet.SubmitChatAsync("你好");
                Check(Messages(server.Requests.Last().Body).Length == 2 && Content(Messages(server.Requests.Last().Body)[0]).Contains("每次以喵开头"), "Role switch uses new persona with fresh conversation");
                launcher.ShowLauncher(); launcher.EditButton.PerformClick();
                launcher.EditorPage.PersonaText.Text = "温柔的小猫，喜欢天文学。"; launcher.EditorPage.CreateButton.PerformClick();
                server.Enqueue(200, Answer("喵，我们看星星。")); await launcher.RunningPet.SubmitChatAsync("你好");
                Check(Messages(server.Requests.Last().Body).Length == 2 && Content(Messages(server.Requests.Last().Body)[0]).Contains("天文学"), "Running pet follows atomically updated persona reference and clears old context");
                launcher.EditButton.PerformClick(); launcher.EditorPage.PersonaName.Text = "";
                Check(!launcher.EditorPage.TryCreate() && launcher.EditorPage != null && launcher.RunningPet != null, "Invalid edits stay on editor and preserve running pet and saved role");
                launcher.EditorPage.BackButton.PerformClick();
                using (var pack = CharacterLoader.Load(first)) Check(pack.Persona.name == "棉花", "Cancel discards invalid unsaved persona edits");
                launcher.RunningPet.Close();
                Check(launcher.Visible && launcher.RunningPet == null, "Closing pet returns to home");
                Check(CharacterLoader.ResolveSelection(workspace, new string[0]) == first, "Successful launch remembers selection");
                launcher.StartSelectedPet(); old = launcher.RunningPet;
                int pendingCount = server.Requests.Count;
                server.Enqueue(200, Answer("旧角色迟到的回复"), 1000);
                Task pending = old.SubmitChatAsync("等待中的消息"); await WaitUntil(() => server.Requests.Count > pendingCount);
                launcher.ShowLauncher(); launcher.Skins.SelectedItem = launcher.Skins.Items.Cast<CharacterEntry>().First(p => p.Folder == second);
                launcher.StartSelectedPet(); await pending;
                Check(old.IsDisposed && !launcher.RunningPet.ChatBusy && launcher.RunningPet.ChatReply != "旧角色迟到的回复", "Switching roles cancels old pending requests without stale replies");
                old = launcher.RunningPet; launcher.ShowLauncher(); launcher.Close();
                Check(old.IsDisposed, "Closing launcher releases pet and tray resources");
            }
            TestCharacterEditing(workspace);
            string previewWorkspace = Path.Combine(workspace, "preview"); Directory.CreateDirectory(previewWorkspace);
            string demo = CopyTestPack("launcher-demo-preview");
            using (var launcher = new LauncherForm(previewWorkspace, demo, false))
            {
                launcher.TopMost = true; launcher.Show(); launcher.Activate(); await Task.Delay(200);
                CaptureLauncher(launcher, "launcher-window.png");
                IntPtr handle = launcher.Handle; int forms = Application.OpenForms.Count;
                launcher.Controls.Find("createCharacter", true)[0].Focus();
                ((Button)launcher.Controls.Find("createCharacter", true)[0]).PerformClick();
                Check(launcher.EditorPage != null && launcher.EditorPage.Images.Count == 0 && launcher.Handle == handle && Application.OpenForms.Count == forms, "New character button navigates to an empty editor in the same window");
                using (var pack = CharacterLoader.Load(demo))
                    foreach (string state in new[] { "normal", "happy", "run", "wave" })
                    {
                        string path = Path.Combine(previewWorkspace, state + ".png"); pack.Sprites[pack.ResolveSprite(state)].Save(path, ImageFormat.Png);
                        launcher.EditorPage.SetImage(state, path, false);
                    }
                launcher.EditorPage.CharacterName.Text = "我的小曜"; launcher.EditorPage.PersonaName.Text = "小曜";
                await Task.Delay(150); CaptureLauncher(launcher, "create-character-window.png");
                launcher.EditorPage.CreateButton.PerformClick();
                Check(launcher.EditorPage == null && launcher.RunningPet == null && ((CharacterEntry)launcher.Skins.SelectedItem).Name == "我的小曜", "Creating role saves and selects it on home without starting a pet");
                launcher.EditButton.PerformClick(); await Task.Delay(150); CaptureLauncher(launcher, "edit-character-window.png");
                launcher.EditorPage.Tabs.SelectedIndex = 1; await Task.Delay(150); CaptureLauncher(launcher, "edit-persona-page.png");
                launcher.EditorPage.BackButton.PerformClick();
                Check(launcher.EditorPage == null && launcher.Handle == handle, "Back navigation restores home in the original window");
                launcher.Close();
            }
        }
        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
        {
            foreach (Control child in control.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
        }
        private static void CaptureLauncher(Form form, string file)
        {
            using (var shot = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size)); shot.Save(Path.Combine(output, file), ImageFormat.Png); }
        }
        private static void TestCharacterEditing(string workspace)
        {
            string packFolder = CopyTestPack("editor-original");
            string manifest = Path.Combine(packFolder, "character.json");
            var doc = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(manifest));
            doc["extension_test"] = "keep me"; SaveJson(manifest, doc);
            string originalJson = File.ReadAllText(manifest);
            string upload = Path.Combine(workspace, "edited-main.png");
            using (var bitmap = new Bitmap(20, 30)) { using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.Black); bitmap.Save(upload, ImageFormat.Png); }
            using (var launcher = new LauncherForm(workspace, packFolder, false))
            {
                launcher.Show(); launcher.EditButton.PerformClick();
                var page = launcher.EditorPage;
                Check(page.Images.Count == 10 && page.PersonaName.Text == "小曜", "Existing sprite-sheet role loads all editable state previews and all persona fields");
                page.SetImage("normal", upload, false); page.ClearImage("run"); page.PersonaText.Text = "不保存的修改";
                page.BackButton.PerformClick();
                Check(File.ReadAllText(manifest) == originalJson, "Cancel leaves existing sprite-sheet manifest and artwork references unchanged");
                launcher.EditButton.PerformClick(); page = launcher.EditorPage;
                page.SetImage("normal", upload, false); page.ClearImage("run"); page.CharacterName.Text = "编辑后的小曜";
                page.PersonaName.Text = "阿曜"; page.PersonaText.Text = "认真友善"; page.SpeechStyle.Text = "用正式语气回答";
                page.BackgroundStory.Text = "图书馆的管理员"; page.Likes.Text = "书籍\r\n星星"; page.Dislikes.Text = "噪音";
                page.CreateButton.PerformClick();
                Check(launcher.EditorPage == null && File.ReadAllText(manifest).Contains("keep me"), "Saving updates existing role and preserves unknown manifest extensions");
                using (var pack = CharacterLoader.Load(packFolder))
                {
                    Check(pack.Definition.id == "demo_character" && pack.Definition.default_size == 190 && pack.Definition.messages.ContainsKey("startup") && pack.Sprites.ContainsKey("front"), "Editing preserves role identity, sizing, messages and extra original sprites");
                    Check(pack.ResolveSprite("run") == pack.ResolveSprite("normal") && pack.MotionFor("run") == "run", "Removing existing running artwork falls back to updated main without losing motion");
                    Check(pack.Sprites[pack.ResolveSprite("normal")].GetPixel(0, 0).ToArgb() == Color.Black.ToArgb(), "Replacement PNG preserves black pixels even in legacy dark-background-removal packs");
                    Check(pack.Persona.name == "阿曜" && pack.Persona.speech_style.Contains("正式") && pack.Persona.background.Contains("图书馆") && pack.Persona.likes.Length == 2 && pack.Persona.dislikes.Length == 1, "All six persona fields persist through the edit page");
                    Check(pack.ResolveSprite("happy") == "happy" && File.Exists(Path.Combine(packFolder, "assets", "character-sheet.png")), "Unchanged expression references and original artwork are retained");
                    string beforeFailedSave = File.ReadAllText(manifest); int filesBefore = Directory.GetFiles(packFolder).Length;
                    bool failed = false;
                    using (var lockFile = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        try { CharacterLibrary.Update(pack, "不能提交", new System.Collections.Generic.Dictionary<string, Bitmap> { { "normal", pack.Sprites[pack.ResolveSprite("normal")] } }, pack.Persona); }
                        catch (IOException) { failed = true; }
                    }
                    Check(failed && File.ReadAllText(manifest) == beforeFailedSave && Directory.GetFiles(packFolder).Length == filesBefore, "Failed manifest commit rolls back staged image/persona files and leaves the existing role intact");
                }
                launcher.EditButton.PerformClick();
                Check(launcher.EditorPage.CharacterName.Text == "编辑后的小曜" && !launcher.EditorPage.Images.ContainsKey("run") && launcher.EditorPage.Likes.Text.Contains("星星"), "Reopening editor restores saved images, removed-state fallback and persona fields");
                launcher.EditorPage.BackButton.PerformClick(); launcher.Close();
            }
        }

        private static void TestCharacterCreation(string workspace)
        {
            string root = Path.Combine(workspace, "creation"); Directory.CreateDirectory(root);
            string main = Path.Combine(root, "main.png"), happy = Path.Combine(root, "happy.png"), run = Path.Combine(root, "run.png");
            string[] files = { main, happy, run }; Color[] colors = { Color.Red, Color.Green, Color.Blue };
            for (int i = 0; i < files.Length; i++)
                using (var bitmap = new Bitmap(20, 30)) { using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(colors[i]); bitmap.Save(files[i], ImageFormat.Png); }
            using (var dialog = new CharacterEditorPage(root))
            {
                dialog.Show();
                Check(dialog.States.Items.Count == 10 && !dialog.CreateButton.Enabled && dialog.Images.Count == 0, "New character editor starts empty with required main and nine optional slots");
                dialog.CharacterName.Text = "多图伙伴";
                Check(!dialog.TryCreate() && !Directory.Exists(Path.Combine(root, "characters")), "Missing main image cannot create a role or leave partial files");
                Check(dialog.SetImage("happy", happy, false) && !dialog.CreateButton.Enabled, "Optional expressions alone do not satisfy required main image");
                Check(dialog.SetImage("normal", main, false) && dialog.CreateButton.Enabled, "Main image plus name enables creation");
                dialog.SetImage("run", run, false);
                Bitmap previous = dialog.Images["run"];
                Check(!dialog.SetImage("run", Path.Combine(root, "missing.png"), false) && Object.ReferenceEquals(previous, dialog.Images["run"]), "Failed image replacement retains the existing motion upload");
                dialog.ClearImage("happy"); Check(!dialog.Images.ContainsKey("happy") && dialog.CreateButton.Enabled, "Removing optional artwork leaves the role creatable");
                dialog.SetImage("happy", happy, false);
                dialog.ClearImage("normal"); Check(!dialog.CreateButton.Enabled && !dialog.TryCreate(), "Removing main image disables creation even with expression and motion uploads");
                dialog.SetImage("normal", main, false);
                dialog.PersonaText.Text = "一只喜欢读书的小猫，说话轻快。";
                File.Delete(main); File.Delete(happy); File.Delete(run);
                Check(dialog.TryCreate(), "Creation uses prepared images after original source files are deleted");
                using (var atlas = new Atlas(CharacterLoader.Load(dialog.CreatedFolder)))
                using (var pet = new PetWindow(atlas, true))
                {
                    var pack = atlas.Character;
                    Check(pack.Sprites.Count == 3 && pack.Definition.default_animation == "idle" && pack.ResolveSprite("idle") == "normal", "New role starts with mandatory main image as idle artwork");
                    Check(pack.ResolveSprite("happy") == "happy" && pack.ResolveSprite("run") == "run" && pack.MotionFor("run") == "run", "Uploaded happy and running images bind to the corresponding runtime states");
                    Check(new[] { "wink", "surprised", "sad", "angry", "walk", "jump", "wave" }.All(s => pack.ResolveSprite(s) == "normal"), "Every absent expression or motion image falls back to main");
                    Check(pack.MotionFor("walk") == "walk" && pack.MotionFor("jump") == "jump" && pack.MotionFor("wave") == "idle", "Fallback artwork retains walking, jumping and waving behavior");
                    Check(pack.Persona.personality.Contains("小猫"), "New role binds the persona entered in its creation dialog");
                    pet.Show(); pet.Act("run", "", 2);
                    using (var rendered = Painter.Frame(atlas, pet.Pose, 190, false, 0, false, "", 0))
                        Check(rendered.GetPixel(rendered.Width / 2, rendered.Height - 40).ToArgb() == Color.Blue.ToArgb(), "Desktop rendering actually displays the uploaded running artwork");
                    pet.Act("happy", "", 2);
                    using (var rendered = Painter.Frame(atlas, pet.Pose, 190, false, 0, false, "", 0))
                        Check(rendered.GetPixel(rendered.Width / 2, rendered.Height - 40).ToArgb() == Color.Green.ToArgb(), "Desktop rendering actually displays the uploaded expression artwork");
                    pet.Close();
                }
                dialog.Dispose();
            }
            string cancelRoot = Path.Combine(root, "cancelled");
            using (var dialog = new CharacterEditorPage(cancelRoot)) { dialog.Show(); dialog.Dispose(); }
            Check(!Directory.Exists(cancelRoot), "Cancelling new character makes no character directories");
            using (var bitmap = new Bitmap(12, 20))
            {
                using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(Color.Gold);
                var images = new System.Collections.Generic.Dictionary<string, Bitmap>();
                foreach (string state in CharacterLibrary.ImageStates) images[state] = bitmap;
                string all = CharacterLibrary.Import(root, "完整角色", images, null);
                using (var pack = CharacterLoader.Load(all))
                    Check(pack.Sprites.Count == 10 && CharacterLibrary.ImageStates.All(s => pack.ResolveSprite(s) == s), "All ten image slots survive disk round-trip and use their own artwork");
                string onlyMain = CharacterLibrary.Import(root, "只有主图", bitmap);
                using (var pack = CharacterLoader.Load(onlyMain))
                    Check(pack.Sprites.Count == 1 && CharacterLibrary.ImageStates.All(s => pack.ResolveSprite(s) == "normal"), "Uploading only the main image is sufficient for all existing interactions");
            }
        }
    }
}
