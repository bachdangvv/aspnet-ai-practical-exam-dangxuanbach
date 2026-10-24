namespace SmartShop.Api.Services;

/// <summary>Single-process history: 1,000 sessions, 30-minute idle expiry, 20 complete turns.</summary>
public sealed class ChatSessionStore(TimeProvider clock)
{
    private sealed class Session
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public List<ChatTurn> History { get; } = [];
        public DateTimeOffset LastUsed { get; set; }
        public int ActiveUsers { get; set; }
    }

    private readonly object sync = new();
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);

    public async Task<string> ReplyAsync(string sessionId, string message,
        Func<IReadOnlyList<ChatTurn>, Task<string>> generate, CancellationToken cancellationToken)
    {
        Session session;
        lock (sync)
        {
            var now = clock.GetUtcNow();
            foreach (var key in sessions.Where(pair => pair.Value.ActiveUsers == 0 &&
                         now - pair.Value.LastUsed >= TimeSpan.FromMinutes(30)).Select(pair => pair.Key).ToArray())
            {
                sessions[key].Gate.Dispose();
                sessions.Remove(key);
            }
            if (!sessions.TryGetValue(sessionId, out session!))
            {
                if (sessions.Count >= 1000)
                    throw new AiException(503, "chat_capacity_reached", "Chat is busy. Please try again later.");
                session = new Session { LastUsed = now };
                sessions.Add(sessionId, session);
            }
            session.ActiveUsers++;
        }

        try
        {
            await session.Gate.WaitAsync(cancellationToken);
            try
            {
                var turns = session.History.Append(new ChatTurn("user", message)).ToArray();
                var reply = await generate(turns);
                session.History.Add(new ChatTurn("user", message));
                session.History.Add(new ChatTurn("model", reply));
                if (session.History.Count > 40)
                    session.History.RemoveRange(0, session.History.Count - 40);
                return reply;
            }
            finally
            {
                session.Gate.Release();
            }
        }
        finally
        {
            lock (sync)
            {
                session.ActiveUsers--;
                session.LastUsed = clock.GetUtcNow();
            }
        }
    }
}
