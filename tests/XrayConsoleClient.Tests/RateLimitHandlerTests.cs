using System.Net;
using System.Net.Http.Headers;

namespace XrayConsoleClient.Tests;

public class RateLimitHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WaitTime_prefers_next_valid_request_date_from_xray()
    {
        var body = """{"error":{"nextValidRequestDate":"2026-10-30T09:00:42.000Z"}}""";
        Assert.Equal(TimeSpan.FromSeconds(42), RateLimitHandler.WaitTime(body, new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)), 0, Now));
    }

    [Fact]
    public void WaitTime_falls_back_to_retry_after_then_backoff()
    {
        Assert.Equal(TimeSpan.FromSeconds(7), RateLimitHandler.WaitTime("not json", new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)), 0, Now));
        Assert.Equal(TimeSpan.FromSeconds(20), RateLimitHandler.WaitTime("", null, 2, Now));
    }

    [Fact]
    public async Task Retries_429_after_the_wait_xray_asks_for()
    {
        var calls = 0;
        var inner = new FakeHttpHandler(_ => ++calls == 1
            ? FakeHttpHandler.Json("""{"error":{"nextValidRequestDate":"2000-01-01T00:00:00Z"}}""", HttpStatusCode.TooManyRequests)
            : FakeHttpHandler.Json("{}"));
        var waits = new List<TimeSpan>();
        using var http = new HttpClient(new RateLimitHandler(TimeSpan.Zero, delay: (wait, _) => { waits.Add(wait); return Task.CompletedTask; }) { InnerHandler = inner });

        using var response = await http.GetAsync("https://xray.example/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1)], waits);
    }

    [Fact]
    public async Task Gives_up_after_max_retries_and_returns_the_429()
    {
        var inner = new FakeHttpHandler(_ => FakeHttpHandler.Json("{}", HttpStatusCode.TooManyRequests));
        using var http = new HttpClient(new RateLimitHandler(TimeSpan.Zero, delay: (_, _) => Task.CompletedTask) { InnerHandler = inner });

        using var response = await http.GetAsync("https://xray.example/api");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(RateLimitHandler.MaxRetries + 1, inner.Requests.Count);
    }

    [Fact]
    public async Task Does_not_wait_longer_than_max_wait()
    {
        var inner = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"error":{"nextValidRequestDate":"2999-01-01T00:00:00Z"}}""", HttpStatusCode.TooManyRequests));
        using var http = new HttpClient(new RateLimitHandler(TimeSpan.Zero, delay: (_, _) => Task.CompletedTask) { InnerHandler = inner });

        using var response = await http.GetAsync("https://xray.example/api");

        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Spaces_requests_min_interval_apart()
    {
        var inner = new FakeHttpHandler(_ => FakeHttpHandler.Json("{}"));
        var waits = new List<TimeSpan>();
        var time = new FrozenTime(Now);
        using var http = new HttpClient(new RateLimitHandler(TimeSpan.FromSeconds(1), time, (wait, _) => { waits.Add(wait); return Task.CompletedTask; }) { InnerHandler = inner });

        for (var i = 0; i < 3; i++)
        {
            using var response = await http.GetAsync("https://xray.example/api");
        }

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], waits);
    }

    private sealed class FrozenTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
