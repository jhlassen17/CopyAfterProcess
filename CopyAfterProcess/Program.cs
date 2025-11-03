using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;

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
        private static Random random = new Random();  // For generating random 4-digit number
        private static string sourcePath = String.Empty;
        private static string destPath = String.Empty;


        /// <summary>
        /// Main entry point
        /// </summary>
        /// <param name="args">Arguments.  Usage: MovieRenamer 
        /// [source_folder] [destination_folder]
        /// </param>
        public static void Main(string[] args)
        {
            // Set up counters
            int totalFiles = 0;
            int goodFiles = 0;
            int badFiles = 0;
            bool cleanUp = false;
            bool updPlex = true;

            // Make sure that we got our arguments
            if (args.Length != 2 && !Debugger.IsAttached)
            {
                Console.WriteLine("Usage: MovieRenamer <source_folder> <destination_folder>");
                Console.WriteLine("Example: MovieRenamer \"C:\\Source\\Movies\" \"C:\\Dest\\Movies\"");
                return;
            }
            else if (Debugger.IsAttached)
            {
                // For debugging purposes, set default testing paths
                args = new string[]
                {
                    @"F:\jeff\Trans",
                    @"H:\jeff\files\Video\Movies"
                };
                cleanUp = false;
                updPlex = false;
            }

            // Set up paths and normalize them
            sourcePath = PathConverter.NormalizePath(args[0]);
            destPath = PathConverter.NormalizePath(args[1]);

            // Validate source and destination directories
            if (!Directory.Exists(sourcePath))
            {
                Console.WriteLine($"Error: Source folder '{sourcePath}' does not exist.");
                return;
            }

            if (!Directory.Exists(destPath))
            {
                Console.WriteLine($"Error: Destination folder '{destPath}' does not exist.");
                return;
            }

            // Get subdirectories
            string[] sourceSubdirs = Directory.GetDirectories(sourcePath);

            // Loop through each dir
            foreach (string subdir in sourceSubdirs)
            {
                totalFiles++;

                // Normalize subdir path for further use
                string normalizedSubdir = PathConverter.NormalizePath(subdir);
                string fullFolderName = Path.GetFileName(normalizedSubdir); // Safe, as GetFileName handles prefixed paths
                Console.WriteLine($"\nProcessing folder: {fullFolderName}");

                // Extract clean movie name: "<Movie Name> (<Year>)"
                int yearEndIndex = fullFolderName.IndexOf(')');
                if (yearEndIndex == -1)
                {
                    Console.WriteLine($"  Skipping: Invalid folder name format (no closing parenthesis).");
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
                Console.WriteLine($"  Clean name: {cleanName}");

                // Normalize dest subdir path
                string destSubdir = PathConverter.NormalizePath(Path.Combine(destPath, cleanName)); // Combine first, then normalize

                // Check to see if it exists
                if (!Directory.Exists(destSubdir))
                {
                    Console.WriteLine($"  Warning: Destination subfolder '{cleanName}' does not exist. Skipping.");
                    badFiles++;
                    continue;
                }

                // Find the target file name in destination (assume first non-trailer .mkv file;)
                string[] destMovieFiles = Directory.GetFiles(destSubdir, "*.mkv")
                                                  .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("-trailer", StringComparison.OrdinalIgnoreCase))
                                                  .ToArray();
                // Make sure that there are destination files
                if (destMovieFiles.Length == 0)
                {
                    Console.WriteLine($"  Skipping: No .mkv file found in destination folder '{cleanName}'.");
                    badFiles++;
                    continue;
                }

                // Check for multiple matches
                if (destMovieFiles.Length > 1)
                {
                    Console.WriteLine($"  Warning: Multiple .mkv files found in destination:");

                    foreach (var curDir in destMovieFiles)
                    {
                        Console.WriteLine($"  Multi-File: \"{curDir}\"");
                    }

                    badFiles++;
                    continue;
                }

                // Normalize target file path
                string targetFilePath = PathConverter.NormalizePath(destMovieFiles[0]);
                string newFileName = Path.GetFileName(targetFilePath);
                string newSourcePath = PathConverter.NormalizePath(Path.Combine(destPath, Path.GetFileName(destSubdir) ?? destSubdir, newFileName)); // Combine then normaliz

                // Find the source movie file matching "<fullFolderName>-1.<ext>"
                string[] movieFiles = Directory.GetFiles(subdir, $"{fullFolderName}-1.*")
                                              .Where(f => Path.GetExtension(f).Length > 0)
                                              .ToArray();

                // Male sure that we got something
                if (movieFiles.Length == 0)
                {
                    Console.WriteLine($"  Skipping: No matching movie file found (expected '{fullFolderName}-1.<ext>').");
                    badFiles++;
                    continue;
                }

                // Check it
                if (movieFiles.Length > 1)
                {
                    Console.WriteLine($"  Warning: Multiple matching files found in source. Processing the first one.");
                }

                // Get the old file path
                string oldFilePath = movieFiles[0];

                //Test size Skip copy if sizes match (only if dest exists)
                if (IsAreEqual(oldFilePath, newSourcePath))
                {
                    goodFiles++;
                    // Should we offer to delete the original since they match?
                    // TODO: Recycle Bin
                    if (cleanUp) doCleanUp(oldFilePath);
                    continue;
                }

                // Check to see if the file is locked
                try
                {
                    // Open a stream
                    using (FileStream stream = File.Open(newSourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        // If we reach here, the file is not locked.
                        stream.Close();
                        Console.WriteLine($"  File is not locked or in use.");
                    }
                }
                catch (IOException ex)
                {
                    badFiles++;

                    // Check for the specific "file in use" error.
                    if (ex.HResult == -2147024864) // ERROR_SHARING_VIOLATION
                    {
                        Console.WriteLine($"  Skipping: Target file '{newFileName}' is currently in use or locked.");
                        continue;
                    }
                    else
                    {
                        throw; // Rethrow if it's a different IOException
                    }
                }

                // Try to copy
                try
                {
                    // Set up mover
                    var mover = new LongPathProgressFileMover();

                    // Set up delegates
                    mover.OnProgressChanged += (progress, speed, name,
                                        transferredGB, totalGB, ref cancel) =>
                    {
                        // Report it to the user
                        string status = $"Copying {name}: {progress:F2}% ({speed}) - " +
                                        $"{{{transferredGB:F4} GB / {totalGB:F4} GB}}";
                        Console.Write($"\r {status,-100}");  // Pad to fixed width for overwrite
                    };

                    mover.OnComplete += (success, message) =>
                    {
                        if (success)
                        {
                            Console.WriteLine(message);
                        }
                        else
                        {
                            Console.WriteLine("Error!!!1");
                            Console.WriteLine(message);
                        }
                    };

                    // Set up long paths
                    string longSource = oldFilePath;
                    string longDest = newSourcePath;

                    // Sanity check
                    if (PathConverter.IsLongPath(longSource))
                        Console.WriteLine("  Detected long path - using \\?\\ prefix.");

                    // Copy the file, with progress reports
                    mover.CopyFile(longSource, longDest);

                    // Now that we have moved, check the files for equal sizes
                    if (IsAreEqual(longSource, longDest))
                    {
                        // Success
                        Console.WriteLine($"  Renamed: {Path.GetFileName(oldFilePath)} -> {newFileName}");
                        goodFiles++;
                    }
                    else
                    {
                        // Fail
                        Console.WriteLine($"  Error: File sizes do not match after copy.");
                        badFiles++;
                    }

                    // Generate a media info file so that we know that we already processed this item
                    GenerateMediaInfoFile(longDest, Path.GetDirectoryName(longDest) ?? String.Empty);  // Pass dest file and its dir
                    //GenerateMediaInfoXml(longDest, Path.GetDirectoryName(longDest) ?? String.Empty);
                }
                catch (Exception ex)
                {
                    // Error
                    Console.WriteLine($"  Error renaming file: {ex.Message}");
                    badFiles++;
                    continue;
                }

                // NEW: After copying, delete existing "-mediainfo.xml" if present
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
                            // Delete it
                            //File.Delete(existingMediaInfoPath);
                            MoveToRecycleBin(existingMediaInfoPath);
                            Console.WriteLine($"  Deleted existing: {Path.GetFileName(existingMediaInfoPath)}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  Warning: Could not delete existing mediainfo: {ex.Message}");
                        }
                    }
                }



                // Move the old folder to the Recycle Bin
                MoveToRecycleBin(oldFilePath);

                // Console
                Console.WriteLine(Environment.NewLine);
                Console.WriteLine("---");
                Console.WriteLine(Environment.NewLine);
            }

            // Done
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine("\nProcessing complete.");
            Console.WriteLine($"Total Files: {totalFiles}, Good Files: {goodFiles}, Bad Files: {badFiles}.");
            Console.WriteLine(Environment.NewLine);
            Console.WriteLine("Telling Plex to scan....");
            if (updPlex) UpdatePlex();
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        /// <summary>
        /// Utility method that recycles videos. To be called with matching sizes
        /// </summary>
        /// <param name="filePath">The path to the file</param>
        private static void doCleanUp(string filePath)
        {
            // Make sure it exists
            if (!File.Exists(filePath) && !Directory.Exists(filePath))
            {
                Console.WriteLine("  Error doing cleanup, path does not exist.");
                return;
            }

            // Recycle
            MoveToRecycleBin(filePath);
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
                throw new ArgumentException($"Path '{path}' does not exist.");
            }

            // Check to see if we are on Windows
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
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
                return;
            }

            // Shrink the source path
            if (Directory.Exists(path))
            {
                path = ShrinkLength(path, GetCleanName(path));
            }
            else if (File.Exists(path) && !path.EndsWith("-mediainfo.xml"))
            {
                path = ShrinkLength(path, GetCleanName(path));
            }
            else if (File.Exists(path))
            {
                path = path;
            }

            // Convert to short path for COM compatibility
            string shortPath = PathConverter.ToShortPath(path);
            bool isDirectory = Directory.Exists(path);

            if ((!isDirectory && !File.Exists(shortPath)) || (isDirectory && !Directory.Exists(shortPath)))
            {
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
                recycleBin.MoveHere(shortPath);
            }
            catch (Exception ex)
            {
                // Fallback: Try original long path
                try
                {
                    recycleBin.MoveHere(path);
                }
                catch
                {
                    Debug.WriteLine($"Fallback to delete on Windows: '{path}'.");
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
                    }
                    catch
                    {
                        // It really failed
                        throw new IOException($"Failed to move '{path}' to Recycle Bin (tried short: '{shortPath}'): {ex.Message}", ex);
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

                    // Check it
                    if (sourceSize == destSize)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Could not compare file sizes: {ex.Message}. Proceeding with copy.");
                }
            }

            // We made it this far, so we failed
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

            // Truncate cleanName to 20 chars
            string truncatedName = Path.GetFileName(cleanName).Length > truncLen
                                    ? Path.GetFileName(cleanName).Substring(0, truncLen)
                                    : Path.GetFileName(cleanName);
            string newFolderPath = PathConverter.NormalizePath(Path.Combine(sourcePath, randomNum.ToString("0000")/*, truncatedName*/));

            // Check length
            if (newFolderPath.Length >= 260)
            {
                Console.WriteLine($"  Warning: New folder path exceeds MAX_PATH limit. Recycle Bin will be skipped.");
                return path;
            }

            // Check if new folder name already exists
            if (Directory.Exists(newFolderPath))
            {
                Console.WriteLine($"  Warning: Target folder name '{Path.GetFileName(newFolderPath)}' already exists. Skipping folder rename.");
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
                        Directory.CreateDirectory(PathConverter.NormalizePath(destParent));
                    }

                    //Directory.Move(PathConverter.NormalizePath(Path.GetDirectoryName(path)), newFolderPath);
                    // Move it
                    Directory.Move(PathConverter.NormalizePath(Path.GetDirectoryName(path)
                                ?? path), newFolderPath);
                    Console.WriteLine($"  Renamed source folder: '{Path.GetFileName(path)}' -> '{Path.GetDirectoryName(newFolderPath)}'");

                    // Return the new folder path
                    return newFolderPath;
                }
                catch (Exception ex)
                {
                    // Error
                    Console.WriteLine($"  Error renaming source folder: {ex.Message}");
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

        // Dictionary to hold parsed args: key = name, value = string
        private static Dictionary<string, string> ParseNamedArgs(string[] args)
        {
            var namedArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Trim();
                if (arg.StartsWith("--") || arg.StartsWith('/'))
                {
                    string key = arg.Substring(2).Trim().ToLower();  // e.g., "--input" -> "input"
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--") && !args[i + 1].StartsWith('/'))
                    {
                        namedArgs[key] = args[++i].Trim();  // Next arg is value
                    }
                    else
                    {
                        namedArgs[key] = string.Empty;  // Flag without value (e.g., --verbose)
                    }
                }
                else if (!namedArgs.ContainsKey("positional"))  // Fallback for unnamed
                {
                    namedArgs["positional"] = arg;  // Handle first positional if needed
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
            // Trigger full scan (or use --section {id} for specific library)
            var scannerPath = @"C:\Program Files\Plex\Plex Media Server\Plex Media Scanner.exe";
            var startInfo = new ProcessStartInfo
            {
                FileName = scannerPath,
                Arguments = $"--scan --section {libraryID.ToString()}",  // 1 = Movies - JPC
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(startInfo);
            process.Start();  // Optional: Wait for completion
            Console.WriteLine("Plex scan triggered.");
        }

        /// <summary>
        /// Save a .json file containing the media info for the specified file, 
        /// saved to the specified folder
        /// </summary>
        /// <param name="videoPath">Path to source video</param>
        /// <param name="outputDir">Destination folder to save MediaInfo to</param>
        private static void GenerateMediaInfoFile(string videoPath, string outputDir)
        {
            // Set up paths
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);
            string mediaInfoPath = Path.Combine(outputDir, $"{fileNameWithoutExt}_media.json");

            // ffprobe command: Quiet mode, JSON format, show format/streams details
            string ffprobePath = "ffprobe";  // Assumes in PATH; else: @"C:\ffmpeg\bin\ffprobe.exe"
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
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Console.WriteLine($"  Warning: Could not start ffprobe for {fileNameWithoutExt}.");
                    return;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"  Warning: ffprobe error for {fileNameWithoutExt}: {error}");
                    return;
                }

                // Save JSON to file
                File.WriteAllText(mediaInfoPath, output, Encoding.UTF8);
                Console.WriteLine($"  Generated: {Path.GetFileName(mediaInfoPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error generating media info: {ex.Message}");
            }
        }

        private static void GenerateMediaInfoXml(string videoPath, string destSubdir)
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);
            string mediaInfoPath = Path.Combine(destSubdir, $"{fileNameWithoutExt}-mediainfo.xml");

            // MediaInfo command: XML output, full details
            string mediainfoExe = @"C:\Program Files\MediaInfo\MediaInfo.exe";  // Assume in PATH; else: @"C:\MediaInfo\MediaInfo.exe"
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
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Console.WriteLine($"  Warning: Could not start MediaInfo for {fileNameWithoutExt}.");
                    return;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"  Warning: MediaInfo error for {fileNameWithoutExt}: {error}");
                    return;
                }

                // Save raw XML
                File.WriteAllText(mediaInfoPath, output, Encoding.UTF8);
                Console.WriteLine($"  Generated: {Path.GetFileName(mediaInfoPath)} (TMM-style XML)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error generating -mediainfo.xml: {ex.Message}");
            }
        }
    }
}
