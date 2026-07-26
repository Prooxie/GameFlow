namespace GameFlow.Infrastructure.Runtime.Input.Mac;

/// <summary>
/// Translates macOS "Carbon" virtual keycodes (the values CGEventTap
/// reports via kCGKeyboardEventKeycode) to Windows virtual-key codes —
/// same reasoning as EvdevKeyCodeMap on the Linux side:
/// <see cref="IKeyboardStateSource"/>'s contract is Windows VK codes, so
/// translating at the source keeps everything downstream OS-agnostic.
///
/// <para>
/// <b>Deliberately smaller than the Linux table.</b> This is recalled
/// from memory with no header or compiler available anywhere in this
/// environment to check it against (unlike evdev's codes, which were
/// cross-checked against real kernel headers). Rather than transcribe
/// ~90 entries at roughly the same risk as the ~10 riskiest ones, this
/// covers only letters, digits, arrows, core editing keys, and
/// modifiers — the subset with the highest recall confidence — and
/// stops there. Function keys, the numpad, and punctuation are left
/// out rather than guessed.
/// </para>
/// </summary>
internal static class MacKeyCodeMap
{
    public static readonly IReadOnlyDictionary<int, int> MacToVirtualKey = Build();

    private static Dictionary<int, int> Build()
    {
        var map = new Dictionary<int, int>();

        // Letters (kVK_ANSI_*) — Mac's own physical-key scancode order, not alphabetical.
        Add(map, 0x00, 0x41);  // A
        Add(map, 0x0B, 0x42);  // B
        Add(map, 0x08, 0x43);  // C
        Add(map, 0x02, 0x44);  // D
        Add(map, 0x0E, 0x45);  // E
        Add(map, 0x03, 0x46);  // F
        Add(map, 0x05, 0x47);  // G
        Add(map, 0x04, 0x48);  // H
        Add(map, 0x22, 0x49);  // I
        Add(map, 0x26, 0x4A);  // J
        Add(map, 0x28, 0x4B);  // K
        Add(map, 0x25, 0x4C);  // L
        Add(map, 0x2E, 0x4D);  // M
        Add(map, 0x2D, 0x4E);  // N
        Add(map, 0x1F, 0x4F);  // O
        Add(map, 0x23, 0x50);  // P
        Add(map, 0x0C, 0x51);  // Q
        Add(map, 0x0F, 0x52);  // R
        Add(map, 0x01, 0x53);  // S
        Add(map, 0x11, 0x54);  // T
        Add(map, 0x20, 0x55);  // U
        Add(map, 0x09, 0x56);  // V
        Add(map, 0x0D, 0x57);  // W
        Add(map, 0x07, 0x58);  // X
        Add(map, 0x10, 0x59);  // Y
        Add(map, 0x06, 0x5A);  // Z

        // Digits.
        Add(map, 0x12, 0x31); Add(map, 0x13, 0x32); Add(map, 0x14, 0x33); Add(map, 0x15, 0x34);
        Add(map, 0x17, 0x35); Add(map, 0x16, 0x36); Add(map, 0x1A, 0x37); Add(map, 0x1C, 0x38);
        Add(map, 0x19, 0x39); Add(map, 0x1D, 0x30);

        // Editing / whitespace.
        Add(map, 0x24, 0x0D); // Return -> VK_RETURN
        Add(map, 0x30, 0x09); // Tab -> VK_TAB
        Add(map, 0x31, 0x20); // Space -> VK_SPACE
        Add(map, 0x33, 0x08); // Delete (Mac's "Backspace", left of Return) -> VK_BACK
        Add(map, 0x75, 0x2E); // Forward Delete (PC-style Delete) -> VK_DELETE
        Add(map, 0x35, 0x1B); // Escape -> VK_ESCAPE
        Add(map, 0x73, 0x24); // Home -> VK_HOME
        Add(map, 0x77, 0x23); // End -> VK_END
        Add(map, 0x74, 0x21); // Page Up -> VK_PRIOR
        Add(map, 0x79, 0x22); // Page Down -> VK_NEXT

        // Arrows.
        Add(map, 0x7B, 0x25); // Left
        Add(map, 0x7C, 0x27); // Right
        Add(map, 0x7D, 0x28); // Down
        Add(map, 0x7E, 0x26); // Up

        // Modifiers — left/right distinct where the Mac keycode space distinguishes them.
        Add(map, 0x38, 0xA0); // Shift -> VK_LSHIFT
        Add(map, 0x3C, 0xA1); // Right Shift -> VK_RSHIFT
        Add(map, 0x3B, 0xA2); // Control -> VK_LCONTROL
        Add(map, 0x3E, 0xA3); // Right Control -> VK_RCONTROL
        Add(map, 0x3A, 0xA4); // Option -> VK_LMENU
        Add(map, 0x3D, 0xA5); // Right Option -> VK_RMENU
        Add(map, 0x37, 0x5B); // Command -> VK_LWIN (closest semantic equivalent)
        Add(map, 0x39, 0x14); // Caps Lock -> VK_CAPITAL

        return map;
    }

    private static void Add(Dictionary<int, int> map, int macKeycode, int virtualKey) => map[macKeycode] = virtualKey;
}
