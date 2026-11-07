using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Keynote.Infrastructure;
using Xunit;

namespace Keynote.Tests;

public class PresenceReportingServiceTests
{
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        public int HeartbeatCount { get; private set; }
        public int EndSessionCount { get; private set; }
        public bool SimulateFailure { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (SimulateFailure)
            {
                throw new HttpRequestException("Simulated connection timeout");
            }

            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/api/app/sessions/heartbeat"))
            {
                HeartbeatCount++;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"heartbeat_interval_sec\":45}")
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/app/sessions/end"))
            {
                EndSessionCount++;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\"}")
                };
                return Task.FromResult(response);
            }

            if (path.EndsWith("/api/app/announcements"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"id\":\"ann-1\",\"title\":\"Update\",\"message\":\"Body\",\"audience\":\"all\",\"published_at\":null,\"expires_at\":null}]")
                };
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task Heartbeat_NotSent_WhenConsentNotGranted()
    {
        var handler = new MockHttpHandler();
        using var client = new HttpClient(handler);
        var service = new PresenceReportingService(client, "http://127.0.0.1:8088", "v1.1.0-beta.1");

        // Consent is false by default (Guest mode)
        Assert.False(service.IsConsentGranted);

        await service.StartAsync();
        // Give background loop a brief moment
        await Task.Delay(50);
        await service.StopAsync();

        Assert.Equal(0, handler.HeartbeatCount);
        Assert.Equal(0, handler.EndSessionCount);
    }

    [Fact]
    public async Task Heartbeat_Sent_WhenConsentGranted()
    {
        var handler = new MockHttpHandler();
        using var client = new HttpClient(handler);
        var service = new PresenceReportingService(client, "http://127.0.0.1:8088", "v1.1.0-beta.1")
        {
            IsConsentGranted = true,
            CurrentStatus = "playing"
        };

        await service.StartAsync();
        await Task.Delay(100);
        await service.StopAsync();

        Assert.True(handler.HeartbeatCount >= 1);
        Assert.Equal(1, handler.EndSessionCount);
    }

    [Fact]
    public async Task NetworkFailure_DoesNotThrow_LocalPlaybackPreserved()
    {
        var handler = new MockHttpHandler { SimulateFailure = true };
        using var client = new HttpClient(handler);
        var service = new PresenceReportingService(client, "http://127.0.0.1:8088", "v1.1.0-beta.1")
        {
            IsConsentGranted = true
        };

        // None of these should throw exceptions
        await service.StartAsync();
        await Task.Delay(50);
        await service.StopAsync();

        var announcements = await service.GetAnnouncementsAsync();
        Assert.Empty(announcements);
    }

    [Fact]
    public async Task GetAnnouncements_DeserializesCleanly()
    {
        var handler = new MockHttpHandler();
        using var client = new HttpClient(handler);
        var service = new PresenceReportingService(client, "http://127.0.0.1:8088", "v1.1.0-beta.1");

        var list = await service.GetAnnouncementsAsync();
        Assert.Single(list);
        Assert.Equal("ann-1", list[0].Id);
        Assert.Equal("Update", list[0].Title);
    }
}
