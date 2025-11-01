using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CopyAfterProcess
{
    public static class LongPathHelper
    {
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            path = path.Trim();  // Remove accidental whitespace

            // Early exit if already prefixed correctly
            if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                return path;

            // Check for local drive pattern: [A-Z]:[\\/]...
            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            {
                return @"\\?\" + path;
            }

            // UNC handling: Starts with \\ but NOT a malformed drive (e.g., not \\C:\)
            if (path.StartsWith(@"\\"))
            {
                // Guard against malformed \\Drive: paths
                if (path.Length >= 5 && char.IsLetter(path[2]) && path[3] == ':' && (path[4] == '\\' || path[4] == '/'))
                {
                    // Treat as local (e.g., \\C:\ -> \\?\C:\)
                    return @"\\?\" + path.Substring(2);  // Skip the leading \\
                }

                // True UNC: Prefix with \\?\UNC\
                if (!path.StartsWith(@"\\?\\UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    return @"\\?\UNC\" + path.Substring(2);
                }
                return path;  // Already good
            }

            // Relative or other: No change
            return path;
        }

        public static bool IsLongPath(string path) => !string.IsNullOrEmpty(path?.Trim()) && path.Length >= 260;
    }

    public class LongPathProgressFileMover
    {
        public delegate void ProgressDelegate(double percentage, ref bool cancel);
        public delegate void CompleteDelegate(bool success, string message);

        public event ProgressDelegate? OnProgressChanged;
        public event CompleteDelegate? OnComplete;

        public void MoveFile(string sourcePath, string destPath)
        {
            // Normalize for long paths
            string normalizedSource = LongPathHelper.NormalizePath(sourcePath);
            string normalizedDest = LongPathHelper.NormalizePath(destPath);

            if (!File.Exists(normalizedSource))
            {
                OnComplete?.Invoke(false, $"Source file '{sourcePath}' does not exist.");
                return;
            }

            bool isSameDrive = string.Equals(Path.GetPathRoot(normalizedSource), Path.GetPathRoot(normalizedDest), StringComparison.OrdinalIgnoreCase);
            bool cancelFlag = false;

            if (isSameDrive)
            {
                // Same drive: Atomic move (File.Move handles long paths with prefix).
                OnProgressChanged?.Invoke(100.0, ref cancelFlag);
                try
                {
                    File.Move(normalizedSource, normalizedDest);
                    OnComplete?.Invoke(true, "File moved successfully (same drive).");
                }
                catch (Exception ex)
                {
                    OnComplete?.Invoke(false, $"Error: {ex.Message}");
                }
            }
            else
            {
                // Different drives: Copy with progress, then delete source.
                CopyWithProgress(normalizedSource, normalizedDest, ref cancelFlag);
                if (!cancelFlag && File.Exists(normalizedDest))
                {
                    try
                    {
                        File.Delete(normalizedSource);
                        OnComplete?.Invoke(true, "File moved successfully (cross-drive).");
                    }
                    catch (Exception ex)
                    {
                        OnComplete?.Invoke(false, $"Copy succeeded but delete failed: {ex.Message}");
                    }
                }
                else
                {
                    OnComplete?.Invoke(false, cancelFlag ? "Move cancelled by user." : "Copy failed.");
                }
            }
        }

        private void CopyWithProgress(string sourcePath, string destPath, ref bool cancelFlag)
        {
            const int bufferSize = 1024 * 1024; // 1MB chunks
            byte[] buffer = new byte[bufferSize];

            try
            {
                using FileStream sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                using FileStream destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
                long totalBytes = sourceStream.Length;
                long totalRead = 0;

                int bytesRead;
                while ((bytesRead = sourceStream.Read(buffer, 0, bufferSize)) > 0)
                {
                    destStream.Write(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    double percentage = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 100.0;

                    // Display progress on same line
                    string status = $"Copying {Path.GetFileName(sourcePath)}: {percentage:F0}%";
                    Console.Write($"\r{status,-60}"); // -60 pads to fixed width, overwrite line
                }

                // Final newline after completion
                Console.WriteLine("\r" + new string(' ', 60) + "\r"); // Clear line
                Console.WriteLine($"  Copied: {Path.GetFileName(destPath)} (overwritten if existed).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n  Error copying file: {ex.Message}");
            }
        }
    }
}
