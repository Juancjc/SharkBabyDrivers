using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static SharkBabyDrivers.Core.Hid.NativeMethods;

namespace SharkBabyDrivers.Core.Hid;

public sealed class HidException(string message, int win32Error = 0) : IOException(
    win32Error == 0 ? message : $"{message} ({new Win32Exception(win32Error).Message}, 0x{win32Error:X})")
{
    public int Win32Error { get; } = win32Error;
}

/// <summary>
/// An open HID collection. Feature reports are synchronous (control transfers); input reports
/// are read on a background loop and raised through <see cref="InputReport"/>.
/// All feature I/O is serialized: these firmwares do not tolerate interleaved transfers.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HidConnection : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly object _ioLock = new();
    private FileStream? _inputStream;
    private CancellationTokenSource? _inputCts;

    public HidDeviceInfo Info { get; }
    public event Action<byte[]>? InputReport;
    public event Action<Exception>? InputError;

    private HidConnection(HidDeviceInfo info, SafeFileHandle handle)
    {
        Info = info;
        _handle = handle;
    }

    public static HidConnection Open(HidDeviceInfo info, bool overlapped = false)
    {
        var handle = CreateFileW(info.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, overlapped ? FILE_FLAG_OVERLAPPED : 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new HidException($"Não foi possível abrir {info}", Marshal.GetLastWin32Error());
        return new HidConnection(info, handle);
    }

    /// <summary>
    /// Sends a feature report. <paramref name="data"/>[0] is the report id.
    /// Windows wants the buffer to match the collection's feature length on some stacks and the exact
    /// report length on others, so the exact length is tried first and the padded length second.
    /// </summary>
    public void SetFeature(ReadOnlySpan<byte> data)
    {
        lock (_ioLock)
        {
            var exact = data.ToArray();
            if (HidD_SetFeature(_handle, exact, exact.Length)) return;
            var firstError = Marshal.GetLastWin32Error();
            var padLength = Math.Max(Info.FeatureReportLength, data.Length);
            if (padLength != exact.Length)
            {
                var padded = new byte[padLength];
                data.CopyTo(padded);
                if (HidD_SetFeature(_handle, padded, padded.Length)) return;
            }
            throw new HidException($"Falha ao enviar feature report 0x{data[0]:X2}", firstError);
        }
    }

    /// <summary>Reads feature report <paramref name="reportId"/>; returns <paramref name="length"/> bytes including the id.</summary>
    public byte[] GetFeature(byte reportId, int length)
    {
        lock (_ioLock)
        {
            var exact = new byte[length];
            exact[0] = reportId;
            if (HidD_GetFeature(_handle, exact, exact.Length)) return exact;
            var firstError = Marshal.GetLastWin32Error();
            var padLength = Math.Max(Info.FeatureReportLength, length);
            if (padLength != length)
            {
                var padded = new byte[padLength];
                padded[0] = reportId;
                if (HidD_GetFeature(_handle, padded, padded.Length)) return padded[..length];
            }
            throw new HidException($"Falha ao ler feature report 0x{reportId:X2}", firstError);
        }
    }

    /// <summary>Starts a background loop that raises <see cref="InputReport"/> for every input report.</summary>
    public void StartInputLoop()
    {
        if (_inputStream is not null || Info.InputReportLength <= 0) return;
        var handle = CreateFileW(Info.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
            handle = CreateFileW(Info.Path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid) return;
        _inputStream = new FileStream(handle, FileAccess.Read, 0, isAsync: true);
        _inputCts = new CancellationTokenSource();
        var token = _inputCts.Token;
        var stream = _inputStream;
        _ = Task.Run(async () =>
        {
            var buffer = new byte[Info.InputReportLength];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var n = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (n <= 0) break;
                    InputReport?.Invoke(buffer[..n]);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested) InputError?.Invoke(ex);
                    break;
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _inputCts?.Cancel();
        try { _inputStream?.Dispose(); } catch { /* device already gone */ }
        _handle.Dispose();
    }
}
