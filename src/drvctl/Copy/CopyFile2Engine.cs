using System.ComponentModel;
using System.Runtime.InteropServices;
using DrvCtl.Native;

namespace DrvCtl.Copy;

internal sealed class CopyFile2Engine : ICopyEngine
{
    private const uint CopyFileFailIfExists = 0x00000001;

    public string Name =>
        "Windows CopyFile2";

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
