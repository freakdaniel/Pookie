using Tmds.DBus.Protocol;

namespace Pookie.Media.Linux;

// Explicit wire types replace Tmds.DBus's Reflection.Emit proxies/adapters.
internal sealed class MprisMediaSession : IMediaSession, IPathMethodHandler
{
    internal const string ObjectPath = "/org/mpris/MediaPlayer2";
    internal const string PlayerInterface = "org.mpris.MediaPlayer2.Player", RootInterface = "org.mpris.MediaPlayer2";
    private readonly DBusConnection connection;
    private MediaSnapshot? snapshot;
    private bool disposed;
    public string Path => ObjectPath;
    public bool HandlesChildPaths => false;
    public event Action<MediaCommand>? Command;
    internal MprisMediaSession(DBusConnection connection)
    { this.connection = connection; connection.AddMethodHandler(this); }
    internal static string ServiceName => "org.mpris.MediaPlayer2.Pookie.instance" + Environment.ProcessId;
    public static async Task<MprisMediaSession> CreateAsync(string? address = null)
    {
        var connection = new DBusConnection(address ?? DBusAddress.Session ?? throw new PlatformNotSupportedException("No D-Bus session."));
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            var session = new MprisMediaSession(connection);
            await connection.RequestNameAsync(ServiceName).ConfigureAwait(false);
            return session;
        }
        catch { connection.Dispose(); throw; }
    }
    internal static ObjectPath TrackPath(string id) => new("/org/mpris/MediaPlayer2/track/" + Convert.ToHexStringLower(System.Text.Encoding.UTF8.GetBytes(id)));
    private static Dictionary<string, VariantValue> Metadata(MediaSnapshot? state)
    {
        if (state == null) return [];
        var result = new Dictionary<string, VariantValue>
        {
            ["mpris:trackid"] = TrackPath(state.TrackId), ["mpris:length"] = (long)(state.Duration * 1_000_000),
            ["xesam:title"] = state.Title, ["xesam:artist"] = VariantValue.Array(new[] { state.Artist })
        };
        if (state.ArtworkUrl != null) result["mpris:artUrl"] = state.ArtworkUrl;
        if (state.TrackUrl != null) result["xesam:url"] = state.TrackUrl;
        return result;
    }
    private static Dictionary<string, VariantValue> Properties(string iface, MediaSnapshot? state) => iface switch
    {
        RootInterface => new()
        {
            ["CanQuit"] = false, ["CanRaise"] = true, ["HasTrackList"] = false, ["Identity"] = "Pookie",
            ["SupportedUriSchemes"] = VariantValue.Array(System.Array.Empty<string>()),
            ["SupportedMimeTypes"] = VariantValue.Array(System.Array.Empty<string>())
        },
        PlayerInterface => new()
        {
            ["PlaybackStatus"] = state?.Playback == MediaPlayback.Playing ? "Playing" : state?.Playback is null or MediaPlayback.Stopped ? "Stopped" : "Paused",
            ["Rate"] = 1d, ["MinimumRate"] = 1d, ["MaximumRate"] = 1d,
            ["Volume"] = state?.Volume ?? 1, ["Shuffle"] = state?.Shuffle ?? false,
            ["CanGoNext"] = state?.CanNext ?? false, ["CanGoPrevious"] = state?.CanPrevious ?? false,
            ["CanSeek"] = state?.CanSeek ?? false, ["CanPlay"] = state != null, ["CanPause"] = state != null, ["CanControl"] = true,
            ["Position"] = (long)((state?.Position ?? 0) * 1_000_000), ["Metadata"] = new Dict<string, VariantValue>(Metadata(state))
        },
        _ => throw new DBusErrorReplyException("org.freedesktop.DBus.Error.UnknownInterface", iface)
    };
    public void Update(MediaSnapshot state)
    {
        if (disposed) return;
        var previous = Interlocked.Exchange(ref snapshot, state);
        var before = Properties(PlayerInterface, previous); var after = Properties(PlayerInterface, state);
        var changes = after.Where(pair => pair.Key != "Position" && (pair.Key == "Metadata"
            ? previous == null || previous.TrackId != state.TrackId || previous.Title != state.Title || previous.Artist != state.Artist || previous.ArtworkUrl != state.ArtworkUrl || previous.TrackUrl != state.TrackUrl || previous.Duration != state.Duration
            : !pair.Value.Equals(before[pair.Key]))).ToDictionary();
        Changed(changes);
        if (previous?.TrackId == state.TrackId && Math.Abs(state.Position - previous.Position) > 1.5)
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(path: Path, @interface: PlayerInterface, member: "Seeked", signature: "x");
            writer.WriteInt64((long)(state.Position * 1_000_000)); connection.TrySendMessage(writer.CreateMessage());
        }
    }
    private void Changed(Dictionary<string, VariantValue> values)
    {
        if (values.Count == 0) return;
        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(path: Path, @interface: "org.freedesktop.DBus.Properties", member: "PropertiesChanged", signature: "sa{sv}as");
        writer.WriteString(PlayerInterface); writer.WriteDictionary(values); writer.WriteArray(System.Array.Empty<string>());
        connection.TrySendMessage(writer.CreateMessage());
    }
    public void Clear()
    {
        Interlocked.Exchange(ref snapshot, null);
        Changed(Properties(PlayerInterface, null).Where(p => p.Key is "Metadata" or "PlaybackStatus" or "CanPlay" or "CanPause" or "CanSeek" or "CanGoNext" or "CanGoPrevious").ToDictionary());
    }
    public void Dispose()
    { if (disposed) return; Clear(); disposed = true; connection.RemoveMethodHandler(Path); connection.Dispose(); }
    public ValueTask HandleMethodAsync(MethodContext context)
    {
        try { Handle(context); }
        catch (DBusErrorReplyException error) { context.ReplyError(error.ErrorName, error.ErrorMessage); }
        catch (Exception) { context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", "Invalid MPRIS arguments."); }
        return ValueTask.CompletedTask;
    }
    private void Handle(MethodContext context)
    {
        var request = context.Request; var member = request.MemberAsString;
        if (context.IsDBusIntrospectRequest)
        { context.ReplyIntrospectXml([System.Text.Encoding.UTF8.GetBytes(Introspection)]); return; }
        var reader = request.GetBodyReader();
        if (context.IsPropertiesInterfaceRequest)
        {
            var iface = reader.ReadString();
            var values = Properties(iface, Volatile.Read(ref snapshot));
            if (member == "GetAll")
            { using var reply = context.CreateReplyWriter("a{sv}"); reply.WriteDictionary(values); context.Reply(reply.CreateMessage()); return; }
            var name = reader.ReadString();
            if (!values.TryGetValue(name, out var value)) throw new DBusErrorReplyException("org.freedesktop.DBus.Error.UnknownProperty", name);
            if (member == "Get")
            { using var reply = context.CreateReplyWriter("v"); reply.WriteVariant(value); context.Reply(reply.CreateMessage()); return; }
            if (member == "Set" && iface == PlayerInterface)
            {
                var next = reader.ReadVariantValue();
                if (name == "Volume" && next.Type == VariantValueType.Double && double.IsFinite(next.GetDouble())) Send(MediaAction.Volume, Math.Max(0, next.GetDouble()));
                else if (name == "Shuffle" && next.Type == VariantValueType.Bool) Send(MediaAction.Shuffle, next.GetBool() ? 1 : 0);
                else if (name != "Rate" || next.Type != VariantValueType.Double || next.GetDouble() != 1) throw new DBusErrorReplyException("org.freedesktop.DBus.Error.PropertyReadOnly", name);
            }
            else { context.ReplyUnknownMethodError(); return; }
        }
        else if (request.InterfaceAsString == RootInterface)
        {
            if (member == "Raise") Send(MediaAction.Raise);
            else if (member != "Quit") { context.ReplyUnknownMethodError(); return; }
        }
        else if (request.InterfaceAsString == PlayerInterface)
        {
            switch (member)
            {
                case "Next": Send(MediaAction.Next); break;
                case "Previous": Send(MediaAction.Previous); break;
                case "Pause": Send(MediaAction.Pause); break;
                case "PlayPause": Send(MediaAction.Toggle); break;
                case "Stop": Send(MediaAction.Stop); break;
                case "Play": Send(MediaAction.Play); break;
                case "Seek": Send(MediaAction.SeekRelative, reader.ReadInt64() / 1_000_000d); break;
                case "SetPosition":
                    var id = reader.ReadObjectPath(); var position = reader.ReadInt64(); var state = Volatile.Read(ref snapshot);
                    if (state != null && TrackPath(state.TrackId) == id && position >= 0) Send(MediaAction.Seek, position / 1_000_000d, state.TrackId);
                    break;
                case "OpenUri": throw new DBusErrorReplyException("org.freedesktop.DBus.Error.NotSupported", "OpenUri is unavailable.");
                default: context.ReplyUnknownMethodError(); return;
            }
        }
        else { context.ReplyUnknownMethodError(); return; }
        using var empty = context.CreateReplyWriter(null); context.Reply(empty.CreateMessage());
    }
    private void Send(MediaAction action, double value = 0, string? id = null)
    { if (!disposed) Command?.Invoke(new(action, value, id)); }

    private const string Introspection = """
      <interface name="org.mpris.MediaPlayer2">
        <method name="Raise"/><method name="Quit"/>
        <property name="CanQuit" type="b" access="read"/><property name="CanRaise" type="b" access="read"/>
        <property name="HasTrackList" type="b" access="read"/><property name="Identity" type="s" access="read"/>
        <property name="SupportedUriSchemes" type="as" access="read"/><property name="SupportedMimeTypes" type="as" access="read"/>
      </interface>
      <interface name="org.mpris.MediaPlayer2.Player">
        <method name="Next"/><method name="Previous"/><method name="Pause"/><method name="PlayPause"/><method name="Stop"/><method name="Play"/>
        <method name="Seek"><arg name="Offset" type="x" direction="in"/></method>
        <method name="SetPosition"><arg name="TrackId" type="o" direction="in"/><arg name="Position" type="x" direction="in"/></method>
        <method name="OpenUri"><arg name="Uri" type="s" direction="in"/></method><signal name="Seeked"><arg name="Position" type="x"/></signal>
        <property name="PlaybackStatus" type="s" access="read"/><property name="Rate" type="d" access="readwrite"/>
        <property name="Volume" type="d" access="readwrite"/><property name="Shuffle" type="b" access="readwrite"/>
        <property name="MinimumRate" type="d" access="read"/><property name="MaximumRate" type="d" access="read"/>
        <property name="Metadata" type="a{sv}" access="read"/><property name="Position" type="x" access="read"/>
        <property name="CanGoNext" type="b" access="read"/><property name="CanGoPrevious" type="b" access="read"/>
        <property name="CanPlay" type="b" access="read"/><property name="CanPause" type="b" access="read"/>
        <property name="CanSeek" type="b" access="read"/><property name="CanControl" type="b" access="read"/>
      </interface>
      """;
}
