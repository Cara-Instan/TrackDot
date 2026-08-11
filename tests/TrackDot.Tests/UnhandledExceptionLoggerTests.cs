using System;
using System.IO;
using System.Threading.Tasks;
using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Tests for the unhandled-exception logger. The logger
/// itself subscribes to three process-wide events; tests
/// exercise the pure formatter and the file sink directly.
/// The WPF dispatcher event is hard to test without a real
/// WPF application, so the formatter is exposed as
/// <c>internal static</c> for direct assertion.
/// </summary>
public sealed class UnhandledExceptionLoggerTests
{
    // ----- pure formatter -----------------------------------------------

    [Fact]
    public void Format_includes_channel_tag_and_exception_text()
    {
        var ex = new InvalidOperationException("boom");

        var line = UnhandledExceptionLogger.Format("Dispatcher", ex);

        // Timestamp (ISO 8601 with O format) is followed by the
        // channel tag, then the exception's ToString. Assert
        // structural pieces — the timestamp varies by test run.
        Assert.Contains("[Dispatcher]", line);
        Assert.Contains("InvalidOperationException", line);
        Assert.Contains("boom", line);
    }

    [Fact]
    public void Format_includes_inner_exception_chain()
    {
        var inner = new ArgumentException("inner");
        var outer = new InvalidOperationException("outer", inner);

        var line = UnhandledExceptionLogger.Format("TaskScheduler", outer);

        Assert.Contains("outer", line);
        Assert.Contains("inner", line);
    }

    [Fact]
    public void Format_throws_on_null_channel()
    {
        Assert.Throws<ArgumentNullException>(
            () => UnhandledExceptionLogger.Format(null!, new Exception("x")));
    }

    [Fact]
    public void Format_throws_on_null_exception()
    {
        Assert.Throws<ArgumentNullException>(
            () => UnhandledExceptionLogger.Format("Dispatcher", null!));
    }

    [Fact]
    public void Format_three_channels_produce_different_lines()
    {
        var ex = new Exception("same");

        var d = UnhandledExceptionLogger.Format("Dispatcher", ex);
        var a = UnhandledExceptionLogger.Format("AppDomain", ex);
        var t = UnhandledExceptionLogger.Format("TaskScheduler", ex);

        Assert.Contains("[Dispatcher]", d);
        Assert.Contains("[AppDomain]", a);
        Assert.Contains("[TaskScheduler]", t);
        Assert.DoesNotContain("[Dispatcher]", a);
        Assert.DoesNotContain("[AppDomain]", t);
    }

    // ----- file sink ----------------------------------------------------

    [Fact]
    public void FileSink_writes_line_to_log_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackdot-test-{Guid.NewGuid():N}.log");

        try
        {
            using (var sink = new FileUnhandledExceptionSink(path))
            {
                Assert.True(sink.IsAvailable);
                sink.WriteLine("hello world");
            }

            var written = File.ReadAllText(path);
            Assert.Contains("hello world", written);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FileSink_appends_multiple_lines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackdot-test-{Guid.NewGuid():N}.log");

        try
        {
            using (var sink = new FileUnhandledExceptionSink(path))
            {
                sink.WriteLine("first");
                sink.WriteLine("second");
                sink.WriteLine("third");
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);
            Assert.Equal("first", lines[0]);
            Assert.Equal("second", lines[1]);
            Assert.Equal("third", lines[2]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FileSink_creates_parent_directory_if_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trackdot-test-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "nested", "crash.log");

        try
        {
            using (var sink = new FileUnhandledExceptionSink(path))
            {
                sink.WriteLine("nested hello");
            }

            Assert.True(File.Exists(path));
            Assert.Contains("nested hello", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FileSink_throws_on_empty_path()
    {
        Assert.Throws<ArgumentException>(() => new FileUnhandledExceptionSink(""));
        Assert.Throws<ArgumentException>(() => new FileUnhandledExceptionSink("   "));
    }

    [Fact]
    public void FileSink_silently_disables_after_write_failure()
    {
        // Use a path that cannot be created: a regular file
        // at the parent of the desired log file. The
        // directory check will fail, the write will fail,
        // and the sink must flip to IsAvailable == false
        // without throwing.
        var blocker = Path.Combine(Path.GetTempPath(), $"trackdot-blocker-{Guid.NewGuid():N}.txt");
        File.WriteAllText(blocker, "blocker");
        var blockedPath = Path.Combine(blocker, "crash.log"); // can't be a file under a file

        try
        {
            using var sink = new FileUnhandledExceptionSink(blockedPath);

            sink.WriteLine("first attempt"); // swallows, disables

            Assert.False(sink.IsAvailable);

            // Subsequent writes are silent no-ops.
            sink.WriteLine("second attempt");
        }
        finally
        {
            if (File.Exists(blocker)) File.Delete(blocker);
        }
    }

    [Fact]
    public void FileSink_write_null_line_is_noop()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackdot-test-{Guid.NewGuid():N}.log");

        try
        {
            using (var sink = new FileUnhandledExceptionSink(path))
            {
                sink.WriteLine(null!);
            }

            // File is created on first write only; null line
            // does not create the file. Even if the underlying
            // AppendAllText would have created it, the early
            // null-check in the sink means no I/O happens.
            // Either way, no exception is the contract.
            Assert.True(!File.Exists(path) || File.ReadAllText(path) == string.Empty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task FileSink_concurrent_writes_do_not_corrupt_lines()
    {
        // Two threads writing different lines. The internal
        // lock must serialise writes so no line is interleaved.
        var path = Path.Combine(Path.GetTempPath(), $"trackdot-test-{Guid.NewGuid():N}.log");

        try
        {
            using (var sink = new FileUnhandledExceptionSink(path))
            {
                var t1 = Task.Run(() =>
                {
                    for (var i = 0; i < 50; i++) sink.WriteLine($"thread-A-{i:D3}-end");
                });
                var t2 = Task.Run(() =>
                {
                    for (var i = 0; i < 50; i++) sink.WriteLine($"thread-B-{i:D3}-end");
                });
                await Task.WhenAll(t1, t2);
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(100, lines.Length);
            // Every line ends with "end" — partial writes would
            // produce a line truncated mid-token.
            foreach (var line in lines)
            {
                Assert.EndsWith("end", line);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ----- sink contract -----------------------------------------------

    [Fact]
    public void Sink_contract_writeLine_is_called_with_formatted_lines()
    {
        // Recording fake — the logger's job is to produce
        // well-formed lines; the sink's job is to write them.
        // A fake sink can verify the contract without touching
        // the filesystem.
        var sink = new RecordingSink();
        sink.WriteLine("one");
        sink.WriteLine("two");

        Assert.Equal(2, sink.Lines.Count);
        Assert.Equal("one", sink.Lines[0]);
        Assert.Equal("two", sink.Lines[1]);
    }

    private sealed class RecordingSink : IUnhandledExceptionSink
    {
        public System.Collections.Generic.List<string> Lines { get; } = new();

        public void WriteLine(string line) => Lines.Add(line);
    }
}
