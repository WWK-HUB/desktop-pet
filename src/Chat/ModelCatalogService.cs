using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DesktopPet.Chat
{
    // Model discovery is separate from chat/history and from WinForms controls.
    internal sealed class ModelCatalogService
    {
        private readonly ChatLog log;
        internal ModelCatalogService(ChatLog log = null) { this.log = log ?? new ChatLog(); }
        internal async Task<List<string>> ListAsync(ModelSettings settings, CancellationToken cancellation)
        {
            try
            {
                return await Task.Run(async delegate
                {
                    cancellation.ThrowIfCancellationRequested();
                    ChatConfig config = ChatConfig.Validate(settings, false);
                    string chatEndpoint = config.Endpoint.AbsoluteUri;
                    Uri endpoint = new Uri(chatEndpoint.Substring(0, chatEndpoint.Length - "/chat/completions".Length) + "/models");
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
                    {
                        client.Timeout = Timeout.InfiniteTimeSpan;
                        timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
                        if (!String.IsNullOrWhiteSpace(config.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
                        try
                        {
                            using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            {
                                if (!response.IsSuccessStatusCode) throw HttpError((int)response.StatusCode);
                                string json;
                                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                using (timeout.Token.Register(delegate { stream.Dispose(); }))
                                using (var buffer = new MemoryStream())
                                {
                                    byte[] chunk = new byte[8192]; int count;
                                    while ((count = await stream.ReadAsync(chunk, 0, chunk.Length, timeout.Token).ConfigureAwait(false)) > 0)
                                    {
                                        if (buffer.Length + count > 524288) throw new ChatException("模型列表过大，请手动填写模型 ID。", "models_response_too_large");
                                        buffer.Write(chunk, 0, count);
                                    }
                                    json = Encoding.UTF8.GetString(buffer.ToArray());
                                }
                                cancellation.ThrowIfCancellationRequested();
                                return Parse(json);
                            }
                        }
                        catch (Exception error)
                        {
                            if (cancellation.IsCancellationRequested) throw new OperationCanceledException("Cancelled", error, cancellation);
                            if (timeout.IsCancellationRequested) throw new ChatException("获取模型超时，请重试；也可以直接填写模型 ID。", "models_timeout", error);
                            if (error is HttpRequestException || error is IOException)
                                throw new ChatException("连接失败，请检查地址、网络或本地服务是否启动。", "models_network_error", error);
                            throw;
                        }
                    }
                }, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (ChatException error) { log.Write(error, settings.ApiKey); throw; }
            catch (Exception error)
            {
                var friendly = new ChatException("未能获取模型列表，请重试或手动填写模型 ID。", "models_unexpected_error", error);
                log.Write(friendly, settings.ApiKey); throw friendly;
            }
        }
        private static List<string> Parse(string text)
        {
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = 524288, RecursionLimit = 32 };
                var root = json.DeserializeObject(text) as Dictionary<string, object>;
                object raw;
                var data = root != null && root.TryGetValue("data", out raw) ? raw as object[] : null;
                if (data == null) throw new ChatException("返回的模型列表格式不兼容，可以手动填写模型 ID。", "models_invalid_response");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (object item in data)
                {
                    var model = item as Dictionary<string, object>;
                    string id = model != null && model.TryGetValue("id", out raw) ? raw as string : null;
                    if (String.IsNullOrWhiteSpace(id) || id.Length > 256) continue;
                    bool control = false; foreach (char c in id) if (Char.IsControl(c)) { control = true; break; }
                    if (!control) ids.Add(id.Trim());
                    if (ids.Count > 5000) throw new ChatException("模型列表过大，请手动填写模型 ID。", "models_response_too_large");
                }
                if (ids.Count == 0) throw new ChatException("服务商没有返回可选模型，可重试或手动填写模型 ID。", "models_empty");
                var result = new List<string>(ids); result.Sort(StringComparer.OrdinalIgnoreCase); return result;
            }
            catch (ChatException) { throw; }
            catch (Exception error) { throw new ChatException("返回的模型列表格式不兼容，可以手动填写模型 ID。", "models_invalid_response", error); }
        }
        private static ChatException HttpError(int status)
        {
            string message;
            switch (status)
            {
                case 401: message = "API Key 不正确或已失效，请修改后重新获取。"; break;
                case 403: message = "没有读取模型列表的权限，可检查密钥权限或手动填写。"; break;
                case 404: case 405: message = "未找到模型列表接口，请检查地址，或手动填写模型 ID。"; break;
                case 429: message = "请求过于频繁或额度受限，请稍后重试。"; break;
                default: message = status >= 500 ? "服务商暂时无法提供模型列表，请稍后重试。" : "获取模型列表失败（HTTP " + status + "），可手动填写模型 ID。"; break;
            }
            return new ChatException(message, "models_http_" + status);
        }
    }
}
