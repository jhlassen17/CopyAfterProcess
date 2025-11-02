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
            string normalized = LongPathHelper.NormalizePath(longPath);  // Ensure consistent prefix
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
    }
}
