// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.IO;
using System.Text.Json;

namespace DepotDownloader
{
    /// <summary>
    /// Handles console-based ANSI progress reporting and (optionally) writes
    /// structured progress data to a file whenever the environment variable
    /// DEPOTDOWNLOADER_PROGRESS_FILE is set.
    /// </summary>
    static class Ansi
    {
        // ------------------------------------------------------------------
        // ANSI progress-bar escape-sequence definitions
        // ------------------------------------------------------------------
        // https://conemu.github.io/en/AnsiEscapeCodes.html#ConEmu_specific_OSC
        // https://learn.microsoft.com/windows/terminal/tutorials/progress-bar-sequences
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

        // true → emit OSC 9;4 sequences (Windows Terminal, ConEmu, etc.)
        private static bool   useProgress;
        // path for JSON progress (from env var)
        private static string? progressFilePath;
        // print the path only once
        private static bool   announced;

        /// <summary>
        /// One-time initialization (call from Program.Main before downloads start).
        /// </summary>
        public static void Init()
        {
            // --- Always enable JSON progress if env var is set, regardless of OS/TTY ---
            progressFilePath = Environment.GetEnvironmentVariable("DEPOTDOWNLOADER_PROGRESS_FILE");
            if (!string.IsNullOrWhiteSpace(progressFilePath) && !announced)
            {
                try
                {
                    Console.WriteLine($"[Ansi] Progress file = {progressFilePath}");
                }
                catch
                {
                    // ignore if output is fully redirected/unsupported
                }
                announced = true;
            }

            // --- Gate only the OSC 9;4 taskbar progress behind terminal/OS checks ---
            // If input or output is redirected, skip OSC (JSON still works).
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                return;

            // OSC 9;4 currently has no effect on Linux ttys (keep JSON enabled though).
            if (OperatingSystem.IsLinux())
                return;

            // If you have a terminal capability detector, wire it in here.
            // Otherwise just assume modern Windows terminals support OSC.
            try
            {
                // Example: if you have your own detector:
                // var (supportsAnsi, legacyConsole) = AnsiDetector.Detect(stdError: false, upgrade: true);
                // useProgress = supportsAnsi && !legacyConsole;

                // Fallback: enable on Windows by default.
                useProgress = OperatingSystem.IsWindows();
            }
            catch
            {
                useProgress = false;
            }
        }

        /// <summary>
        /// Convenience overload – calculate percentage automatically.
        /// </summary>
        public static void Progress(ulong downloaded, ulong total)
        {
            byte pct = total == 0
                ? (byte)0
                : (byte)MathF.Round(downloaded / (float)total * 100.0f);

            Progress(ProgressState.Default, pct, downloaded, total);
        }

        /// <summary>
        /// Core routine – emits OSC 9;4 (if supported) and writes JSON (if env var set).
        /// </summary>
        public static void Progress(
            ProgressState state,
            byte          percent    = 0,
            ulong         downloaded = 0,
            ulong         total      = 0)
        {
            // ---- console taskbar progress (Windows Terminal, ConEmu, etc.) ----
            if (useProgress)
            {
                try
                {
                    Console.Write($"{ESC}]9;4;{(byte)state};{percent}{BEL}");
                }
                catch
                {
                    // ignore console write failures
                }
            }

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

                // Overwrite in *create* mode so the reader can open the file immediately.
                // Share read/write so a polling reader can read concurrently.
                using var fs = new FileStream(
                    progressFilePath!,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);

                JsonSerializer.Serialize(fs, payload, options);
                // optional: flush to be extra safe
                fs.Flush(flushToDisk: false);
            }
            catch
            {
                // ignore I/O errors (locked path, permission issues, short-lived races, etc.)
            }
        }
    }
}
