namespace Adawat.Linux;

/// <summary>The shared catalog and the windows name icons by their Segoe Fluent Icons code points, as the Windows app draws
/// them. Linux draws Material Icons Round (Apache 2.0), so each code point is translated to its closest Material icon.</summary>
public static class Icons
{
    private static readonly Dictionary<int, int> Map = new()
    {
        [0xE70D] = 0xE5CF, // ChevronDown → expand_more
        [0xE70E] = 0xE5CE, // ChevronUp → expand_less
        [0xE70F] = 0xE429, // Edit → tune
        [0xE710] = 0xE145, // Add → add
        [0xE711] = 0xE5CD, // Cancel → close
        [0xE713] = 0xE8B8, // Settings → settings
        [0xE71A] = 0xE047, // Stop → stop
        [0xE71B] = 0xE157, // Link → link
        [0xE71E] = 0xE8FF, // Zoom → zoom_in
        [0xE722] = 0xE412, // Camera → photo_camera
        [0xE727] = 0xE3A5, // InPrivate (blurred background) → blur_on
        [0xE72A] = 0xE5C8, // Forward → arrow_forward
        [0xE72B] = 0xE5C4, // Back → arrow_back
        [0xE72C] = 0xF053, // Refresh (reset) → restart_alt
        [0xE72D] = 0xE6B8, // Share → ios_share
        [0xE738] = 0xE15B, // Remove → remove
        [0xE739] = 0xE3C6, // Checkbox (rectangle tool) → crop_square
        [0xE73E] = 0xE86C, // CheckMark → check_circle
        [0xE73F] = 0xE94D, // BackToWindow (compress) → compress
        [0xE74D] = 0xE872, // Delete → delete
        [0xE74F] = 0xE04F, // Mute → volume_off
        [0xE756] = 0xEB8E, // CommandPrompt → terminal
        [0xE767] = 0xE050, // Volume → volume_up
        [0xE768] = 0xE037, // Play → play_arrow
        [0xE769] = 0xE034, // Pause → pause
        [0xE76B] = 0xE5CB, // ChevronLeft → chevron_left
        [0xE76C] = 0xE5CC, // ChevronRight → chevron_right
        [0xE771] = 0xE75F, // Personalize (background) → wallpaper
        [0xE77B] = 0xE87C, // Contact (faces) → face
        [0xE783] = 0xE000, // Error → error
        [0xE78C] = 0xE41C, // SaveLocal (trim) → straighten
        [0xE7A7] = 0xE166, // Undo → undo
        [0xE7A8] = 0xE3BE, // Crop → crop
        [0xE7AD] = 0xE41A, // Rotate → rotate_right
        [0xE7BA] = 0xE002, // Warning (bleep) → warning
        [0xE7E6] = 0xE25F, // Highlight → highlight
        [0xE7E8] = 0xE8AC, // PowerButton → power_settings_new
        [0xE7F3] = 0xE049, // SettingsDisplaySound (channels) → surround_sound
        [0xE840] = 0xF10D, // Pinned → push_pin
        [0xE842] = 0xF10D, // PinnedFill → push_pin
        [0xE892] = 0xE045, // Previous → skip_previous
        [0xE893] = 0xE044, // Next → skip_next
        [0xE895] = 0xE627, // Sync (working) → sync
        [0xE897] = 0xE8FD, // Help → help_outline
        [0xE8A5] = 0xE873, // Document → description
        [0xE8B2] = 0xE02C, // Movies → movie
        [0xE8B7] = 0xE2C7, // Folder → folder
        [0xE8BB] = 0xE5CD, // ChromeClose → close
        [0xE8C1] = 0xE262, // Characters (copy text) → text_fields
        [0xE8C6] = 0xF08B, // Cut → content_cut
        [0xE8C8] = 0xF08A, // Copy → content_copy
        [0xE8D2] = 0xE262, // Font → text_fields
        [0xE8D6] = 0xE3A1, // Audio → audiotrack
        [0xE8DE] = 0xE675, // MoveToFolder → drive_file_move
        [0xE91B] = 0xE3F4, // Photo → image
        [0xE930] = 0xE86C, // Completed → check_circle
        [0xE946] = 0xE88E, // Info → info
        [0xE992] = 0xE04E, // Volume0 (silence) → volume_mute
        [0xE9D9] = 0xE1B8, // Diagnostic (waveform) → graphic_eq
        [0xEA3A] = 0xE836, // CircleRing → radio_button_unchecked
        [0xEA90] = 0xE415, // PDF → picture_as_pdf
        [0xEB9F] = 0xE43E, // Photo2 (image tool) → add_photo_alternate
        [0xEC4A] = 0xE9E4, // SpeedHigh → speed
        [0xED14] = 0xE00A, // QRCode → qr_code_2
        [0xED1A] = 0xE8F5, // Hide (redact) → visibility_off
        [0xED63] = 0xE746, // Pencil → draw
        [0xEE56] = 0xE3AE, // PenPalette (annotate) → brush
        [0xF012] = 0xEB2C, // ZipFolder → folder_zip
        [0xF0E2] = 0xE9B0, // GridView → grid_view
        [0xF146] = 0xE400, // Dial1 (numbered callout) → looks_one
        [0xF5ED] = 0xE0B3, // Set (merge) → call_merge
    };

    /// <summary>The Material Icons text for a Segoe glyph; other text passes through.</summary>
    public static string Glyph(string segoe)
    {
        if (string.IsNullOrEmpty(segoe)) return segoe;
        var chars = segoe.Select(c => Map.TryGetValue(c, out var m) ? (char)m : c).ToArray();
        return new string(chars);
    }
}
