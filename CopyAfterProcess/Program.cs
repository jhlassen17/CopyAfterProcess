using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CopyAfterProcess
{
    class Program
    {
        static void Main(string[] args)
        {

            if (args.Length != 2 && !Debugger.IsAttached)
            {
                Console.WriteLine("Usage: MovieRenamer <source_folder> <destination_folder>");
                Console.WriteLine("Example: MovieRenamer \"C:\\Source\\Movies\" \"C:\\Dest\\Movies\"");
                return;
            }
            else if (Debugger.IsAttached)
            {
                // For debugging purposes, set default paths
                args = new string[]
                {
                    @"F:\jeff\Trans",
                    @"H:\jeff\files\Video\Movies"
                };
            }

            string sourcePath = LongPathHelper.NormalizePath(args[0]);
            string destPath = LongPathHelper.NormalizePath(args[1]);

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

            string[] sourceSubdirs = Directory.GetDirectories(sourcePath);

            foreach (string subdir in sourceSubdirs)
            {
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

                Console.WriteLine($"  Clean name: {cleanName}");

                // Normalize dest subdir path
                string destSubdir = LongPathHelper.NormalizePath(Path.Combine(destPath, cleanName)); // Combine first, then normalize

                if (!Directory.Exists(destSubdir))
                {
                    Console.WriteLine($"  Warning: Destination subfolder '{cleanName}' does not exist. Skipping.");
                    continue;
                }

                // Find the target file name in destination (assume first .mkv file; adjust extension filter if needed)
                string[] destMovieFiles = Directory.GetFiles(destSubdir, "*.mkv")
                                                  .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("-trailer", StringComparison.OrdinalIgnoreCase))
                                                  .ToArray();
                if (destMovieFiles.Length == 0)
                {
                    Console.WriteLine($"  Skipping: No .mkv file found in destination folder '{cleanName}'.");
                    continue;
                }

                if (destMovieFiles.Length > 1)
                {
                    Console.WriteLine($"  Warning: Multiple .mkv files found in destination. Using the first one.");
                }
                // Normalize target file path
                string targetFilePath = LongPathHelper.NormalizePath(destMovieFiles[0]);
                string newFileName = Path.GetFileName(targetFilePath);
                string newSourcePath = LongPathHelper.NormalizePath(Path.Combine(destPath, Path.GetFileName(destSubdir) ?? destSubdir, newFileName)); // Combine then normaliz

                // Find the source movie file matching "<fullFolderName> -1.<ext>"
                string[] movieFiles = Directory.GetFiles(subdir, $"{fullFolderName}-1.*")
                                              .Where(f => Path.GetExtension(f).Length > 0)
                                              .ToArray();

                if (movieFiles.Length == 0)
                {
                    Console.WriteLine($"  Skipping: No matching movie file found (expected '{fullFolderName}-1.<ext>').");
                    continue;
                }

                if (movieFiles.Length > 1)
                {
                    Console.WriteLine($"  Warning: Multiple matching files found in source. Processing the first one.");
                }

                string oldFilePath = movieFiles[0];

                try
                {
                    using (FileStream stream = File.Open(newSourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        // If we reach here, the file is not locked.
                        stream.Close();
                        Console.WriteLine($"File is not locked or in use.");
                    }
                }
                catch (IOException ex)
                {
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

                try
                {
                    var mover = new LongPathProgressFileMover();
                    mover.OnProgressChanged += (percentage, ref cancel) =>
                    {
                        Console.WriteLine($"Progress: {percentage:F1}%");
                    };
                    mover.OnComplete += (success, message) =>
                    {
                        Console.WriteLine(message);
                    };

                    // Example with a long path (adjust as needed)
                    string longSource = oldFilePath;
                    string longDest = newSourcePath;

                    if (LongPathHelper.IsLongPath(longSource))
                        Console.WriteLine("Detected long path - using \\?\\ prefix.");

                    mover.MoveFile(longSource, longDest);
                    //File.Move(oldFilePath, newSourcePath, true);
                    Console.WriteLine($"  Renamed: {Path.GetFileName(oldFilePath)} -> {newFileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error renaming file: {ex.Message}");
                    continue;
                }
                //}

                //string destFilePath = Path.Combine(destSubdir, newFileName);

                //// Copy to destination, overwriting
                //try
                //{
                //    File.Copy(newSourcePath, destFilePath, true);
                //    Console.WriteLine($"  Copied: {newFileName} to destination (overwritten if existed).");
                //}
                //catch (Exception ex)
                //{
                //    Console.WriteLine($"  Error copying file: {ex.Message}");
                //}

                // Move to recycle bin

                MoveToRecycleBin(oldFilePath);
            }

            Console.WriteLine("\nProcessing complete."); ;
        }

        public static void MoveToRecycleBin(string path)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Recycle Bin operations are only supported on Windows.");
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new ArgumentException($"Path '{path}' does not exist.");
            }

            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            dynamic shellApp = Activator.CreateInstance(shellType)!;

            // 0xa is the constant for the Recycle Bin folder (ShellSpecialFolderConstants.ssfRECYCLEBIN).
            var recycleBin = shellApp.Namespace(0xa);

            // Move the file/folder to the Recycle Bin.
            recycleBin.MoveHere(path);
        }
    }
}