using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CopyAfterProcess
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Utility class that helps convert normal and long paths 
    /// to a short path version for use with Win32 APIs
    /// </summary>
    public static class PathConverter
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string lpszLongPath, [Out] StringBuilder lpszShortPath, uint cchBuffer);

        /// <summary>
        /// Converts a long path (with or without prefix) to its 8.3 short path equivalent.
        /// </summary>
        /// <param name="longPath">The input path (e.g., "\\?\C:\Long\Path\file.txt").</param>
        /// <returns>The short path (e.g., "C:\LONG~1\PATH\FILE~1.TXT").</returns>
        public static string ToShortPath(string longPath)
        {
            if (string.IsNullOrEmpty(longPath))
                return longPath;

            // Strip long path prefix first (MoveHere doesn't like it)
            string normalized = PathConverter.NormalizePath(longPath);  // Ensure consistent prefix
            string unpinned = normalized;
            if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                unpinned = normalized.Substring(4);  // Remove "\\?\" (4 chars)
            }
            else if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                unpinned = @"\\" + normalized.Substring(8);  // Remove "\\?\UNC\" -> "\\"
            }

            // Allocate buffer (MAX_PATH +1)
            StringBuilder shortPath = new StringBuilder(261);
            uint result = GetShortPathName(unpinned, shortPath, 261);

            if (result == 0)
            {
                // Fallback: Return original unpinned (or prefixed if needed)
                Marshal.GetLastWin32Error();  // Log error if desired
                return unpinned;  // Or throw: throw new IOException("Failed to get short path.");
            }

            return shortPath.ToString();
        }

        /// <summary>
        /// Takes a path and normalizes it into a supported 
        /// Long Path
        /// </summary>
        /// <param name="path">Path to file or folder</param>
        /// <returns>A converted long path</returns>
        public static string ToLongPath(string path)
        {
            // Check base case
            if (string.IsNullOrEmpty(path))
                return path;

            // Remove accidental whitespace
            path = path.Trim();

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

        /// <summary>
        /// Takes a path and normalizes it into a supported 
        /// Long Path
        /// </summary>
        /// <param name="path">Path to file or folder</param>
        /// <returns>A converted long path</returns>
        public static string NormalizePath(string path)
        {
            return ToLongPath(path);
        }

        /// <summary>
        /// Utility method that checks to see if the specified 
        /// path is a long path
        /// </summary>
        /// <param name="path">A path to a file or folder</param>
        /// <returns>True if it is a long path</returns>
        public static bool IsLongPath(string path) => !string.IsNullOrEmpty(path?.Trim()) && path.Length >= 260;

    }
}
