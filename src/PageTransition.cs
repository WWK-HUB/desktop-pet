using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopPet
{
    // Animate temporary snapshots, leaving the real page's layout and form opacity unchanged.
    internal sealed class PageTransition : IDisposable
    {
        internal const int DurationMilliseconds = 180;
        private readonly Control host, destination;
        private readonly TransitionSurface surface;
        private readonly Timer timer = new Timer { Interval = 15 };
        private readonly Stopwatch elapsed = new Stopwatch();
        private bool disposed;
        internal bool IsRunning { get { return !disposed; } }
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int action, int parameter, out bool enabled, int flags);
        internal static bool AnimationsEnabled
        {
            get { bool enabled; return !SystemParametersInfo(0x1042, 0, out enabled, 0) || enabled; }
        }
        internal static Bitmap Capture(Control control)
        {
            if (control == null || control.IsDisposed || !control.Visible || !control.IsHandleCreated || control.Width < 1 || control.Height < 1 || !AnimationsEnabled) return null;
            Bitmap image = null;
            try { image = new Bitmap(control.Width, control.Height); control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); return image; }
            catch { if (image != null) image.Dispose(); return null; }
        }
        internal static PageTransition Begin(Control host, Control destination, Bitmap before, int direction)
        {
            if (before == null) return null;
            if (host.IsDisposed || !host.Visible || destination.IsDisposed || !destination.Visible || !AnimationsEnabled) { before.Dispose(); return null; }
            return new PageTransition(host, destination, before, direction);
        }
        private PageTransition(Control host, Control destination, Bitmap before, int direction)
        {
            this.host = host; this.destination = destination;
            surface = new TransitionSurface { Before = before, Direction = direction < 0 ? -1 : 1, Bounds = destination == host ? host.ClientRectangle : destination.Bounds, BackColor = destination.BackColor, TabStop = false };
            host.Controls.Add(surface); surface.BringToFront();
            host.SizeChanged += End; host.VisibleChanged += VisibilityChanged; host.Disposed += End;
            destination.VisibleChanged += VisibilityChanged; destination.Disposed += End;
            timer.Tick += delegate
            {
                if (disposed) return;
                surface.Progress = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / DurationMilliseconds);
                if (surface.Progress >= 1) Dispose(); else surface.Invalidate();
            };
            // Wait until the navigation event has finished updating selection, labels and layout.
            host.BeginInvoke((Action)delegate
            {
                if (disposed) return;
                if (!host.Visible || destination.IsDisposed || !destination.Visible) { Dispose(); return; }
                if (host == destination) surface.Hide();
                Bitmap after = Capture(destination);
                if (disposed) { if (after != null) after.Dispose(); return; }
                if (after == null) { Dispose(); return; }
                surface.After = after; surface.Show(); surface.BringToFront(); elapsed.Start(); timer.Start(); surface.Invalidate();
            });
        }
        private void End(object sender, EventArgs e) { Dispose(); }
        private void VisibilityChanged(object sender, EventArgs e) { if (!host.Visible || !destination.Visible) Dispose(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            timer.Stop(); timer.Dispose(); elapsed.Stop();
            host.SizeChanged -= End; host.VisibleChanged -= VisibilityChanged; host.Disposed -= End;
            destination.VisibleChanged -= VisibilityChanged; destination.Disposed -= End;
            if (!host.IsDisposed) host.Controls.Remove(surface);
            surface.Dispose();
        }
        private sealed class TransitionSurface : Control
        {
            internal Bitmap Before, After;
            internal double Progress;
            internal int Direction;
            internal TransitionSurface() { SetStyle(ControlStyles.Selectable, false); DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                double eased = 1 - Math.Pow(1 - Progress, 3);
                int distance = Math.Max(8, (int)(12 * e.Graphics.DpiX / 96f));
                if (Before != null) e.Graphics.DrawImageUnscaled(Before, (int)(-Direction * distance * eased), 0);
                if (After == null) return;
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)eased });
                    e.Graphics.DrawImage(After, new Rectangle((int)(Direction * distance * (1 - eased)), 0, Width, Height), 0, 0, After.Width, After.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) { if (Before != null) { Before.Dispose(); Before = null; } if (After != null) { After.Dispose(); After = null; } }
                base.Dispose(disposing);
            }
        }
    }
}
