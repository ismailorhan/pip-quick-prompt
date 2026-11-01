namespace QuickPrompt.Tests
{
    using System.Text.Json;

    using QuickPrompt.Core;

    using Xunit;

    public class AppControlTests
    {
        [Fact]
        public async Task EventsArriveInOrderAndSinceSkipsSeenOnes()
        {
            var control = new AppControl("t");
            control.Publish(new { type = "open", url = "a" });
            control.Publish(new { type = "keys", action = "copy" });

            using var all = JsonDocument.Parse(await control.WaitForEventsAsync(0, TimeSpan.FromSeconds(1), CancellationToken.None));
            Assert.Equal(2, all.RootElement.GetProperty("last").GetInt64());
            Assert.Equal("open", all.RootElement.GetProperty("events")[0].GetProperty("type").GetString());

            using var rest = JsonDocument.Parse(await control.WaitForEventsAsync(1, TimeSpan.FromSeconds(1), CancellationToken.None));
            Assert.Single(rest.RootElement.GetProperty("events").EnumerateArray());
        }

        [Fact]
        public async Task LongPollWakesUpOnPublish()
        {
            var control = new AppControl("t");
            var wait = control.WaitForEventsAsync(0, TimeSpan.FromSeconds(10), CancellationToken.None);
            await Task.Delay(100);
            Assert.False(wait.IsCompleted);
            control.Publish(new { type = "notify" });
            var finished = await Task.WhenAny(wait, Task.Delay(3000));
            Assert.Same(wait, finished);
            using var doc = JsonDocument.Parse(await wait);
            Assert.Equal(1, doc.RootElement.GetProperty("last").GetInt64());
        }

        [Fact]
        public async Task WithoutManagerReportsStarting()
        {
            var control = new AppControl("t");
            var (status, _) = await control.HandleAsync("GET", "status", "", new Dictionary<string, string>(), CancellationToken.None);
            Assert.Equal(503, status);
        }
    }
}
