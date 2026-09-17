using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DesktopPet.Chat
{
    internal sealed class ModelSettings
    {
        internal string BaseUrl = "", ApiKey = "", Model = "", TimeoutSeconds = "45";
    }
    internal sealed class SavedModelSettings
    {
        public string BaseUrl { get; set; }
        public string EncryptedApiKey { get; set; }
        public string Model { get; set; }
        public int TimeoutSeconds { get; set; }
    }
    internal static class ModelSettingsStore
    {
        internal const string FileName = "model-settings.json";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DesktopPet.ModelSettings.v1");
        internal static ModelSettings Read(string folder)
        {
            string path = Path.Combine(folder, FileName);
            if (!File.Exists(path)) return ChatConfig.ReadLegacy(folder);
            try
            {
                var saved = new JavaScriptSerializer().Deserialize<SavedModelSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (saved == null) throw new InvalidDataException();
                string key = String.IsNullOrEmpty(saved.EncryptedApiKey) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(saved.EncryptedApiKey), Entropy, DataProtectionScope.CurrentUser));
                return new ModelSettings { BaseUrl = saved.BaseUrl, ApiKey = key, Model = saved.Model, TimeoutSeconds = saved.TimeoutSeconds.ToString() };
            }
            catch (Exception error)
            {
                throw new ChatException("无法读取模型设置，请右键小人 → 设置模型，重新填写并保存。", "settings_read_failed", error);
            }
        }
        internal static void Save(string folder, ModelSettings settings)
        {
            ChatConfig validated = ChatConfig.Validate(settings);
            var saved = new SavedModelSettings
            {
                BaseUrl = settings.BaseUrl.Trim(), Model = validated.Model, TimeoutSeconds = validated.TimeoutSeconds,
                EncryptedApiKey = String.IsNullOrEmpty(validated.ApiKey) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(validated.ApiKey), Entropy, DataProtectionScope.CurrentUser))
            };
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, FileName), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(saved), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
