using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CopyAfterProcess
{
    public class HashComparer
    {
        public static bool CompareFileHashes(string filePath1, string filePath2)
        {
            if (String.IsNullOrWhiteSpace(filePath1) || String.IsNullOrWhiteSpace(filePath2)) return false;
            if (String.Equals(filePath1, filePath2, StringComparison.OrdinalIgnoreCase)) return true;
            if (!Path.Exists(filePath1) || !Path.Exists(filePath2)) return false;

            string hash1 = ComputeSha256(filePath1);
            string hash2 = ComputeSha256(filePath2);

            bool match = string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);

            //Console.WriteLine($"Hash 1: {hash1}");
            //Console.WriteLine($"Hash 2: {hash2}");
            //Console.WriteLine($"Match: {match}");

            return match;
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
