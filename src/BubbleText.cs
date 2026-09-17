using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace DesktopPet
{
    internal static class BubbleText
    {
        internal const int ExtraHeight = 92;
        internal const int TextHeight = 76;
        internal static List<string> Pages(string text, int canvasWidth)
        {
            var pages = new List<string>();
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Font font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point))
            using (StringFormat format = Format())
            {
                // Explicit pixel breaks make the measured pagination and rendered text identical.
                StringBuilder page = new StringBuilder(), line = new StringBuilder();
                int lines = 1;
                int maxLines = Math.Max(1, (int)(TextHeight / font.GetHeight(g)));
                TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text.Replace("\r\n", "\n").Replace('\r', '\n'));
                while (elements.MoveNext())
                {
                    string element = elements.GetTextElement();
                    bool newline = element == "\n";
                    bool wrap = !newline && line.Length > 0 && g.MeasureString(line.ToString() + element, font, Int32.MaxValue, format).Width > canvasWidth - 48;
                    if (newline || wrap)
                    {
                        page.Append(line); line.Clear();
                        if (lines >= maxLines) { pages.Add(page.ToString()); page.Clear(); lines = 1; }
                        else { page.Append('\n'); lines++; }
                        if (newline) continue;
                    }
                    line.Append(element);
                }
                page.Append(line);
                if (page.Length > 0 || pages.Count == 0) pages.Add(page.ToString());
            }
            return pages;
        }
        internal static StringFormat Format()
        {
            return new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
        }
    }
}
