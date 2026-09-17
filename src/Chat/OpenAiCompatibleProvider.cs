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
    internal sealed class OpenAiCompatibleProvider : IChatProvider
    {
        private readonly ChatConfig config;
        private readonly HttpClient client;
        internal OpenAiCompatibleProvider(ChatConfig config)
        {
            this.config = config;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // Do not forward a credential or request body to a redirect destination.
            client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            client.Timeout = Timeout.InfiniteTimeSpan;
        }
        public async Task<string> CompleteAsync(IList<ChatMessage> messages, CancellationToken cancellation)
        {
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 524288, RecursionLimit = 32 };
            string body = json.Serialize(new { model = config.Model, messages = messages, stream = false });
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
                if (!String.IsNullOrWhiteSpace(config.ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                try
                {
                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) throw HttpError((int)response.StatusCode);
                        string result;
                        using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (timeout.Token.Register(delegate { stream.Dispose(); }))
                        using (var buffer = new MemoryStream())
                        {
                            byte[] chunk = new byte[8192]; int count;
                            while ((count = await stream.ReadAsync(chunk, 0, chunk.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                if (buffer.Length + count > 524288) throw new ChatException("接口返回的内容过大，请换个更简短的问题。", "response_too_large");
                                buffer.Write(chunk, 0, count);
                            }
                            result = Encoding.UTF8.GetString(buffer.ToArray());
                        }
                        try
                        {
                            var data = json.DeserializeObject(result) as Dictionary<string, object>;
                            object raw;
                            var choices = data != null && data.TryGetValue("choices", out raw) ? raw as object[] : null;
                            var choice = choices != null && choices.Length > 0 ? choices[0] as Dictionary<string, object> : null;
                            var message = choice != null && choice.TryGetValue("message", out raw) ? raw as Dictionary<string, object> : null;
                            string answer = message != null && message.TryGetValue("content", out raw) ? raw as string : null;
                            if (String.IsNullOrWhiteSpace(answer)) throw new ChatException("接口没有返回文字，请检查模型是否支持文字聊天。", "response_missing_content");
                            return answer.Trim();
                        }
                        catch (ChatException) { throw; }
                        catch (Exception e) { throw new ChatException("接口返回格式异常，请检查 Base URL 和模型的兼容性。", "response_invalid_json", e); }
                    }
                }
                catch (OperationCanceledException e)
                {
                    if (cancellation.IsCancellationRequested) throw;
                    throw new ChatException("等得有点久，请稍后再试，或调大请求超时时间。", "request_timeout", e);
                }
                catch (Exception e)
                {
                    if (cancellation.IsCancellationRequested) throw new OperationCanceledException("Cancelled", e, cancellation);
                    if (timeout.IsCancellationRequested) throw new ChatException("等得有点久，请稍后再试，或调大请求超时时间。", "request_timeout", e);
                    if (e is HttpRequestException) throw new ChatException("连接 AI 失败，请检查网络、Base URL 或本地模型是否已启动。", "network_error", e);
                    if (e is IOException) throw new ChatException("读取 AI 回复时连接中断，请稍后再试。", "response_read_error", e);
                    throw;
                }
            }
        }
        private static ChatException HttpError(int status)
        {
            string message;
            switch (status)
            {
                case 401: message = "API Key 不正确或已失效，请在「设置模型」中检查。"; break;
                case 403: message = "接口拒绝访问，请检查 API Key 的权限。"; break;
                case 404: message = "没有找到接口或模型，请检查 Base URL 和 Model。"; break;
                case 400: case 422: message = "接口不接受这个请求，请检查 Model 和接口兼容性。"; break;
                case 429: message = "请求过于频繁或额度不足，请稍后再试并检查账户额度。"; break;
                default: message = status >= 500 ? "AI 服务暂时不可用，请稍后再试。" : "接口请求失败，请检查 API 配置（HTTP " + status + "）。"; break;
            }
            return new ChatException(message, "http_status_" + status);
        }
        public void Dispose() { client.Dispose(); }
    }
}
