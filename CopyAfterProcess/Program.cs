using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CopyAfterProcess
{
    /// <summary>
    /// Copy After Process allows you to take transcoded files in a temporary 
    /// directory to a Movie Library Folder
    /// </summary>
    class Program
    {
        /// <summary>
        /// Random Number Generator
        /// </summary>
        private static Random random = new Random();  // For generating random 4-digit number

        /// <summary>
        /// Main entry point
        /// </summary>
        /// <param name="args">Arguments.  Usage: MovieRenamer 
        /// [source_folder] [destination_folder]
        /// </param>
        static void Main(string[] args)
        {
            ///*RecycleBinHelper.*/MoveToRecycleBin(@"F:\jeff\Trans\Jurassic Park (1993) {Tmdb-329} - [Remux-2160P Proper][Dts-Hd Ma 5.1][Dv Hdr10][Hevc]-Cinephiles");

            //MoveToRecycleBin(@"F:\jeff\Trans\Jurassic Park (1993) {Tmdb-329} - [Remux-2160P Proper][Dts-Hd Ma 5.1][Dv Hdr10][Hevc]-Cinephiles\Jurassic Park (1993) {Tmdb-329} - [Remux-2160P Proper][Dts-Hd Ma 5.1][Dv Hdr10][Hevc]-Cinephiles-1\Jurassic Park (1993) {Tmdb-329} - [Re\New Text Document.txt");

            //return;

            // Set up counters
            int totalFiles = 0;
            int goodFiles = 0;
            int badFiles = 0;

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
            }

            // Set up paths and normalize them
            string sourcePath = LongPathHelper.NormalizePath(args[0]);
            string destPath = LongPathHelper.NormalizePath(args[1]);

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
                string normalizedSubdir = LongPathHelper.NormalizePath(subdir);
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
                string destSubdir = LongPathHelper.NormalizePath(Path.Combine(destPath, cleanName)); // Combine first, then normalize

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
                        Console.WriteLine($" Multi-File: \"{curDir}\"");
                    }

                    badFiles++;
                    continue;
                }

                // Normalize target file path
                string targetFilePath = LongPathHelper.NormalizePath(destMovieFiles[0]);
                string newFileName = Path.GetFileName(targetFilePath);
                string newSourcePath = LongPathHelper.NormalizePath(Path.Combine(destPath, Path.GetFileName(destSubdir) ?? destSubdir, newFileName)); // Combine then normaliz

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
                        Console.WriteLine($" File is not locked or in use.");
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
                    mover.OnProgressChanged += (progress, ref cancel) =>
                    {
                        Debug.WriteLine($" Progress: {progress}");
                    };

                    mover.OnComplete += (success, message) =>
                    {
                        Console.WriteLine(message);
                    };

                    // Set up long paths
                    string longSource = oldFilePath;
                    string longDest = newSourcePath;

                    // Sanity check
                    if (LongPathHelper.IsLongPath(longSource))
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
                }
                catch (Exception ex)
                {
                    // Error
                    Console.WriteLine($"  Error renaming file: {ex.Message}");
                    badFiles++;
                    continue;
                }

                // Move the old folder to the Recycle Bin
                MoveToRecycleBin(Path.GetDirectoryName(oldFilePath));
            }

            // Done
            Console.WriteLine("\nProcessing complete.");
            Console.WriteLine($"Total Files: {totalFiles}, Good Files: {goodFiles}, Bad Files: {badFiles}.");
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
            
            // Truncate cleanName to 20 chars
            string truncatedName = cleanName.Length > 20 ? cleanName.Substring(0, 20) : cleanName;
            //string newFolderName = $"{randomNum} {truncatedName}";
            string newFolderPath = LongPathHelper.NormalizePath(Path.Combine(path, randomNum.ToString(), truncatedName, cleanName));

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
                    // Move it
                    Directory.Move(LongPathHelper.NormalizePath(Path.GetDirectoryName(path)), newFolderPath);
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

            // Set up shell object
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            dynamic shellApp = Activator.CreateInstance(shellType)!;

            // 0xa is the constant for the Recycle Bin folder (ShellSpecialFolderConstants.ssfRECYCLEBIN).
            var recycleBin = shellApp.Namespace(0xa);

            // Shrink the source path
            path = ShrinkLength(path, GetCleanName(path));

            // Move the file/folder to the Recycle Bin.
            recycleBin.MoveHere(path);
        }
    }
}
