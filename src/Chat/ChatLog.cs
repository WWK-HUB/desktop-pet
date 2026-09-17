using System;
using System.IO;
using System.Text;

namespace DesktopPet.Chat
{
    internal sealed class ChatLog
    {
        private static readonly object Gate = new object();
        internal readonly string FilePath;
        internal ChatLog(string path = null)
        {
            FilePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet", "logs", "chat.log");
        }
        internal void Write(Exception error, string secret = null)
        {
            try
            {
                StringBuilder text = new StringBuilder(DateTimeOffset.Now.ToString("o") + "\n");
                for (Exception current = error; current != null; current = current.InnerException)
                {
                    ChatException chat = current as ChatException;
                    text.AppendLine(current.GetType().FullName + " HResult=" + current.HResult + (chat == null ? "" : " " + chat.Diagnostic));
                    text.AppendLine(current.StackTrace ?? "(no stack)");
                    // Raw exception messages, response bodies, URLs, headers and chat text are deliberately omitted.
                }
                string safe = text.ToString();
                if (!String.IsNullOrEmpty(secret)) safe = safe.Replace(secret, "[redacted]");
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1024 * 1024)
                    {
                        File.Copy(FilePath, FilePath + ".1", true);
                        File.WriteAllText(FilePath, "", Encoding.UTF8);
                    }
                    File.AppendAllText(FilePath, safe + "\n", Encoding.UTF8);
                }
            }
            catch { /* Logging must never crash the desktop pet, even in a read-only directory. */ }
        }
    }
}
