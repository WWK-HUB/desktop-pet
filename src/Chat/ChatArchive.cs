using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DesktopPet.Chat
{
    internal sealed class ArchivedTurn
    {
        public string time { get; set; }
        public string user { get; set; }
        public string assistant { get; set; }
        public string speaker { get; set; }
        public string status { get; set; }
        public string persona { get; set; }
    }

    // Local display history is never used as the model's request history.
    internal sealed class ChatArchive
    {
        private static readonly object FileGate = new object();
        internal readonly string FilePath;
        internal string Error { get; private set; }
        internal ChatArchive(string settingsDirectory, string characterFolder)
        {
            FilePath = Path.Combine(settingsDirectory, "user-data", "history", Hash(Path.GetFullPath(characterFolder).ToUpperInvariant()) + ".jsonl");
        }
        internal static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        internal void Append(ArchivedTurn turn)
        {
            try
            {
                lock (FileGate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.AppendAllText(FilePath, new JavaScriptSerializer().Serialize(turn) + Environment.NewLine, new UTF8Encoding(false));
                }
                Error = null;
            }
            catch (Exception error) { Error = "聊天记录未能保存，请检查程序目录是否可写。"; new ChatLog().Write(error); }
        }
        internal List<ArchivedTurn> Read(int limit)
        {
            var recent = new Queue<ArchivedTurn>();
            try
            {
                lock (FileGate)
                {
                    if (!File.Exists(FilePath)) return new List<ArchivedTurn>();
                    var json = new JavaScriptSerializer { MaxJsonLength = 131072 };
                    foreach (string line in File.ReadLines(FilePath, Encoding.UTF8))
                    {
                        try
                        {
                            var turn = json.Deserialize<ArchivedTurn>(line);
                            if (turn == null || turn.user == null || turn.assistant == null) continue;
                            recent.Enqueue(turn); while (recent.Count > limit) recent.Dequeue();
                        }
                        catch (ArgumentException) { }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            catch (Exception error) { Error = "部分聊天记录未能读取，请检查记录文件。"; new ChatLog().Write(error); }
            return new List<ArchivedTurn>(recent);
        }
        internal int ReadContextTurns()
        {
            int turns;
            try { if (Int32.TryParse(File.ReadAllText(FilePath + ".context"), out turns) && Array.IndexOf(new[] { 0, 2, 4, 6 }, turns) >= 0) return turns; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return 2;
        }
        internal void SaveContextTurns(int turns)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath + ".context", turns.ToString()); }
            catch (Exception error) { new ChatLog().Write(error); }
        }
    }
}
