using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting; // For IHost
using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.Console;
using Serilog; // New using for Serilog
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Hosting; // Add this for SerilogHostBuilderExtensions
using Serilog.Extensions.Logging;
//using Serilog.Sinks.Console; // New using for Serilog console sink
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
//using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
//using Microsoft.Extensions.Hosting; // For IHost
//using Microsoft.Extensions.Logging;
//using Serilog;
namespace CopyAfterProcess
{
    /// <summary>
    /// Copy After Process allows you to take transcoded files in a temporary
    /// directory to a Movie Library Folder
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Random Number Generator
        /// </summary>
        private static Random random = new Random(); // For generating random 4-digit number
        private static string sourcePath = String.Empty;
        private static string[] destPaths = Array.Empty<string>();
        private static Microsoft.Extensions.Logging.ILogger? logger;
        private static readonly string logDir = @"C:\Apps\librarymover\logs\";
        /// <summary>
        /// Main entry point
        /// </summary>
        /// <param name="args">Arguments. Usage: MovieRenamer
        /// [source_folder] [destination_folder1] [destination_folder2] ...
        /// </param>
        public static void Main(string[] args)
        {
            // Create log directory if missing
            Directory.CreateDirectory(logDir);
            // NEW: Configure Serilog (file + console)
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information() // Adjust level as needed
                                            //.WriteTo.SerilogConsole() // Optional: Log to console
                .WriteTo.File(
                    Path.Combine(logDir, "MovieRenamer-.log"), // Rolling files
                    rollingInterval: RollingInterval.Day, // Daily rollover
                    retainedFileCountLimit: 7, // Keep 7 days
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}") // Custom format
                .CreateLogger();
            // Integrate with Microsoft.Extensions.Logging
            using var host = Host.CreateDefaultBuilder(args)
                .UseSerilog(Log.Logger) // Ties Serilog to the logger factory
                .Build();
            logger = host.Services.GetRequiredService<ILogger<Program>>();
            // Start the log
            logger?.LogInformation("MovieRenamer started at {Timestamp}", DateTime.Now);
            // Set up counters
            int totalSubdirs = 0;
            int goodCopies = 0;
            int badCopies = 0;
            int totalCopyAttempts = 0;
            bool cleanUp = false;
            bool updPlex = true;
            // Make sure that we got our arguments
            if (args.Length < 2 && !Debugger.IsAttached)
            {
                Console.WriteLine("Usage: MovieRenamer <source_folder> <destination_folder1> [<destination_folder2> ...]");
                Console.WriteLine("Example: MovieRenamer \"C:\\Source\\Movies\" \"C:\\Dest\\Movies1\" \"C:\\Dest\\Movies2\"");
                logger?.LogError("Invalid arguments. Exiting...");
                return;
            }
            else if (Debugger.IsAttached)
            {
                logger?.LogDebug("Debugger attached, setting defaults...");
                // For debugging purposes, set default testing paths
                args = new string[]
                {
                    @"F:\jeff\Trans",
                    @"H:\jeff\files\Video\Movies",
                    @"\\ds-jeff.lassenhome.com\video_3\Movies"  // Added a second dest for testing
                };
                cleanUp = false;
                updPlex = false;
                logger?.LogDebug("Default settings: Source:\"{pos0}\", Dests:[\"{pos1}\", \"{pos2}\"], " +
                    "Clean Up?:{pos3}, Update Plex?:{pos4}", args[0], args[1], args[2], cleanUp, updPlex);
            }
            // Set up paths and normalize them
            sourcePath = PathConverter.NormalizePath(args[0]);
            destPaths = args.Skip(1).Select(PathConverter.NormalizePath).ToArray();
            logger?.LogInformation($"{sourcePath} -> [{string.Join(", ", destPaths)}]");
            // Validate source directory
            if (!Directory.Exists(sourcePath))
            {
                Console.WriteLine($"Error: Source folder '{sourcePath}' does not exist.");
                logger?.LogError("ERROR: Source folder: \"{pos0}\" does not exist!", sourcePath);
                return;
            }
            // Validate all destination directories
            foreach (var destPath in destPaths)
            {
                if (!Directory.Exists(destPath))
                {
                    Console.WriteLine($"Error: Destination folder '{destPath}' does not exist.");
                    logger?.LogError("ERROR: Dest folder: \"{pos0}\" does not exist!", destPath);
                    return;
                }
            }
            // Get subdirectories
            string[] sourceSubdirs = Directory.GetDirectories(sourcePath);
            totalSubdirs = sourceSubdirs.Length;
            logger?.LogDebug(" Got {pos0} sub-dirs", totalSubdirs);
            string outputMsg = string.Empty;
            // Loop through each dir
            foreach (string subdir in sourceSubdirs)
            {
                // Normalize subdir path for further use
                string normalizedSubdir = PathConverter.NormalizePath(subdir);
                string fullFolderName = Path.GetFileName(normalizedSubdir); // Safe, as GetFileName handles prefixed paths
                outputMsg = $"\nProcessing folder: {fullFolderName}";
                Console.WriteLine(outputMsg);
                logger?.LogDebug(outputMsg.Trim());
                // Extract clean movie name: "<Movie Name> (<Year>)"
                int yearEndIndex = fullFolderName.IndexOf(')');
                if (yearEndIndex == -1)
                {
                    outputMsg = $" Skipping: Invalid folder name format (no closing parenthesis).";
                    Console.WriteLine(outputMsg);
                    logger?.LogInformation(outputMsg.Trim());
                    badCopies += destPaths.Length; // Count as bad for all dests
                    totalCopyAttempts += destPaths.Length;
                    continue;
                }
                // Get year
                int spaceAfterYearIndex = fullFolderName.IndexOf(' ', yearEndIndex + 1);
                string cleanName;
                if (spaceAfterYearIndex == -1)
                {
                    cleanName = fullFolderName;
                }
                else
                {
                    cleanName = fullFolderName.Substring(0, spaceAfterYearIndex);
                }
                // Output
                outputMsg = $" Clean name: {cleanName}";
                Console.WriteLine(outputMsg);
                logger?.LogInformation(outputMsg);
                // Find the source movie file matching "<fullFolderName>-1.<ext>"
                string[] movieFiles = Directory.GetFiles(subdir, $"{fullFolderName}-1.*")
                                              .Where(f => Path.GetExtension(f).Length > 0)
                                              .ToArray();
                // Make sure that we got something
                if (movieFiles.Length == 0)
                {
                    outputMsg = $" Skipping: No matching movie file found (expected '{fullFolderName}-1.<ext>').";
                    Console.WriteLine(outputMsg);
                    logger?.LogInformation(outputMsg);
                    badCopies += destPaths.Length;
                    totalCopyAttempts += destPaths.Length;
                    continue;
                }
                // Check it
                if (movieFiles.Length > 1)
                {
                    Console.WriteLine($" Warning: Multiple matching files found in source. Processing the first one.");
                    logger?.LogDebug(" Mulitple matching mkvs found, attempting to determine course of action");
                    movieFiles = movieFiles
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .ToArray();
                    outputMsg = $" Sorted {movieFiles.Length} matching files by size (largest first).";
                    Console.WriteLine(outputMsg);
                    logger?.LogDebug(outputMsg);
                }
                // Get the old file path
                string oldFilePath = movieFiles[0];
                logger?.LogDebug(" Got movie file: \"{pos0}\"", oldFilePath);
                // Check to make sure that the source file is okay (Ex. Term... Genysis keeps encoding with errors, so don't move it)
                outputMsg = " Attempting to validate existing video file. ";
                Console.WriteLine(outputMsg);
                logger?.LogInformation(outputMsg);
                string errMsg = string.Empty;
                if (!ValidateVideoFile(oldFilePath, out errMsg))
                {
                    // Fail
                    badCopies += destPaths.Length;
                    totalCopyAttempts += destPaths.Length;
                    outputMsg = $" ERROR!!!!: Input file is not a valid video file! {errMsg}";
                    Console.WriteLine(outputMsg);
                    logger?.LogError(outputMsg);
                    continue;
                }
                else
                {
                    // Success
                    outputMsg = " Success! Video file is valid";
                    Console.WriteLine(outputMsg);
                    logger?.LogInformation(outputMsg);
                }
                // Now process each destination
                int subdirBadCount = 0;
                bool allGood = true;
                foreach (string destPath in destPaths)
                {
                    totalCopyAttempts++;
                    // Normalize dest subdir path
                    string destSubdir = PathConverter.NormalizePath(Path.Combine(destPath, cleanName)); // Combine first, then normalize
                    // Check to see if it exists
                    if (!Directory.Exists(destSubdir))
                    {
                        outputMsg = $" Warning: Destination subfolder '{cleanName}' does not exist in '{destPath}'. Skipping this dest.";
                        Console.WriteLine(outputMsg);
                        logger?.LogInformation(outputMsg);
                        subdirBadCount++;
                        allGood = false;
                        continue;
                    }
                    // Always delete existing "-mediainfo.xml" if present (for this dest)
                    logger?.LogDebug(" Attempting to find the corresponding '-mediainfo.xml' file in {pos0}.", destSubdir);
                    string[] existingMediaInfoPaths = Directory.GetFiles(destSubdir, $"*-mediainfo.xml")
                                                           .ToArray();
                    // Loop through all the files in case there are more than one
                    foreach (var existingMediaInfoPath in existingMediaInfoPaths)
                    {
                        // Make sure that the file is accessible
                        if (!string.IsNullOrEmpty(existingMediaInfoPath))
                        {
                            try
                            {
                                //File.Delete(existingMediaInfoPath);
                                MoveToRecycleBin(existingMediaInfoPath);
                                outputMsg = $" Deleted existing: {Path.GetFileName(existingMediaInfoPath)}";
                                Console.WriteLine(outputMsg);
                                logger?.LogInformation(outputMsg);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($" Warning: Could not delete existing mediainfo: {ex.Message}");
                                logger?.LogError(" ERROR!!!: \"{pos0}\"", ex.Message);
                            }
                        }
                    }
                    // Find the target file name in destination (assume first non-trailer .mkv file;)
                    string[] destMovieFiles = Directory.GetFiles(destSubdir, "*.mkv")
                                                      .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("-trailer", StringComparison.OrdinalIgnoreCase))
                                                      .ToArray();
                    // Make sure that there are destination files
                    if (destMovieFiles.Length == 0)
                    {
                        outputMsg = $" Skipping: No .mkv file found in destination folder '{cleanName}' for '{destPath}'.";
                        Console.WriteLine(outputMsg);
                        logger?.LogInformation(outputMsg);
                        subdirBadCount++;
                        allGood = false;
                        continue;
                    }
                    // Check for multiple matches
                    if (destMovieFiles.Length > 1)
                    {
                        outputMsg = $" Warning: Multiple .mkv files found in destination '{destPath}':";
                        Console.WriteLine(outputMsg);
                        logger?.LogError(outputMsg);
                        foreach (var curDir in destMovieFiles)
                        {
                            outputMsg = $" - Multi-File: \"{curDir}\"";
                            Console.WriteLine(outputMsg);
                            logger?.LogError(outputMsg);
                        }
                        subdirBadCount++;
                        allGood = false;
                        continue;
                    }
                    // Normalize target file path
                    string targetFilePath = PathConverter.NormalizePath(destMovieFiles[0]);
                    string newFileName = Path.GetFileName(targetFilePath);
                    string newSourcePath = PathConverter.NormalizePath(Path.Combine(destSubdir, newFileName)); // Combine then normalize
                    logger?.LogDebug(" *targetFilePath: \"{pos0}\", newFileName: \"{pos1}\", " +
                          "newSourcePath: \"{pos2}\".", targetFilePath, newFileName, newSourcePath);
                    //Test size Skip copy if sizes match (only if dest exists)
                    if (IsAreEqual(oldFilePath, newSourcePath))
                    {
                        logger?.LogInformation(" File sizes are equal for '{pos0}', generating media info and skipping copy.", destPath);
                        // Generate media info for the existing file
                        GenerateMediaInfoFile(newSourcePath, destSubdir);
                        goodCopies++;
                        continue;
                    }
                    string lockErrMsg = string.Empty;
                    // Check to see if the file is locked
                    try
                    {
                        logger?.LogDebug(" Attempting to lock file...");
                        // Open a stream
                        using (FileStream stream = File.Open(newSourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            // If we reach here, the file is not locked.
                            stream.Close();
                            outputMsg = $" File is not locked or in use.";
                            Console.WriteLine(outputMsg);
                            logger?.LogInformation(outputMsg);
                        }
                    }
                    catch (IOException ex)
                    {
                        subdirBadCount++;
                        allGood = false;
                        // Check for the specific "file in use" error.
                        if (ex.HResult == -2147024864) // ERROR_SHARING_VIOLATION
                        {
                            outputMsg = $" Skipping: Target file '{newFileName}' is currently in use or locked in '{destPath}'.";
                            Console.WriteLine(outputMsg);
                            logger?.LogInformation(outputMsg);
                            badCopies++;
                            continue;
                        }
                        else
                        {
                            logger?.LogError(" ERROR: {pos0}", ex.Message);
                            badCopies++;
                            continue; // Don't throw, continue to next dest
                        }
                    }
                    // Try to copy
                    try
                    {
                        // Set up mover
                        var mover = new LongPathProgressFileMover();
                        logger?.LogDebug(" Preparing to copy files to '{pos0}'...", destPath);
                        // Set up delegates
                        mover.OnProgressChanged += (progress, speed, name,
                                            transferredGB, totalGB, ref cancel) =>
                        {
                            // Report it to the user
                            string status = $"Copying {name} to {destPath}: {progress:F2}% ({speed}) - " +
                                            $"{{{transferredGB:F4} GB / {totalGB:F4} GB}}";
                            Console.Write($"\r {status,-100}"); // Pad to fixed width for overwrite
                        };
                        mover.OnComplete += (success, message) =>
                        {
                            if (success)
                            {
                                Console.WriteLine(message);
                                logger?.LogInformation("Success! {pos0}", message);
                            }
                            else
                            {
                                Console.WriteLine("Error!!!1");
                                Console.WriteLine($" {message}");
                                logger?.LogError("ERROR!!!: {pos0}", message);
                            }
                        };
                        // Set up long paths
                        string longSource = oldFilePath;
                        string longDest = newSourcePath;
                        // Sanity check
                        if (PathConverter.IsLongPath(longSource))
                        {
                            outputMsg = " Detected long path - using \\\\?\\\\ prefix.";
                            Console.WriteLine(outputMsg);
                            logger?.LogDebug(outputMsg);
                        }
                        // Copy the file, with progress reports
                        logger?.LogDebug(" Starting the copy process");
                        mover.CopyFile(longSource, longDest);
                        logger?.LogDebug(" Finished copying the file!");
                        // Now that we have moved, check the files for equal sizes
                        if (IsAreEqual(longSource, longDest))
                        {
                            // Success
                            outputMsg = $" Renamed: {Path.GetFileName(oldFilePath)} -> {newFileName} in '{destPath}'";
                            Console.WriteLine(outputMsg);
                            logger?.LogInformation(outputMsg);
                            // Check video file for errors via hash
                            outputMsg = " Attempting to check file for errors";
                            Console.WriteLine(outputMsg);
                            logger?.LogDebug(outputMsg);
                            if (VerifyVideoFile(longSource, longDest))
                            {
                                // Sweet!
                                Console.WriteLine(" Video file has no errors!");
                                logger?.LogInformation(" Success! Video file has no errors.");
                            }
                            else
                            {
                                // Bummer
                                Console.WriteLine(" Error, video file is damaged!");
                                logger?.LogError(" ERROR!!! Video file damaged!");
                                subdirBadCount++;
                                allGood = false;
                                badCopies++;
                                continue;
                            }
                            // Generate a media info file so that we know that we already processed this item
                            logger?.LogDebug(" Generating JSON media info so we know that we have touched this folder");
                            GenerateMediaInfoFile(longDest, Path.GetDirectoryName(longDest) ?? String.Empty); // Pass dest file and its dir
                            goodCopies++;
                        }
                        else
                        {
                            // Fail
                            outputMsg = $" Error: File sizes do not match after copy to '{destPath}'.";
                            Console.WriteLine(outputMsg);
                            logger?.LogError(outputMsg);
                            subdirBadCount++;
                            allGood = false;
                            badCopies++;
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Error
                        Console.WriteLine($" Error renaming file to '{destPath}': {ex.Message}");
                        logger?.LogError(" ERROR!!!: Error renaming file: \"{pos0}\"", ex.Message);
                        subdirBadCount++;
                        allGood = false;
                        badCopies++;
                        continue;
                    }
                } // End foreach dest
                // After processing all dests for this subdir, clean up source if all good
                if (allGood && cleanUp)
                {
                    logger?.LogInformation(" All destinations processed successfully, cleaning source folder.");
                    doCleanUp(normalizedSubdir); // Now passes the subdir path
                }
                else if (!allGood)
                {
                    logger?.LogWarning(" Partial failure for subdir '{0}', source not cleaned.", fullFolderName);
                }
                // Console
                Console.WriteLine(Environment.NewLine);
                Console.WriteLine("---");
                Console.WriteLine("Moving to next folder");
                Console.WriteLine(Environment.NewLine);
                logger?.LogInformation($"Moving to next folder.{Environment.NewLine}" +
                    $"{Environment.NewLine}");
            }
            // Done
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine("\nProcessing complete.");
            logger?.LogInformation("Done processing...");
            outputMsg = $"Total Subdirs: {totalSubdirs}, Total Attempts: {totalCopyAttempts}, Good Copies: {goodCopies}, Bad Copies: {badCopies}.";
            Console.WriteLine(outputMsg);
            logger?.LogInformation(outputMsg);
            Console.WriteLine(Environment.NewLine);
            // Check to see if we want to update the Plex Library
            if (updPlex)
            {
                outputMsg = "Telling Plex to scan...";
                Console.WriteLine(outputMsg);
                logger?.LogInformation(outputMsg);
                UpdatePlex();
            }
            // Close-out
            logger?.LogInformation("Processing complete at {Timestamp}", DateTime.Now);
            Log.CloseAndFlush(); // Clean shutdown
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }
        /// <summary>
        /// Utility method that recycles videos. To be called with matching sizes
        /// </summary>
        /// <param name="filePath">The path to the file or folder</param>
        private static void doCleanUp(string path)
        {
            // Make sure it exists
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                string outputMsg = " Error doing cleanup, path does not exist.";
                Console.WriteLine(outputMsg);
                logger?.LogError(outputMsg);
                return;
            }
            // Recycle
            logger?.LogDebug(" Calling MoveToRecycleBin Method");
            MoveToRecycleBin(path);
            logger?.LogDebug(" Done cleaning up");
        }
        /// <summary>
        /// Utility method that moves a file or folder to the
        /// Recycyle Bin on Windows, or normal delete otherwise
        /// </summary>
        /// <param name="path">Path to the file or folder</param>
        /// <exception cref="ArgumentException"></exception>
        public static void MoveToRecycleBin(string path)
        {
            // Check to see if the file or directory exists
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                logger?.LogError($"\"{path}\" does not exist.");
                throw new ArgumentException($"Path '{path}' does not exist.");
            }
            // Check to see if we are on Windows
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                logger?.LogDebug(" Not on a Windows machine, doing a normal deletion");
                // Nope, do a manual delete
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
                // Leave it
                logger?.LogDebug(" Done deleting path");
                return;
            }
            // Shrink the source path
            if (Directory.Exists(path) || (File.Exists(path) && !path.EndsWith("-mediainfo.xml")))
            {
                logger?.LogDebug(" Attempting to shrink the path length...");
                path = ShrinkLength(path, GetCleanName(path));
            }
            else if (File.Exists(path))
            {
                logger?.LogDebug(" The path is an actual file, so we don't need to do anything to the path");
                path = path;
            }
            // Convert to short path for COM compatibility
            string shortPath = PathConverter.ToShortPath(path);
            bool isDirectory = Directory.Exists(path);
            // Make sure that we have either a file or a folder
            if (!File.Exists(shortPath) && !Directory.Exists(shortPath))
            {
                logger?.LogError($"Path '{path}' does not exist.");
                throw new ArgumentException($"Path '{path}' does not exist.");
            }
            // Set up shell object
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            dynamic shellApp = Activator.CreateInstance(shellType)!;
            // 0xa is the constant for the Recycle Bin folder (ShellSpecialFolderConstants.ssfRECYCLEBIN).
            var recycleBin = shellApp.Namespace(0xa);
            try
            {
                // Use short path for MoveHere
                logger?.LogDebug(" Attempting short path Recycle...");
                recycleBin.MoveHere(shortPath);
            }
            catch (Exception ex)
            {
                logger?.LogDebug(" Short Recycle failed, attempting longer path...");
                // Fallback: Try original long path
                try
                {
                    recycleBin.MoveHere(path);
                }
                catch
                {
                    logger?.LogError(" Recycle failed! Attempting normal delete.");
                    logger?.LogDebug($" Fallback to delete on Windows: '{path}'.");
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }
                        else if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        logger?.LogDebug(" Successfully deleted");
                    }
                    catch
                    {
                        // It really failed
                        logger?.LogError("Failed to move '{path}' to Recycle Bin " +
                            "(tried short: '{shortPath}'): {exMsg}", path, shortPath, ex.Message);
                        throw new IOException($"Failed to move '{path}' to Recycle " +
                            $"Bin (tried short: '{shortPath}'): {ex.Message}", ex);
                    }
                }
            }
        }
        /// <summary>
        /// Checks to see if the two specified paths are
        /// equal in size.
        /// </summary>
        /// <param name="oldFilePath">Original File Path</param>
        /// <param name="newSourcePath">New File Path</param>
        /// <returns>True if they are equal</returns>
        private static bool IsAreEqual(string oldFilePath, string newSourcePath)
        {
            // Make sure that the file exists
            if (File.Exists(oldFilePath) && File.Exists(newSourcePath))
            {
                try
                {
                    // Get length
                    long sourceSize = new FileInfo(oldFilePath).Length;
                    long destSize = new FileInfo(newSourcePath).Length;
                    logger?.LogDebug(" File Size - Source: {pos0}, Dest: {pos1}", sourceSize, destSize);
                    // Check it
                    if (sourceSize == destSize)
                    {
                        logger?.LogDebug(" File sizes equal, sweet!");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    string outputMsg = $" Warning: Could not compare file sizes: {ex.Message}. Proceeding with copy.";
                    Console.WriteLine(outputMsg);
                    logger?.LogError(outputMsg);
                }
            }
            // We made it this far, so we failed
            logger?.LogDebug(" File size mismatch, oh well. Continuing...");
            return false;
        }
        /// <summary>
        /// Utility method to shorten folder and file names on the source directory
        /// </summary>
        /// <param name="path">Path to source file</param>
        /// <param name="cleanName">Clean name to use in shortening</param>
        /// <returns>A shortened path</returns>
        private static string ShrinkLength(string path, string cleanName)
        {
            // Generate random 4-digit number
            int randomNum = random.Next(1000, 9999);
            const int truncLen = 10;
            // Debug
            logger?.LogDebug(" Shortening the folder path to {pos0}", randomNum.ToString("0000"));
            // Truncate cleanName to 20 chars
            string truncatedName = Path.GetFileName(cleanName).Length > truncLen
                                    ? Path.GetFileName(cleanName).Substring(0, truncLen)
                                    : Path.GetFileName(cleanName);
            string newFolderPath = PathConverter.NormalizePath(Path.Combine(sourcePath,
                                    randomNum.ToString("0000")/*, truncatedName*/));
            logger?.LogDebug(" Truncated name: \"{pos0}\", newFolderPath: \"{pos1}\".",
                                    truncatedName, newFolderPath);
            // Check length
            if (newFolderPath.Length >= 260)
            {
                Console.WriteLine($" Warning: New folder path exceeds MAX_PATH limit. Recycle Bin will be skipped.");
                logger?.LogDebug(" Warning, the new folder path still exceeds MAX_PATH limit.");
                return path;
            }
            // Check if new folder name already exists
            if (Directory.Exists(newFolderPath))
            {
                Console.WriteLine($" Warning: Target folder name '{Path.GetFileName(newFolderPath)}" +
                    $"' already exists. Skipping folder rename.");
                logger?.LogCritical(" Warning, target folder: \"{pos0}\", already exists.",
                    Path.GetFileName(newFolderPath));
                return newFolderPath;
            }
            else
            {
                try
                {
                    // Create it
                    // C#
                    string destParent = Path.GetDirectoryName(newFolderPath) ?? newFolderPath;
                    if (!Directory.Exists(destParent))
                    {
                        // Create using normalized path
                        logger?.LogDebug(" Attempting to create the directory...");
                        Directory.CreateDirectory(PathConverter.NormalizePath(destParent));
                    }
                    // Move it
                    logger?.LogDebug(" Renaming the folder...");
                    Directory.Move(PathConverter.NormalizePath(Path.GetDirectoryName(path)
                                ?? path), newFolderPath);
                    string outputMsg = $" Renamed source folder: '{Path.GetFileName(path)}' -> " +
                        $"'{Path.GetDirectoryName(Path.Combine(newFolderPath, randomNum.ToString()))}'";
                    Console.WriteLine(outputMsg);
                    logger?.LogDebug(outputMsg);
                    // Return the new folder path
                    return newFolderPath;
                }
                catch (Exception ex)
                {
                    // Error
                    Console.WriteLine($" Error renaming source folder: {ex.Message}");
                    logger?.LogError($" Error renaming source folder: \"{ex.Message}\"");
                    throw;
                }
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
                logger?.LogError(" Folder name is not properly formatted. We shouldn't be getting this error here!");
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
            logger?.LogDebug(" Clean name is: \"{pos0}\"", cleanName);
            return cleanName;
        }
        // Dictionary to hold parsed args: key = name, value = string
        private static Dictionary<string, string> ParseNamedArgs(string[] args)
        {
            var namedArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Trim();
                if (arg.StartsWith("--") || arg.StartsWith('/'))
                {
                    string key = arg.Substring(2).Trim().ToLower(); // e.g., "--input" -> "input"
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--") && !args[i + 1].StartsWith('/'))
                    {
                        namedArgs[key] = args[++i].Trim(); // Next arg is value
                    }
                    else
                    {
                        namedArgs[key] = string.Empty; // Flag without value (e.g., --verbose)
                    }
                }
                else if (!namedArgs.ContainsKey("positional")) // Fallback for unnamed
                {
                    namedArgs["positional"] = arg; // Handle first positional if needed
                }
            }
            return namedArgs;
        }
        /// <summary>
        /// Triggers a Plex Library scan on the specified Library
        /// </summary>
        /// <remarks>
        /// 1 - Movies - JPC285
        /// 2 - TV Shows - JPC285
        /// 3 - Music - JPC285
        /// 4 - Test Movies
        /// 5 - Test TV Shows
        /// </remarks>
        /// <param name="libraryID">The Library ID to update.
        /// Defaults to Movies - JPC285</param>
        private static void UpdatePlex(int libraryID = 1)
        {
            try
            {
                logger?.LogDebug(" Attempting to launch Plex Media Scanner...");
                // Trigger full scan (or use --section {id} for specific library)
                var scannerPath = @"C:\Program Files\Plex\Plex Media Server\Plex Media Scanner.exe";
                var startInfo = new ProcessStartInfo
                {
                    FileName = scannerPath,
                    Arguments = $"--scan --section {libraryID.ToString()}", // 1 = Movies - JPC
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using (var process = Process.Start(startInfo))
                {
                    process?.Start(); // Optional: Wait for completion
                    Console.WriteLine(" Plex scan triggered.");
                    logger?.LogDebug(" Plex scan triggered.");
                }
            }
            catch (Exception ex)
            {
                string outputMsg = $" ERROR!!!: Problem launching process! {ex.Message}.";
                Console.WriteLine(outputMsg);
                logger?.LogError(outputMsg);
            }
        }
        /// <summary>
        /// Save a .json file containing the media info for the specified file,
        /// saved to the specified folder
        /// </summary>
        /// <param name="videoPath">Path to source video</param>
        /// <param name="outputDir">Destination folder to save MediaInfo to</param>
        private static void GenerateMediaInfoFile(string videoPath, string outputDir)
        {
            logger?.LogDebug(" Beginning to create JSON...");
            // Set up paths
            string outputMsg = string.Empty;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);
            string mediaInfoPath = Path.Combine(outputDir, $"{fileNameWithoutExt}_media.json");
            // ffprobe command: Quiet mode, JSON format, show format/streams details
            string ffprobePath = "ffprobe"; // Assumes in PATH; else: @"C:\ffmpeg\bin\ffprobe.exe"
            string arguments = $"-v quiet -print_format json -show_format -show_streams \"{videoPath}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            try
            {
                logger?.LogDebug(" Attempting to start ffprobe for \"{pos0}\".", fileNameWithoutExt);
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    outputMsg = $" Warning: Could not start ffprobe for {fileNameWithoutExt}.";
                    Console.WriteLine(outputMsg);
                    logger?.LogError(outputMsg);
                    return;
                }
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    outputMsg = $" Warning: ffprobe error for {fileNameWithoutExt}: {error}";
                    Console.WriteLine(outputMsg);
                    logger?.LogError(outputMsg);
                    return;
                }
                // Save JSON to file
                File.WriteAllText(mediaInfoPath, output, Encoding.UTF8);
                outputMsg = $" Generated: {Path.GetFileName(mediaInfoPath)}";
                Console.WriteLine(outputMsg);
                logger?.LogInformation(outputMsg);
            }
            catch (Exception ex)
            {
                outputMsg = $" Error generating media info: {ex.Message}";
                Console.WriteLine(outputMsg);
                logger?.LogError(outputMsg);
            }
        }
        /// <summary>
        /// Utility method that attempts to generate a "[Movie]-mediainfo.xml" similar
        /// to what TinyMediaManager does. Not using at the moment
        /// </summary>
        /// <param name="videoPath">The path to the source video</param>
        /// <param name="destSubdir">Where to save the XML file</param>
        private static void GenerateMediaInfoXml(string videoPath, string destSubdir)
        {
            logger?.LogDebug(" Beginning to create XML...");
            // Set up paths
            string outputMsg = string.Empty;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);
            string mediaInfoPath = Path.Combine(destSubdir, $"{fileNameWithoutExt}-mediainfo.xml");
            // MediaInfo command: XML output, full details
            string mediainfoExe = @"C:\Program Files\MediaInfo\MediaInfo.exe"; // Assume in PATH; else: @"C:\MediaInfo\MediaInfo.exe"
            string arguments = $"--Output=XML \"{videoPath}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = mediainfoExe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            try
            {
                logger?.LogDebug(" Attempting to start ffproMediaInfobe for \"{pos0}\".",
                                fileNameWithoutExt);
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    outputMsg = $" Warning: Could not start MediaInfo for {fileNameWithoutExt}.";
                    Console.WriteLine(outputMsg);
                    logger?.LogError(outputMsg);
                    return;
                }
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    outputMsg = $" Warning: MediaInfo error for {fileNameWithoutExt}: {error}";
                    Console.WriteLine(outputMsg);
                    logger?.LogError(outputMsg);
                    return;
                }
                // Save raw XML
                File.WriteAllText(mediaInfoPath, output, Encoding.UTF8);
                outputMsg = $" Generated: {Path.GetFileName(mediaInfoPath)} (TMM-style XML)";
                Console.WriteLine(outputMsg);
                logger?.LogInformation(outputMsg);
            }
            catch (Exception ex)
            {
                outputMsg = $" Error generating -mediainfo.xml: {ex.Message}";
                Console.WriteLine(outputMsg);
                logger?.LogError(outputMsg);
            }
        }
        private static bool VerifyVideoFile(string sourcePath, string targetPath)
        {
            //
            return HashComparer.CompareFileHashes(sourcePath, targetPath);
        }
        private static bool PartialDecodeValidate(string videoPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            string ffmpegPath = "ffmpeg";
            string unprefixedPath = PathConverter.GetUnprefixedPath(videoPath);
            const int startSec = 0; // Start scan here
            const int startEndSec = 900; // End start scan here
            const int endStartSec = 20; // Start End scan here
            // Run 1: Head (first 10 minutes or 10% - adjust -t as needed)
            bool headValid = RunPartialDecode(ffmpegPath, unprefixedPath,
                                $"-ss {startSec} -t {startEndSec}"); // 10 min
            // Run 2: Tail (last 10 seconds)
            bool tailValid = RunPartialDecode(ffmpegPath, unprefixedPath,
                                $"-ss -{endStartSec}");
            if (headValid && tailValid)
            {
                Console.WriteLine($" Partial validated: {Path.GetFileName(videoPath)} (head and tail OK).");
                return true;
            }
            else
            {
                errorMessage = "Partial decode failed on head or tail.";
                return false;
            }
        }
        private static bool RunPartialDecode(string ffmpegPath, string inputPath, string seekArgs)
        {
            string arguments = $"-hwaccel cuda -v error -fflags +igndts -avoid_negative_ts make_zero {seekArgs} -i \"{inputPath}\" -f null -";
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            try
            {
                using var process = Process.Start(startInfo);
                if (process == null) return false;
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                string trimmedStderr = stderr.Trim();
                bool hasFatalError = process.ExitCode != 0 ||
                    (trimmedStderr.Contains("Invalid") && !trimmedStderr.Contains("dts")) ||
                    trimmedStderr.Contains("Error") || trimmedStderr.Contains("Failed");
                if (hasFatalError)
                {
                    Console.WriteLine($" Partial decode warning ({seekArgs}): {trimmedStderr}");
                    return false; // Flag as invalid only on fatal
                }
                return true;
            }
            catch
            {
                return false; // Fail safe
            }
        }
        /// <summary>
        /// Utility method that uses ffmpeg to attempt to check the video
        /// file for errors
        /// </summary>
        /// <param name="videoPath">The path to the video file to test</param>
        /// <param name="errorMessage">The error message, if any</param>
        /// <returns>True if the file is valid</returns>
        private static bool ValidateVideoFile(string videoPath, out string errorMessage)
        {
            // Initialize
            logger?.LogDebug(" Beginning to validate video file...");
            errorMessage = string.Empty;
            // FFmpeg command: Validate by decoding to null (reports errors only)
            string ffmpegPath = "ffmpeg"; // Assume in PATH; else: @"C:\ffmpeg\bin\ffmpeg.exe"
            //string arguments = $"-v error -i \"{videoPath}\" -f null -y"; // -y to overwrite temp if needed
            //string arguments = $"-v error -fflags +igndts -avoid_negative_ts make_zero -i \"{videoPath}\" -f null -";
            string arguments = $"-hwaccel cuda -v error -fflags +igndts -avoid_negative_ts make_zero -i \"{videoPath}\" -f null -";
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    errorMessage = "Could not start FFmpeg process.";
                    Console.WriteLine(" " + errorMessage);
                    logger?.LogError(" " + errorMessage);
                    return false;
                }
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                string trimmedStderr = stderr.Trim();
                bool hasFatalError = process.ExitCode != 0 ||
                                    (trimmedStderr.Contains("Invalid") && !trimmedStderr.Contains("dts")) || // Ignore DTS but catch other invalids
                                    trimmedStderr.Contains("Error") || trimmedStderr.Contains("Failed");
                //if (process.ExitCode != 0 || !string.IsNullOrEmpty(stderr.Trim()))
                if (hasFatalError)
                {
                    errorMessage = $"Validation failed: {stderr.Trim()} (Exit code: {process.ExitCode})";
                    Console.WriteLine($" Warning: {errorMessage}");
                    logger?.LogError($" Warning: {errorMessage}");
                    return false; // Invalid
                }
                // Log warnings separately
                if (trimmedStderr.Contains("dts") || trimmedStderr.Contains("monotonically"))
                {
                    logger?.LogDebug($" Warning: Minor timestamp issues in {Path.GetFileName(videoPath)} (ignored; file is playable).");
                }
                else
                {
                    logger?.LogDebug($" Validated: {Path.GetFileName(videoPath)} (no errors detected, GPU: cuda).");
                }
                Console.WriteLine($" Validated: {Path.GetFileName(videoPath)} (no errors detected).");
                logger?.LogInformation(" Success! \"{pos0}\" has no errors!", Path.GetFileName(videoPath));
                return true; // Valid
            }
            catch (Exception ex)
            {
                errorMessage = $"Validation error: {ex.Message}";
                Console.WriteLine($" Error validating {Path.GetFileName(videoPath)}: {ex.Message}");
                logger?.LogError($" Error validating {Path.GetFileName(videoPath)}: {ex.Message}");
                return false;
            }
        }
    }
}