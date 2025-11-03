using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CopyAfterProcess
{
    /// <summary>
    /// Provides functionality to move or copy files, including support for long paths and progress reporting.  
    /// </summary>
    /// <remarks>This class is designed to handle file operations that may involve long paths, cross-drive
    /// transfers,  and progress reporting. It provides events for monitoring progress and completion, allowing for 
    /// cancellation and feedback during file operations.</remarks>
    public class LongPathProgressFileMover
    {
        /// <summary>
        /// Copy/Move File Progress Delegate
        /// </summary>
        /// <param name="percentage">Overall copy/move 
        /// progress as a percentage</param>
        /// <param name="cancel">Ref bool to cancel processing</param>
        public delegate void ProgressDelegate(double percentage, string speed,
                            string name, double transferredGB,
                            double totalGB, ref bool cancel);

        /// <summary>
        /// Copy/Move Completed Delegate
        /// </summary>
        /// <param name="success">True if the process completed 
        /// successfully</param>
        /// <param name="message">Success message</param>
        public delegate void CompleteDelegate(bool success, string message);

        /// <summary>
        /// OnProgressChanged Event.  Fired when the copy/move 
        /// progress changes.
        /// </summary>
        public event ProgressDelegate? OnProgressChanged;

        /// <summary>
        /// Completed Event.  Fired when the copy/move is completed
        /// </summary>
        public event CompleteDelegate? OnComplete;

        /// <summary>
        /// Moves a file with progress reports
        /// </summary>
        /// <param name="sourcePath">The path to the source file</param>
        /// <param name="destPath">The destination path to save 
        /// the source file to</param>
        public void MoveFile(string sourcePath, string destPath)
        {
            // Normalize for long paths
            string normalizedSource = PathConverter.NormalizePath(sourcePath);
            string normalizedDest = PathConverter.NormalizePath(destPath);

            // Make sure that the source file exists
            if (!File.Exists(normalizedSource))
            {
                OnComplete?.Invoke(false, $"Source file '{sourcePath}' does not exist.");
                return;
            }

            // Do some checks
            bool isSameDrive = string.Equals(Path.GetPathRoot(normalizedSource), Path.GetPathRoot(normalizedDest), StringComparison.OrdinalIgnoreCase);
            bool cancelFlag = false;

            // Check to see if we are doing a copy/move on the same drive
            if (isSameDrive)
            {
                // Same drive: Atomic move (File.Move handles long paths with prefix).
                OnProgressChanged?.Invoke(100.0, "0 MB/s", Path.GetDirectoryName(sourcePath)
                                    ?? String.Empty, 0.00, 0.00, ref cancelFlag);
                try
                {
                    // Move it
                    File.Move(normalizedSource, normalizedDest);
                    OnComplete?.Invoke(true, "File moved successfully (same drive).");
                }
                catch (Exception ex)
                {
                    // It gave me error
                    OnComplete?.Invoke(false, $"Error: {ex.Message}");
                }
            }
            else
            {
                // Different drives: Copy with progress, then delete source.
                CopyWithProgress(normalizedSource, normalizedDest, ref cancelFlag);

                // Make sure that we didn't cancel and also that the destihation file exists now
                if (!cancelFlag && File.Exists(normalizedDest))
                {
                    try
                    {
                        // Delete it
                        File.Delete(normalizedSource);
                        OnComplete?.Invoke(true, "File moved successfully (cross-drive).");
                    }
                    catch (Exception ex)
                    {
                        // More handling?
                        OnComplete?.Invoke(false, $"Copy succeeded but delete failed: {ex.Message}");
                    }
                }
                else
                {
                    // We canceled it or it failed because the dest file does not exist
                    OnComplete?.Invoke(false, cancelFlag ? "Move cancelled by user." : "Copy failed.");
                }
            }
        }

        /// <summary>
        /// Copy a file with progress reports
        /// </summary>
        /// <param name="sourcePath">The path to the source file or directory</param>
        /// <param name="destPath">The path to copy the source path to</param>
        public void CopyFile(string sourcePath, string destPath)
        {
            // Normalize for long paths
            string normalizedSource = PathConverter.NormalizePath(sourcePath);
            string normalizedDest = PathConverter.NormalizePath(destPath);

            // Make sure that the sorce file exists
            if (!File.Exists(normalizedSource))
            {
                OnComplete?.Invoke(false, $"Source file '{sourcePath}' does not exist.");
                return;
            }

            // Do some checks
            bool isSameDrive = string.Equals(Path.GetPathRoot(normalizedSource), Path.GetPathRoot(normalizedDest), StringComparison.OrdinalIgnoreCase);
            bool cancelFlag = false;

            // Check to see if we are on the same drive
            if (isSameDrive)
            {
                // Same drive: Simple copy (File.Copy handles long paths with prefix, overwrites if exists).
                OnProgressChanged?.Invoke(100.0, "0 MB/s", Path.GetDirectoryName(sourcePath)
                                    ?? String.Empty, 0.00, 0.00, ref cancelFlag);
                try
                {
                    // Copy it
                    File.Copy(normalizedSource, normalizedDest, true);  // true = overwrite
                    OnComplete?.Invoke(true, "File copied successfully (same drive).");
                }
                catch (Exception ex)
                {
                    OnComplete?.Invoke(false, $"Error: {ex.Message}");
                }
            }
            else
            {
                // Different drives: Copy with progress (no delete).
                CopyWithProgress(normalizedSource, normalizedDest, ref cancelFlag);

                // Make sure that we didn't cancel and the destination file now exists
                if (!cancelFlag && File.Exists(normalizedDest))
                {
                    OnComplete?.Invoke(true, "File copied successfully (cross-drive).");
                }
                else
                {
                    OnComplete?.Invoke(false, cancelFlag ? "Copy cancelled by user." : "Copy failed.");
                }
            }
        }

        /// <summary>
        /// Utility method that copies a file with progress reports
        /// </summary>
        /// <param name="sourcePath">The source file path</param>
        /// <param name="destPath">The destination path</param>
        /// <param name="cancelFlag">A ref flag that allows the copy 
        /// to be cancelled by the user</param>
        private void CopyWithProgress(string sourcePath, string destPath, ref bool cancelFlag)
        {
            // Set up buffer
            const int bufferSize = 4096 * 1024; // 4MB chunks
            byte[] buffer = new byte[bufferSize];
            // Set up tracking
            var stopwatch = Stopwatch.StartNew();  // Start timing
            long lastTotalRead = 0;  // For speed sampling
            DateTime lastUpdate = DateTime.UtcNow;  // Throttle updates

            try
            {
                // Set up file streams
                using FileStream sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                using FileStream destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
                // Tracking
                long totalBytes = sourceStream.Length;
                long totalRead = 0;
                // Clean
                string cleanName = GetCleanName(Path.GetFileNameWithoutExtension(sourcePath));

                int bytesRead;
                while ((bytesRead = sourceStream.Read(buffer, 0, bufferSize)) > 0)
                {
                    // Check to see if we need to cancel
                    if (cancelFlag) break;
                    const double throttleSpeed = 0.75;

                    // Write it
                    destStream.Write(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    double percentage = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 100.0;

                    // Throttle updates: Every 500ms or at end
                    DateTime now = DateTime.UtcNow;
                    double deltaSeconds = (now - lastUpdate).TotalSeconds;
                    if (deltaSeconds < throttleSpeed && totalRead < totalBytes) continue;

                    // Calculate instantaneous speed (MB/s) over the last interval
                    double deltaBytes = totalRead - lastTotalRead;
                    double speedMBs = deltaSeconds > 0 ? (deltaBytes / deltaSeconds / (1024.0 * 1024.0)) : 0.0;
                    lastTotalRead = totalRead;
                    lastUpdate = now;

                    //// Report progress (invoke event for external use, e.g., UI)
                    //OnProgressChanged?.Invoke(percentage, "0 MB/s", ref cancelFlag);

                    // Determine units and format
                    string speedStr;
                    if (speedMBs >= 1.0)
                    {
                        speedStr = $"{speedMBs:F1} MB/s";
                    }
                    else
                    {
                        double speedKBs = deltaSeconds > 0 ? (deltaBytes / deltaSeconds / 1024.0) : 0.0;
                        if (speedKBs >= 1.0)
                        {
                            speedStr = $"{speedKBs:F1} KB/s";
                        }
                        else
                        {
                            double speedBs = deltaSeconds > 0 ? (deltaBytes / deltaSeconds) : 0.0;
                            speedStr = $"{speedBs:F0} B/s";
                        }
                    }

                    // Calculate transfer totals
                    double transferredGB = totalRead / (1024.0 * 1024.0 * 1024.0);
                    double totalGB = totalBytes / (1024.0 * 1024.0 * 1024.0);

                    // Report progress (invoke event for external use, e.g., UI)
                    OnProgressChanged?.Invoke(percentage, speedStr, cleanName,
                                        transferredGB, totalGB, ref cancelFlag);

                    // Report it to the user
                    string status = $"Copying {cleanName}: {percentage:F0}% ({speedStr})";
                    Debug.WriteLine(status);
                    //Console.Write($"\r {status,-60}");  // Pad to fixed width for overwrite
                }

                stopwatch.Stop();
                // Final average speed
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                double avgSpeedMBs = elapsedSeconds > 0 ? (totalRead / elapsedSeconds / (1024.0 * 1024.0)) : 0.0;
                Console.WriteLine("\r" + new string(' ', 60) + "\r");  // Clear line
                Console.WriteLine($"  Copied: {Path.GetFileName(destPath)} ({avgSpeedMBs:F1} MB/s avg).");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                OnComplete?.Invoke(false, $"Copy error: {ex.Message}");
                cancelFlag = true;
                Console.WriteLine($"\n  Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Utility method that generates a "clean name" from the 
        /// specified file path.
        /// </summary>
        /// <param name="path">Path to file</param>
        /// <returns>Clean Name</returns>
        /// <exception cref="ArgumentException">Name pattern mismatch</exception>
        private static string GetCleanName(string path)
        {
            // Extract clean movie name: "<Movie Name> (<Year>)"
            int yearEndIndex = path.IndexOf(')');
            if (yearEndIndex == -1)
            {
                throw new ArgumentException("Folder name does not contain a year in parentheses. <This shouldn't happen here>");
            }

            // Get the rest
            int spaceAfterYearIndex = path.IndexOf(' ', yearEndIndex + 1);
            string cleanName;
            if (spaceAfterYearIndex == -1)
            {
                cleanName = path;
            }
            else
            {
                cleanName = path.Substring(0, spaceAfterYearIndex);
            }

            // Return it
            return cleanName;
        }
    }
}
