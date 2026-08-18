/*
 * The only copy engine drvctl ships. Uses the Win32 CopyFile2 API directly
 * rather than System.IO.File.Copy so failures surface as the real Win32
 * error code (e.g. ERROR_FILENAME_EXCED_RANGE) instead of a generic IOException.
 */

using System.ComponentModel;
using System.Runtime.InteropServices;
using DrvCtl.Native;

namespace DrvCtl.Copy;

/// Copies files using the Windows CopyFile2 API.
internal sealed class CopyFile2Engine : ICopyEngine
{
    private const uint CopyFileFailIfExists = 0x00000001;

    public string Name =>
        "Windows CopyFile2";

    /// <inheritdoc/>
    /// <exception cref="Win32Exception">CopyFile2 returned a Win32 HRESULT failure.</exception>
    /// <exception cref="IOException">CopyFile2 returned a non-Win32 HRESULT failure.</exception>
    public void Copy(
        string source,
        string destination
    )
    {
        Kernel32Native.CopyFile2ExtendedParameters parameters =
            new()
            {
                Size =
                    (uint)Marshal.SizeOf<
                        Kernel32Native.CopyFile2ExtendedParameters
                    >(),
                CopyFlags =
                    CopyFileFailIfExists,
                Cancel =
                    0,
                ProgressRoutine =
                    0,
                CallbackContext =
                    0
            };

        int hresult =
            Kernel32Native.CopyFile2(
                source,
                destination,
                in parameters
            );

        if (hresult >= 0)
        {
            return;
        }

        int? win32 =
            HResultToWin32(
                hresult
            );

        if (win32.HasValue)
        {
            throw new Win32Exception(
                win32.Value,
                Marshal.GetPInvokeErrorMessage(
                    win32.Value
                )
            );
        }

        throw new IOException(
            $"CopyFile2 returned HRESULT 0x{unchecked((uint)hresult):X8}."
        );
    }

    /// Unwraps an HRESULT that was constructed from a Win32 error via
    /// HRESULT_FROM_WIN32 (facility 0x7, the FACILITY_WIN32 pattern
    /// 0x8007xxxx). Returns null for HRESULTs that don't follow that pattern,
    /// since not every native failure has a corresponding Win32 code.
    private static int? HResultToWin32(
        int hresult
    )
    {
        uint value =
            unchecked(
                (uint)hresult
            );

        if (
            (value & 0xFFFF0000u) ==
            0x80070000u
        )
        {
            return (int)(
                value &
                0x0000FFFFu
            );
        }

        return null;
    }
}
