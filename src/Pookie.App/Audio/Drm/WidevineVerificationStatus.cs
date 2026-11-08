namespace Pookie.Audio;

internal static class WidevineVerificationStatus
{
    // Same public response metadata used by https://github.com/castlabs/wv-vmp-lab.
    // SignedMessage.type == LICENSE (2), SignedMessage.msg (2), License.platform_verification_status (10).
    internal static string? Read(ReadOnlySpan<byte> response)
    {
        if (!TryReadField(response, 1, 0, out var type, out _) || type != 2 ||
            !TryReadField(response, 2, 2, out _, out var license) ||
            !TryReadField(license, 10, 0, out var status, out _)) return null;
        return status switch
        {
            0 => "PLATFORM_UNVERIFIED",
            1 => "PLATFORM_TAMPERED",
            2 => "PLATFORM_SOFTWARE_VERIFIED",
            3 => "PLATFORM_HARDWARE_VERIFIED",
            4 => "PLATFORM_NO_VERIFICATION",
            5 => "PLATFORM_SECURE_STORAGE_SOFTWARE_VERIFIED",
            _ => null
        };
    }

    private static bool TryReadField(ReadOnlySpan<byte> message, int number, int wireType,
        out ulong value, out ReadOnlySpan<byte> data)
    {
        value = 0;
        data = default;
        while (!message.IsEmpty)
        {
            if (!TryReadVarint(ref message, out var tag) || tag < 8 || tag > uint.MaxValue) return false;
            var wire = (int)(tag & 7);
            var matches = tag >> 3 == (ulong)number && wire == wireType;
            int length;
            switch (wire)
            {
                case 0:
                    if (!TryReadVarint(ref message, out var scalar)) return false;
                    if (matches) { value = scalar; return true; }
                    continue;
                case 1: length = 8; break;
                case 2:
                    if (!TryReadVarint(ref message, out var size) || size > (ulong)message.Length) return false;
                    length = (int)size;
                    break;
                case 5: length = 4; break;
                default: return false;
            }
            if (length > message.Length) return false;
            if (matches) { data = message[..length]; return true; }
            message = message[length..];
        }
        return false;
    }

    private static bool TryReadVarint(ref ReadOnlySpan<byte> message, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift <= 63 && !message.IsEmpty; shift += 7)
        {
            var next = message[0];
            message = message[1..];
            if (shift == 63 && next > 1) return false;
            value |= (ulong)(next & 0x7f) << shift;
            if (next < 0x80) return true;
        }
        return false;
    }
}
