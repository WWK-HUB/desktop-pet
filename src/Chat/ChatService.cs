using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopPet.Chat
{
    internal sealed class ChatService
    {
        internal const int MaxInputCharacters = 1000;
        internal const int MaxReplyCharacters = 12000;
        internal const int MaxHistoryMessages = 12; // Six complete user/assistant turns.
        internal const int MaxHistoryCharacters = 24000;
        private readonly Func<ChatConfig> loadConfig;
        private readonly Func<ChatConfig, IChatProvider> createProvider;
        private readonly ISystemPromptSource prompts;
        private readonly ChatLog log;
        private readonly List<ChatMessage> history = new List<ChatMessage>();
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private string conversationTarget;
        private int contextRevision;
        internal int ContextTurns = 6;
        internal int ContextCharacterLimit = MaxHistoryCharacters;
        internal void InvalidateContext() { Interlocked.Increment(ref contextRevision); }
        internal int HistoryCount { get { return history.Count; } }

        internal ChatService(string directory) : this(() => ChatConfig.Load(directory), c => new OpenAiCompatibleProvider(c), new DefaultSystemPromptSource(), new ChatLog()) { }
        internal ChatService(string directory, ISystemPromptSource prompts) : this(() => ChatConfig.Load(directory), c => new OpenAiCompatibleProvider(c), prompts, new ChatLog()) { }
        internal ChatService(Func<ChatConfig> config, Func<ChatConfig, IChatProvider> provider, ISystemPromptSource prompts, ChatLog log)
        { loadConfig = config; createProvider = provider; this.prompts = prompts; this.log = log; }

        internal async Task<string> SendAsync(string input, CancellationToken cancellation)
        {
            input = (input ?? "").Trim();
            if (input.Length == 0) throw new ChatException("先写点什么再发送吧。", "input_empty");
            if (input.Length > MaxInputCharacters) throw new ChatException("这次先聊短一点，每条最多 1000 字。", "input_too_long");
            if (!await gate.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new ChatException("上一条消息还在回复中，请稍等。", "request_already_running");
            ChatConfig config = null;
            int revision = contextRevision;
            try
            {
                // Includes configuration/JSON work and legacy HTTP setup in the worker task.
                // The WinForms caller resumes on its own UI synchronization context.
                return await Task.Run(async delegate
                {
                    cancellation.ThrowIfCancellationRequested();
                    config = loadConfig();
                    string systemPrompt = prompts.GetSystemPrompt();
                    string target = config.Endpoint.AbsoluteUri + "\n" + config.Model + "\n" + systemPrompt + "\n" + revision;
                    if (target != conversationTarget) { history.Clear(); conversationTarget = target; }
                    var request = new List<ChatMessage> { new ChatMessage("system", systemPrompt) };
                    int start = history.Count, characters = 0, turns = Math.Max(0, Math.Min(6, ContextTurns));
                    while (start >= 2 && (history.Count - start) / 2 < turns)
                    {
                        int size = history[start - 2].content.Length + history[start - 1].content.Length;
                        if (characters + size > ContextCharacterLimit) break;
                        characters += size; start -= 2;
                    }
                    request.AddRange(history.GetRange(start, history.Count - start));
                    request.Add(new ChatMessage("user", input));
                    string answer;
                    using (IChatProvider provider = createProvider(config)) answer = await provider.CompleteAsync(request, cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    if (revision != contextRevision || prompts.GetSystemPrompt() != systemPrompt)
                    {
                        history.Clear(); conversationTarget = null;
                        throw new ChatException("人设已更新，这条旧设定下的回复已作废，请重新发送。", "persona_changed");
                    }
                    if (String.IsNullOrWhiteSpace(answer)) throw new ChatException("AI 没有返回文字，请再试一次。", "provider_empty_answer");
                    if (answer.Length > MaxReplyCharacters) throw new ChatException("这次回复太长了，请让 AI 简短回答。", "reply_too_long");
                    // Commit complete turns only. Failures/cancellation never leave an unmatched user message.
                    history.Add(new ChatMessage("user", input)); history.Add(new ChatMessage("assistant", answer));
                    while (history.Count > MaxHistoryMessages || HistoryCharacters() > MaxHistoryCharacters) history.RemoveRange(0, 2);
                    return answer;
                }, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (ChatException e) { log.Write(e, config == null ? null : config.ApiKey); throw; }
            catch (Exception e)
            {
                var friendly = new ChatException("AI 请求没有成功，请在「设置模型」中检查，详细原因已记录到日志。", "unexpected_chat_error", e);
                log.Write(friendly, config == null ? null : config.ApiKey); throw friendly;
            }
            finally { gate.Release(); }
        }
        private int HistoryCharacters()
        {
            int total = 0; foreach (ChatMessage message in history) total += message.content.Length;
            return total;
        }
    }
}
