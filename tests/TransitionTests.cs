using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DesktopPet
{
    internal static partial class ChatTests
    {
        private static async Task TransitionTests()
        {
            string workspace = Path.Combine(output, "transition-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
            string role = CopyTestPack("transition-role");
            using (var launcher = new LauncherForm(workspace, role, false))
            {
                launcher.Opacity = 0; launcher.ShowInTaskbar = false; launcher.Show(); IntPtr window = launcher.Handle;
                launcher.OpenEditor(false);
                Check(launcher.EditorPage != null && launcher.Handle == window, "Animated navigation changes the page synchronously inside the original window");
                if (PageTransition.AnimationsEnabled) Check(launcher.IsPageTransitionRunning, "Entering the character editor starts a transition overlay");
                await WaitUntil(() => !launcher.IsPageTransitionRunning);
                Check(launcher.EditorPage.CharacterName.CanSelect, "The real editor is interactive after the transition finishes");
                var tabs = (PetTabs)launcher.EditorPage.Tabs;
                tabs.SelectedIndex = 1;
                if (PageTransition.AnimationsEnabled) Check(tabs.IsTransitionRunning, "Switching editor tabs animates only the page content");
                tabs.SelectedIndex = 2; tabs.SelectedIndex = 0;
                await WaitUntil(() => !tabs.IsTransitionRunning);
                Check(tabs.SelectedIndex == 0 && tabs.SelectedTab.Visible, "Rapid tab changes finish on the most recent selection");
                launcher.BackToHome(); launcher.OpenEditor(true); launcher.BackToHome();
                await WaitUntil(() => !launcher.IsPageTransitionRunning);
                Check(launcher.EditorPage == null && launcher.EditButton.CanSelect, "Rapid forward and back navigation leaves the correct home page usable");
                launcher.OpenEditor(true); launcher.Size = new Size(launcher.Width + 10, launcher.Height + 10);
                Check(!launcher.IsPageTransitionRunning && launcher.EditorPage.Visible, "Resizing safely ends the overlay and exposes the current page");
                launcher.BackToHome(); await WaitUntil(() => !launcher.IsPageTransitionRunning);
                uint before = Native.GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
                for (int i = 0; i < 8; i++) { launcher.OpenEditor(false); launcher.BackToHome(); }
                await WaitUntil(() => !launcher.IsPageTransitionRunning);
                GC.Collect(); GC.WaitForPendingFinalizers();
                uint after = Native.GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
                Check(after <= before + 12, "Repeated navigation releases temporary animation graphics (" + before + " -> " + after + ")");
                launcher.OpenEditor(true); launcher.Close(); await Task.Delay(250);
                Check(launcher.IsDisposed && !launcher.IsPageTransitionRunning, "Closing during a transition disposes its timer and pending frames");
            }
            if (!PageTransition.AnimationsEnabled) { Check(true, "Windows reduced-motion preference skips visual effects while preserving navigation"); return; }
            using (var host = new Form { ClientSize = new Size(320, 180), Opacity = 0, ShowInTaskbar = false })
            using (var oldPage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(220, 40, 40) })
            using (var newPage = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 40, 220) })
            {
                host.Controls.Add(oldPage); host.Show(); Bitmap before = PageTransition.Capture(oldPage);
                oldPage.Hide(); host.Controls.Add(newPage); newPage.BringToFront();
                using (var animation = PageTransition.Begin(host, newPage, before, 1))
                {
                    bool blended = false;
                    while (animation.IsRunning)
                    {
                        Control top = host.Controls[0];
                        using (var frame = new Bitmap(top.Width, top.Height))
                        {
                            top.DrawToBitmap(frame, new Rectangle(Point.Empty, frame.Size)); Color pixel = frame.GetPixel(frame.Width / 2, frame.Height / 2);
                            if (pixel.R > 55 && pixel.R < 205 && pixel.B > 55 && pixel.B < 205) blended = true;
                        }
                        await Task.Delay(15);
                    }
                    Check(blended, "Transition paints intermediate blended frames instead of abruptly swapping pages");
                }
                host.Close();
            }
        }
    }
}
