using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopPet.Chat
{
    internal sealed class ChatMessage
    {
        public string role { get; private set; }
        public string content { get; private set; }
        internal ChatMessage(string role, string content) { this.role = role; this.content = content; }
    }

    internal interface IChatProvider : IDisposable
    {
        Task<string> CompleteAsync(IList<ChatMessage> messages, CancellationToken cancellation);
    }

    // CharacterPersonaPromptSource loads persona.json behind this boundary; UI and providers stay independent.
    internal interface ISystemPromptSource { string GetSystemPrompt(); }
    internal sealed class DefaultSystemPromptSource : ISystemPromptSource
    {
        public string GetSystemPrompt() { return "你是一个桌面 AI 助手，请使用自然、简短的语言回答用户。"; }
    }

    internal sealed class ChatException : Exception
    {
        internal readonly string UserMessage;
        internal readonly string Diagnostic;
        internal ChatException(string message, string diagnostic, Exception inner = null) : base(message, inner)
        { UserMessage = message; Diagnostic = diagnostic; }
    }
}
