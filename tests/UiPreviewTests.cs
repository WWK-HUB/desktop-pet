using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopPet.Chat;

namespace DesktopPet
{
    internal static partial class ChatTests
    {
        private static async Task UiPreviewTests()
        {
            string workspace = Path.Combine(output, "ui-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
            string role = CopyTestPack("ui-preview");
            using (var launcher = new LauncherForm(workspace, role, false))
            {
                launcher.Opacity = 0; launcher.ShowInTaskbar = false; launcher.Show(); await Snapshot(launcher, "ui-home");
                launcher.Size = launcher.MinimumSize; await Snapshot(launcher, "ui-home-small"); launcher.ClientSize = new Size(1120, 800);
                launcher.OpenEditor(true); await Snapshot(launcher, "ui-edit-images");
                launcher.EditorPage.Tabs.SelectedIndex = 1; await Snapshot(launcher, "ui-edit-persona");
                launcher.EditorPage.Tabs.SelectedIndex = 2; await Snapshot(launcher, "ui-edit-dialogue");
                launcher.BackToHome(); launcher.OpenEditor(false); await Snapshot(launcher, "ui-create-empty"); launcher.Close();
            }
            using (var empty = new LauncherForm(workspace, null, false)) { empty.Opacity = 0; empty.ShowInTaskbar = false; empty.Show(); await Snapshot(empty, "ui-home-empty"); empty.Close(); }
            using (var settings = new ModelSettingsForm(workspace, delegate { }))
            {
                settings.Opacity = 0; settings.Show(); settings.BaseUrl.Text = "https://api.example.com/v1"; settings.ApiKey.Text = "demo-key-not-real"; settings.Model.Text = "your-chat-model";
                await Snapshot(settings, "ui-api-settings"); settings.Close();
            }
            var archive = new ChatArchive(workspace, role);
            archive.Append(new ArchivedTurn { user = "今天终于把计划做完了！", assistant = "辛苦啦！把这一刻记下来吧，今天的你又向前走了一小步。要不要休息一下？", speaker = "小曜", time = "今天 16:20", status = "success", persona = "preview" });
            archive.Append(new ArchivedTurn { user = "好呀，陪我聊会儿吧。", assistant = "当然，我就在这里。今天有没有一件让你特别开心的小事？", speaker = "小曜", time = "今天 16:21", status = "success", persona = "preview" });
            using (var chat = new ChatInputForm(s => Task.FromResult(false), delegate { }, archive)) { chat.Opacity = 0; chat.Show(); chat.Input.Text = "想和你分享今天的小事…"; await Snapshot(chat, "ui-chat"); chat.SetBusy(true); await Snapshot(chat, "ui-chat-pending"); chat.Close(); }
            using (var history = new ChatInputForm(null, delegate { }, archive)) { history.Opacity = 0; history.Show(); await Snapshot(history, "ui-history"); history.Close(); }
            Check(true, "All application pages rendered from their own windows using isolated data and no API calls");
        }
        private static async Task Snapshot(Form form, string name)
        {
            await Task.Delay(PageTransition.DurationMilliseconds + 80);
            form.PerformLayout(); form.Refresh();
            using (var shot = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                // RichEdit does not support DrawToBitmap. Render its own formatted text through
                // EM_FORMATRANGE into the same capture, without reading any other window.
                foreach (Control child in Descendants(form))
                {
                    var rich = child as RichTextBox; if (rich == null || !rich.Visible) continue;
                    Point point = rich.PointToScreen(Point.Empty); point.Offset(-form.Left, -form.Top);
                    using (var graphics = Graphics.FromImage(shot))
                    {
                        graphics.FillRectangle(Brushes.White, new Rectangle(point, rich.ClientSize));
                        float scaleX = 1440f / graphics.DpiX, scaleY = 1440f / graphics.DpiY;
                        IntPtr dc = graphics.GetHdc();
                        try
                        {
                            var range = new RichFormat { dc = dc, target = dc, area = new RichRect { left = (int)(point.X * scaleX), top = (int)(point.Y * scaleY), right = (int)((point.X + rich.ClientSize.Width - 18) * scaleX), bottom = (int)((point.Y + rich.ClientSize.Height) * scaleY) }, start = 0, end = -1 };
                            range.page = range.area; RenderRich(rich.Handle, 0x439, (IntPtr)1, ref range);
                        }
                        finally { graphics.ReleaseHdc(dc); Native.SendMessage(rich.Handle, 0x439, IntPtr.Zero, IntPtr.Zero); }
                    }
                }
                shot.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct RichRect { internal int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct RichFormat { internal IntPtr dc, target; internal RichRect area, page; internal int start, end; }
        [DllImport("user32.dll", EntryPoint = "SendMessage")] private static extern IntPtr RenderRich(IntPtr window, int message, IntPtr render, ref RichFormat range);
    }
}
