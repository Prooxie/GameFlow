using System.Runtime.InteropServices;

namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>
/// Moves the OS mouse cursor by a relative delta — the WRITE
/// counterpart to <see cref="IMouseStateSource"/>'s read direction (see
/// MouseGamepadSynthesizer.cs). There is exactly one system cursor, so
/// this is a process-wide singleton: every slot with a touchpad-mouse
/// mapping enabled moves the SAME cursor and will visibly fight each
/// other, the same way two physical mice plugged into one PC do — that
/// is expected, not a bug to design around here.
/// </summary>
public interface IMouseOutputWriter
{
    /// <summary>Moves the cursor by (dx, dy) screen pixels this tick. A no-op when both round to zero.</summary>
    void MoveRelative(float dx, float dy);
}

/// <summary>No-op default for non-Windows platforms — mirrors <see cref="NullMouseStateSource"/>'s role on the read side.</summary>
public sealed class NullMouseOutputWriter : IMouseOutputWriter
{
    public void MoveRelative(float dx, float dy)
    {
        // Intentional no-op: SendInput is Windows-only.
    }
}

/// <summary>
/// Real implementation via user32.dll's SendInput with
/// MOUSEEVENTF_MOVE — the same relative-motion mechanism every mouse
/// driver on the system uses, so it composes correctly with the user's
/// existing pointer speed/acceleration settings rather than fighting
/// them. Struct layout and P/Invoke style matches
/// WindowsRawInputReader.cs's existing user32.dll bindings in this same
/// namespace (DllImport, not LibraryImport — SDL3's bindings elsewhere
/// use LibraryImport, but that's a different native library with its
/// own established convention in this codebase; this matches its
/// nearest actual precedent instead).
///
/// <para>
/// Not unit-testable in this environment — SendInput and the Windows
/// message/cursor subsystem don't exist outside a real Windows desktop
/// session. The struct layout and calling convention are written
/// against the well-established, decades-stable Win32 contract with
/// high confidence, but this specific file needs a real-hardware smoke
/// test the other rounds' pure-Core changes didn't.
/// </para>
/// </summary>
public sealed class Win32MouseOutputWriter : IMouseOutputWriter
{
    private const uint InputTypeMouse = 0;
    private const uint MouseEventFlagMove = 0x0001;

    public void MoveRelative(float dx, float dy)
    {
        var roundedDx = (int)MathF.Round(dx);
        var roundedDy = (int)MathF.Round(dy);
        if (roundedDx == 0 && roundedDy == 0)
        {
            return;
        }

        var input = new MouseInput
        {
            Type = InputTypeMouse,
            Mouse = new MouseInputPayload
            {
                Dx = roundedDx,
                Dy = roundedDy,
                MouseData = 0,
                Flags = MouseEventFlagMove,
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        };

        // Return value / GetLastError deliberately not checked: a
        // dropped mouse-move frame is a forgettable event at 60-1000 Hz
        // (unlike a dropped controller-output write elsewhere in this
        // codebase, which gets a full status banner) — not worth
        // failing or even logging over one missed cursor nudge.
        _ = SendInput(1, ref input, Marshal.SizeOf<MouseInput>());
    }

    // ─── P/Invoke ───
    // INPUT is natively a tagged union (MOUSEINPUT/KEYBDINPUT/HARDWAREINPUT);
    // only the mouse variant is ever populated here, so this embeds
    // MOUSEINPUT directly via LayoutKind.Sequential rather than declaring
    // the full three-way union — correct as long as nothing ever reads
    // this same struct as a keyboard/hardware input, which nothing does.

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputPayload
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public uint Type;
        public MouseInputPayload Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref MouseInput pInputs, int cbSize);
}
