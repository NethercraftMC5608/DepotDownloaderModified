// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.IO;
using System.Text.Json;
using Spectre.Console;

namespace DepotDownloader;

/// <summary>
/// Handles console‑based ANSI progress reporting and (optionally) writes
/// structured progress data to a file whenever the environment variable
/// DEPOTDOWNLOADER_PROGRESS_FILE is set.
/// </summary>
static class Ansi
{
    // ------------------------------------------------------------------
    // ANSI progress‑bar escape‑sequence definitions
    // ------------------------------------------------------------------
    // https://conemu.github.io/en/AnsiEscapeCodes.html#ConEmu_specific_OSC
    // https://learn.microsoft.com/en-us/windows/terminal/tutorials/progress-bar-sequences
    public enum ProgressState : byte
    {
        Hidden        = 0,
        Default       = 1,
        Error         = 2,
        Indeterminate = 3,
        Warning       = 4
    }

    private const char ESC = (char)0x1B;
    private const char BEL = (char)0x07;

    private static bool   useProgress;          // true → console supports OSC 9;4
    private static string? progressFilePath;    // path from env‑var, if any
    private static bool   announced;            // print the path only once

    // ------------------------------------------------------------------
    // One‑time initialisation (called from Program.Main)
    // ------------------------------------------------------------------
    public static void Init()
    {
        // skip OSC sequences if input or output is redirected
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
            return;

        // OSC 9;4 currently has no effect on Linux ttys
        if (OperatingSystem.IsLinux())
            return;

        var (supportsAnsi, legacyConsole) = AnsiDetector.Detect(stdError: false, upgrade: true);
        useProgress = supportsAnsi && !legacyConsole;

        progressFilePath = Environment.GetEnvironmentVariable("DEPOTDOWNLOADER_PROGRESS_FILE");
        if (!string.IsNullOrWhiteSpace(progressFilePath) && !announced)
        {
            Console.WriteLine($"[Ansi] Progress file = {progressFilePath}");
            announced = true;
        }
    }

    // ------------------------------------------------------------------
    // Convenience overload – calculate percentage automatically
    // ------------------------------------------------------------------
    public static void Progress(ulong downloaded, ulong total)
    {
        byte pct = total == 0
            ? (byte)0
            : (byte)MathF.Round(downloaded / (float)total * 100.0f);

        Progress(ProgressState.Default, pct, downloaded, total);
    }

    // ------------------------------------------------------------------
    // Core routine – emits OSC 9;4 *and* (optionally) writes JSON
    // ------------------------------------------------------------------
    public static void Progress(
        ProgressState state,
        byte          percent     = 0,
        ulong         downloaded  = 0,
        ulong         total       = 0)
    {
        // ---- console progress bar (Windows Terminal, WT, ConEmu, etc.) ----
        if (useProgress)
            Console.Write($"{ESC}]9;4;{(byte)state};{percent}{BEL}");

        // ---- structured file logging for external tools (Python, etc.) ----
        if (string.IsNullOrWhiteSpace(progressFilePath))
            return; // feature disabled

        try
        {
            var payload = new
            {
                downloaded,
                total,
                percentage = percent
            };

            var options = new JsonSerializerOptions { WriteIndented = false };

            // overwrite in *create* mode so the reader can open the file immediately
            using var fs = new FileStream(
                progressFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite);   // allow simultaneous reading

            JsonSerializer.Serialize(fs, payload, options);
        }
        catch
        {
            // ignore I/O errors (locked path, permission issues, etc.)
        }
    }
}
