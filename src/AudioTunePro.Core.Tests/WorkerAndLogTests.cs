using AudioTunePro.Core.Models;
using AudioTunePro.Core.Services;

namespace AudioTunePro.Core.Tests;

public class LatestWinsWorkerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

    [Fact]
    public void A_submitted_item_is_handled_off_the_calling_thread()
    {
        int handlerThread = -1;
        using var worker = new LatestWinsWorker<int>(_ => handlerThread = Environment.CurrentManagedThreadId, "test");

        worker.Submit(1);

        Assert.True(worker.Flush(Generous));
        Assert.NotEqual(-1, handlerThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, handlerThread);
    }

    [Fact]
    public void While_busy_only_the_newest_pending_item_survives()
    {
        var seen = new List<int>();
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var worker = new LatestWinsWorker<int>(i =>
        {
            lock (seen) seen.Add(i);
            if (i == 1) { started.Set(); release.Wait(Generous); }
        }, "test");

        worker.Submit(1);
        Assert.True(started.Wait(Generous));
        worker.Submit(2);
        worker.Submit(3);
        worker.Submit(4);
        release.Set();

        Assert.True(worker.Flush(Generous));
        Assert.Equal(new[] { 1, 4 }, seen);
    }

    [Fact]
    public void Flush_times_out_while_the_handler_is_stuck_and_succeeds_once_it_finishes()
    {
        var release = new ManualResetEventSlim();
        using var worker = new LatestWinsWorker<int>(_ => release.Wait(Generous), "test");

        worker.Submit(1);

        Assert.False(worker.Flush(TimeSpan.FromMilliseconds(100)));
        release.Set();
        Assert.True(worker.Flush(Generous));
    }

    [Fact]
    public void Flush_with_nothing_queued_returns_immediately()
    {
        using var worker = new LatestWinsWorker<int>(_ => { }, "test");

        Assert.True(worker.Flush(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void A_throwing_handler_does_not_stop_later_items()
    {
        var handled = new List<int>();
        using var worker = new LatestWinsWorker<int>(i =>
        {
            if (i == 1) throw new InvalidOperationException("boom");
            lock (handled) handled.Add(i);
        }, "test");

        worker.Submit(1);
        Assert.True(worker.Flush(Generous));
        worker.Submit(2);
        Assert.True(worker.Flush(Generous));

        Assert.Equal(new[] { 2 }, handled);
    }

    [Fact]
    public void Items_submitted_after_dispose_are_ignored_but_a_queued_item_is_still_processed()
    {
        var handled = new List<int>();
        var release = new ManualResetEventSlim();
        var started = new ManualResetEventSlim();
        var worker = new LatestWinsWorker<int>(i =>
        {
            lock (handled) handled.Add(i);
            if (i == 1) { started.Set(); release.Wait(Generous); }
        }, "test");

        worker.Submit(1);
        Assert.True(started.Wait(Generous));
        worker.Submit(2);   // queued while 1 runs
        worker.Dispose();
        worker.Submit(3);   // too late
        release.Set();

        Assert.True(worker.Flush(Generous));
        Assert.Equal(new[] { 1, 2 }, handled);
    }
}

public sealed class AppLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "atp-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Entries_are_written_with_level_message_and_exception()
    {
        var log = new AppLog(_dir);

        log.Write("ERROR", "something broke", new InvalidOperationException("detail here"));

        var text = File.ReadAllText(log.FilePath);
        Assert.Contains("ERROR something broke", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("detail here", text);
    }

    [Fact]
    public void The_log_rolls_over_instead_of_growing_without_bound()
    {
        var log = new AppLog(_dir);
        var line = new string('x', 1000);

        for (int i = 0; i < (AppLog.MaxBytes / 1000) + 20; i++) log.Write("INFO", line, null);

        Assert.True(File.Exists(log.FilePath + ".1"));
        Assert.True(new FileInfo(log.FilePath).Length < AppLog.MaxBytes);
    }

    [Fact]
    public void Writing_to_an_unusable_location_never_throws()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "atp-log-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "a file where the log folder should be");
        try
        {
            new AppLog(Path.Combine(blocker, "logs")).Write("ERROR", "cannot be written", new Exception("x"));
        }
        finally { File.Delete(blocker); }
    }

    [Fact]
    public void Static_helpers_do_nothing_until_a_log_is_configured_and_then_write()
    {
        var previous = AppLog.Current;
        try
        {
            AppLog.Current = null;
            AppLog.Error("ignored"); // must not throw

            AppLog.Current = new AppLog(_dir);
            AppLog.Warn("hello", null);

            Assert.Contains("WARN hello", File.ReadAllText(AppLog.Current.FilePath));
        }
        finally { AppLog.Current = previous; }
    }
}

public sealed class SettingsTwoStepSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "atp-save-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Serialize_then_write_round_trips_like_a_direct_save()
    {
        var store = new AppDataStore(_dir);
        var settings = new AppSettings { ActivePresetName = "Two step", ShowLevelMeter = false };

        var json = store.SerializeSettings(settings);
        settings.ActivePresetName = "changed after the snapshot"; // the snapshot must not follow later edits
        Assert.True(store.WriteSettings(json));

        var loaded = store.LoadSettings();
        Assert.Equal("Two step", loaded.ActivePresetName);
        Assert.False(loaded.ShowLevelMeter);
    }

    [Fact]
    public void Writing_to_an_unwritable_folder_reports_false()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "atp-save-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a folder");
        try
        {
            Assert.False(new AppDataStore(blocker).WriteSettings("{}"));
        }
        finally { File.Delete(blocker); }
    }
}
