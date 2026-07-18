using System.Collections.Concurrent;
using VisionPaint.Services;

namespace VisionPaint.Tests.Infrastructure;

public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Html, string? Text)> Sent { get; } = new();

    public void Clear()
    {
        while (Sent.TryDequeue(out _))
        {
        }
    }

    public Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((toEmail, subject, htmlBody, textBody));
        return Task.CompletedTask;
    }
}
