using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DesktopPet.Chat
{
    internal sealed class ChatConfig
    {
        internal Uri Endpoint;
        internal string ApiKey;
        internal string Model;
        internal int TimeoutSeconds = 45;

        // Called on sending, never during pet startup. Missing configuration cannot stop the pet.
        internal static ChatConfig Load(string folder)
        {
            ModelSettings settings = ModelSettingsStore.Read(folder);
            return Validate(settings);
        }
        internal static ModelSettings ReadLegacy(string folder)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string path = Path.Combine(folder, ".env");
            if (File.Exists(path))
            {
                int number = 0;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    number++;
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int equals = line.IndexOf('=');
                    if (equals < 1) throw new ChatException(".env 格式有误，请检查第 " + number + " 行。", "config_syntax_line_" + number);
                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();
                    if (value.StartsWith("\"") || value.StartsWith("'"))
                    {
                        char quote = value[0];
                        int end = value.IndexOf(quote, 1);
                        if (end < 0 || (value.Substring(end + 1).Trim().Length > 0 && !value.Substring(end + 1).Trim().StartsWith("#")))
                            throw new ChatException(".env 的引号有误，请检查第 " + number + " 行。", "config_quote_line_" + number);
                        value = value.Substring(1, end - 1);
                    }
                    else
                    {
                        int comment = value.IndexOf(" #", StringComparison.Ordinal);
                        if (comment >= 0) value = value.Substring(0, comment).TrimEnd();
                    }
                    values[key] = value;
                }
            }
            return new ModelSettings { BaseUrl = Read(values, "AI_BASE_URL"), ApiKey = Read(values, "AI_API_KEY"), Model = Read(values, "AI_MODEL"), TimeoutSeconds = Read(values, "AI_TIMEOUT_SECONDS") };
        }
        internal static ChatConfig Validate(ModelSettings settings, bool requireModel = true)
        {
            ChatConfig result = new ChatConfig();
            string baseUrl = (settings.BaseUrl ?? "").Trim();
            result.ApiKey = (settings.ApiKey ?? "").Trim();
            result.Model = (settings.Model ?? "").Trim();
            if (String.IsNullOrWhiteSpace(baseUrl) || (requireModel && String.IsNullOrWhiteSpace(result.Model)))
                throw new ChatException("还没配置 AI。请右键小人 → 设置模型，填写 Base URL、API Key 和模型名称。", "config_missing");
            Uri uri;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new ChatException("Base URL 格式不正确。请填写 http(s) API 地址，不带密钥或查询参数。", "config_invalid_url");
            string endpoint = uri.AbsoluteUri.TrimEnd('/');
            if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) endpoint += "/chat/completions";
            result.Endpoint = new Uri(endpoint);
            if (String.IsNullOrWhiteSpace(result.ApiKey) && !uri.IsLoopback)
                throw new ChatException("还没填写 API Key，请右键小人 → 设置模型。", "config_missing_key");
            if (result.ApiKey.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
                throw new ChatException("API Key 必须写在同一行。", "config_invalid_key");
            string timeout = settings.TimeoutSeconds ?? "";
            if (timeout.Length > 0 && (!Int32.TryParse(timeout, out result.TimeoutSeconds) || result.TimeoutSeconds < 1 || result.TimeoutSeconds > 180))
                throw new ChatException("等待超时请填写 1 到 180 之间的秒数。", "config_invalid_timeout");
            return result;
        }
        private static string Read(Dictionary<string, string> values, string name)
        {
            // Use only our own explicit AI_* variables; never silently consume other apps' keys.
            string environment = Environment.GetEnvironmentVariable(name);
            if (environment != null) return environment.Trim();
            string value;
            return values.TryGetValue(name, out value) ? value : "";
        }
    }
}
