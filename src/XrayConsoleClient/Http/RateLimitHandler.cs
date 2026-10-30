using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XrayConsoleClient;

// Xray Cloud limits every Jira instance to 300 calls per 5 minutes (Standard) or 1000 (Enterprise),
// shared by all callers, and answers 429 with { error: { nextValidRequestDate } } when it is exceeded.
// https://docs.getxray.app/space/ProductKB/46268656/[Xray+Cloud]+Rest+API+Limit
// Spaces the requests at least minInterval apart and retries the ones rejected with 429 once the
// time Xray asks for has passed.
sealed class RateLimitHandler : DelegatingHandler
{
    public const int MaxRetries = 5;
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(6);
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(1);

    private readonly TimeSpan minInterval;
    private readonly TimeProvider time;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Lock slotLock = new();
    private DateTimeOffset nextSlot = DateTimeOffset.MinValue;

    public RateLimitHandler(TimeSpan minInterval, TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.minInterval = minInterval;
        this.time = time ?? TimeProvider.System;
        this.delay = delay ?? ((wait, cancellationToken) => Task.Delay(wait, cancellationToken));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await WaitForSlotAsync(cancellationToken);

            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(AttemptTimeout);
            var response = await base.SendAsync(request, attemptTimeout.Token);
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var wait = Max(WaitTime(body, response.Headers.RetryAfter, attempt, time.GetUtcNow()), TimeSpan.Zero) + Margin;
            if (attempt >= MaxRetries || wait > MaxWait)
            {
                return response;
            }

            response.Dispose();
            Console.WriteLine($"Xray rate limit reached, retrying in {Math.Ceiling(wait.TotalSeconds)}s (retry {attempt + 1} of {MaxRetries})");
            await delay(wait, cancellationToken);
        }
    }

    internal static TimeSpan WaitTime(string? body, RetryConditionHeaderValue? retryAfter, int attempt, DateTimeOffset now)
    {
        if (ReadNextValidRequestDate(body) is { } nextValidRequestDate)
        {
            return nextValidRequestDate - now;
        }

        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            return date - now;
        }

        return TimeSpan.FromSeconds(5 * Math.Pow(2, attempt));
    }

    private async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (slotLock)
        {
            var now = time.GetUtcNow();
            var slot = now > nextSlot ? now : nextSlot;
            nextSlot = slot + minInterval;
            wait = slot - now;
        }

        if (wait > TimeSpan.Zero)
        {
            await delay(wait, cancellationToken);
        }
    }

    private static DateTimeOffset? ReadNextValidRequestDate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body) is JsonObject root
                && root["error"] is JsonObject error
                && XrayJson.Text(error["nextValidRequestDate"]) is { } value
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                    ? date
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}
