using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopPet.Chat;
using DesktopPet.Characters;

namespace DesktopPet
{
    internal static partial class ChatTests
    {
        private static async Task CustomDialogueTests(MockServer server, string workspace)
        {
            string role = CopyTestPack("custom-dialogue"), config = Path.Combine(workspace, "custom-dialogue-config");
            Directory.CreateDirectory(config);
            ModelSettingsStore.Save(config, new ModelSettings { BaseUrl = server.BaseUrl, ApiKey = FakeKey, Model = FixtureModel, TimeoutSeconds = "3" });
            server.Enqueue(200, AmbientAnswer("模型台词")); await AmbientDialogue.GenerateAsync(config, role, false, CancellationToken.None);
            using (var launcher = new LauncherForm(config, role, false))
            {
                launcher.Show(); launcher.StartSelectedPet(); var pet = launcher.RunningPet; pet.Wander = false;
                launcher.ShowLauncher(); launcher.EditButton.PerformClick(); var editor = launcher.EditorPage;
                editor.Tabs.SelectedIndex = 2;
                Check(editor.DefaultMessages.Count == 19 && editor.IdleLines.Text == "", "Existing characters expose an empty custom dialogue tab without copying generated defaults into overrides");
                editor.DefaultMessages["pet"].Text = "这是我写的摸头台词。";
                editor.IdleLines.Text = "自定义待机第一句。\r\n自定义待机第二句。";
                await Task.Delay(150);
                // Render only our form so other foreground applications are never captured.
                using (var shot = new Bitmap(launcher.Width, launcher.Height))
                { launcher.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size)); shot.Save(Path.Combine(output, "custom-dialogue-page.png")); }
                editor.CreateButton.PerformClick();
                pet.PetMenu.Items.Cast<System.Windows.Forms.ToolStripItem>().First(i => i.Text == "摸摸头").PerformClick();
                Check(pet.CurrentSpeech == "这是我写的摸头台词。", "Saving custom interaction dialogue immediately overrides generated lines in the running pet");
                pet.Tick(60); Check(pet.CurrentSpeech.StartsWith("自定义待机"), "Idle speech exclusively uses the custom list when supplied");
                var definition = CharacterLoader.LoadCurrentDefinition(role);
                var generated = AmbientDialogue.Read(role, AmbientDialogue.Fingerprint(role));
                Check(AmbientDialogue.Resolve("wave", definition.custom_dialogue, generated, definition).StartsWith("模型台词"), "Blank custom interaction falls back independently to its generated line");
                Check(AmbientDialogue.Resolve("wave", definition.custom_dialogue, null, definition) == definition.messages["wave"], "Without generated lines, the original character message is restored");
                definition.messages["idle"] = "初始待机台词。";
                Check(AmbientDialogue.ResolveIdle(null, null, definition)[0] == "初始待机台词。", "Initial idle message is used when neither custom nor generated idle lines exist");
                launcher.EditButton.PerformClick();
                Check(launcher.EditorPage.DefaultMessages["pet"].Text == "这是我写的摸头台词。" && launcher.EditorPage.IdleLines.Lines.Length == 2, "Reopening the editor restores persisted custom interaction and idle lines");
                launcher.EditorPage.DefaultMessages["pet"].Text = "取消时不应保存"; launcher.EditorPage.BackButton.PerformClick(); pet.RefreshPersona();
                Check(CharacterLoader.LoadCurrentDefinition(role).custom_dialogue.messages["pet"] == "这是我写的摸头台词。", "Cancelling dialogue edits leaves saved custom lines intact");
                launcher.EditButton.PerformClick(); launcher.EditorPage.SpeechStyle.Text = "正式地回答。"; launcher.EditorPage.CreateButton.PerformClick();
                Check(AmbientDialogue.Read(role, AmbientDialogue.Fingerprint(role)) == null, "Persona edits invalidate model lines while preserving user-authored dialogue");
                server.Enqueue(200, AmbientAnswer("重新生成")); await AmbientDialogue.GenerateAsync(config, role, true, CancellationToken.None); pet.RefreshPersona();
                pet.PetMenu.Items.Cast<System.Windows.Forms.ToolStripItem>().First(i => i.Text == "摸摸头").PerformClick();
                Check(pet.CurrentSpeech == "这是我写的摸头台词。", "Regenerating model dialogue after persona changes never replaces custom dialogue");
                launcher.EditButton.PerformClick(); launcher.EditorPage.DefaultMessages["pet"].Clear(); launcher.EditorPage.IdleLines.Clear(); launcher.EditorPage.CreateButton.PerformClick();
                pet.PetMenu.Items.Cast<System.Windows.Forms.ToolStripItem>().First(i => i.Text == "摸摸头").PerformClick();
                Check(pet.CurrentSpeech.StartsWith("重新生成"), "Clearing a saved custom line immediately restores the next priority");
                pet.Tick(120); Check(pet.CurrentSpeech.StartsWith("重新生成"), "Clearing the custom idle list restores generated idle lines");
                launcher.EditButton.PerformClick(); launcher.EditorPage.IdleLines.Text = String.Join("\r\n", Enumerable.Repeat("一条台词", 21));
                Check(!launcher.EditorPage.TryCreate() && CharacterLoader.LoadCurrentDefinition(role).custom_dialogue.idle.Length == 0, "Invalid custom dialogue is rejected before any character changes are saved");
                launcher.Close();
            }
            using (var editor = new CharacterEditorPage(config))
            {
                editor.Images["normal"] = new Bitmap(10, 10); using (var g = Graphics.FromImage(editor.Images["normal"])) g.Clear(Color.Blue);
                editor.CharacterName.Text = "新建台词角色"; editor.DefaultMessages["startup"].Text = "自定义见面台词。";
                Check(editor.TryCreate(), "New characters can save custom dialogue together with their required main image");
                using (var pack = CharacterLoader.Load(editor.CreatedFolder)) Check(pack.Definition.custom_dialogue.messages["startup"] == "自定义见面台词。", "Imported characters persist custom dialogue inside their own manifest");
            }
        }
    }
}
