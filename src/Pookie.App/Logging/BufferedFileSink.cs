// SPDX-License-Identifier: BSD-3-Clause
using System.Threading.Channels;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Pookie.Logging;

// File output must never wait on a UI/audio caller. A bounded queue drops new
// entries under disk pressure instead of retaining arbitrary log payloads.
internal sealed class BufferedFileSink(ILogger target) : ILogEventSink, IDisposable
{
    private readonly Channel<LogEvent> queue = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(1024) { SingleReader=true, FullMode=BoundedChannelFullMode.Wait });
    private Task? worker;
    private readonly object sync=new();
    public void Emit(LogEvent item)
    {
        lock(sync) worker ??= Task.Run(DrainAsync);
        queue.Writer.TryWrite(item);
    }
    private async Task DrainAsync()
    {
        try { await foreach(var item in queue.Reader.ReadAllAsync()) target.Write(item); }
        finally { (target as IDisposable)?.Dispose(); }
    }
    public void Dispose()
    {
        Task? finishing;
        lock(sync) { queue.Writer.TryComplete(); finishing=worker; }
        if(finishing!=null)finishing.GetAwaiter().GetResult();else (target as IDisposable)?.Dispose();
    }
}
