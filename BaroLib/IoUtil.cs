using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

namespace BaroLib
{
    public static class IoUtil
    {
        public static string MultiplayerSaveFolder =
            Path.Combine(SaveFolder, "Multiplayer");

        public static string SaveFolder
            => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                   ? Path.Combine(
                                  Environment.GetFolderPath(Environment.SpecialFolder
                                                                       .Personal),
                                  "Library",
                                  "Application Support",
                                  "Daedalic Entertainment GmbH",
                                  "Barotrauma")
                   : Path.Combine(
                                  Environment.GetFolderPath(Environment.SpecialFolder
                                                                       .LocalApplicationData),
                                  "Daedalic Entertainment GmbH",
                                  "Barotrauma");

        public static XDocument LoadSub(string filepath)
        {
            using var originalFileStream =
                new FileStream(filepath, FileMode.Open);
            using var decompressionStream =
                new GZipStream(originalFileStream, CompressionMode.Decompress);
            return XDocument.Load(decompressionStream);
        }

        public static void SaveSub(this XDocument sub, string filepath)
        {
            string temp = Path.GetTempFileName();
            File.WriteAllText(temp, sub.ToString());
            byte[] b;

            using (var f = new FileStream(temp, FileMode.Open))
            {
                b = new byte[f.Length];
                f.Read(b, 0, (int)f.Length);
            }

            using (var f2 = new FileStream(filepath, FileMode.OpenOrCreate))
            using (var gz = new GZipStream(f2, CompressionMode.Compress, false))
            {
                gz.Write(b, 0, b.Length);
            }
        }

        public static void CompressFile(string sDir, string sRelativePath, GZipStream zipStream)
        {
            //Compress file name
            char[] chars = sRelativePath.ToCharArray();
            zipStream.Write(BitConverter.GetBytes(chars.Length), 0, sizeof(int));
            foreach (char c in chars)
            {
                zipStream.Write(BitConverter.GetBytes(c), 0, sizeof(char));
            }

            //Compress file content
            byte[] bytes = File.ReadAllBytes(Path.Combine(sDir, sRelativePath));
            zipStream.Write(BitConverter.GetBytes(bytes.Length), 0, sizeof(int));
            zipStream.Write(bytes, 0, bytes.Length);
        }

        public static void CompressDirectory(string sInDir, string sOutFile)
        {
            IEnumerable<string> sFiles = Directory.GetFiles(sInDir, "*.*", SearchOption.AllDirectories);
            int iDirLen = sInDir[sInDir.Length - 1] == Path.DirectorySeparatorChar ? sInDir.Length : sInDir.Length + 1;

            using FileStream outFile = File.Open(sOutFile, FileMode.Create, FileAccess.Write);
            using var        str     = new GZipStream(outFile, CompressionMode.Compress);
            foreach (string sFilePath in sFiles)
            {
                string sRelativePath = sFilePath.Substring(iDirLen);
                CompressFile(sInDir, sRelativePath, str);
            }
        }

        public static Stream DecompressFiletoStream(string fileName)
        {
            using FileStream originalFileStream     = File.Open(fileName, FileMode.Open);
            var              decompressedFileStream = new MemoryStream();

            using var decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress);
            decompressionStream.CopyTo(decompressedFileStream);
            return decompressedFileStream;
        }

        private static bool DecompressFile(bool writeFile, string sDir, GZipStream zipStream, out string fileName)
        {
            fileName = null;

            //Decompress file name
            var bytes  = new byte[sizeof(int)];
            int readed = zipStream.Read(bytes, 0, sizeof(int));
            if (readed < sizeof(int))
            {
                return false;
            }

            var iNameLen = BitConverter.ToInt32(bytes, 0);
            if (iNameLen > 255)
            {
                throw new
                    Exception($"Failed to decompress \"{sDir}\" (file name length > 255). The file may be corrupted.");
            }

            bytes = new byte[sizeof(char)];
            var sb = new StringBuilder();
            for (var i = 0; i < iNameLen; i++)
            {
                zipStream.Read(bytes, 0, sizeof(char));
                var c = BitConverter.ToChar(bytes, 0);
                sb.Append(c);
            }

            var sFileName = sb.ToString();

            fileName = sFileName;

            //Decompress file content
            bytes = new byte[sizeof(int)];
            zipStream.Read(bytes, 0, sizeof(int));
            var iFileLen = BitConverter.ToInt32(bytes, 0);

            bytes = new byte[iFileLen];
            zipStream.Read(bytes, 0, bytes.Length);

            string sFilePath = Path.Combine(sDir, sFileName);
            string sFinalDir = Path.GetDirectoryName(sFilePath);

            string sDirFull =
                (string.IsNullOrEmpty(sDir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(sDir))
                .CleanUpPathCrossPlatform(false);
            string sFinalDirFull =
                (string.IsNullOrEmpty(sFinalDir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(sFinalDir))
                .CleanUpPathCrossPlatform(false);

            if (!sFinalDirFull.StartsWith(sDirFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new
                    InvalidOperationException($"Error extracting \"{sFileName}\": cannot be extracted to parent directory");
            }

            if (!writeFile)
            {
                return true;
            }

            if (!Directory.Exists(sFinalDir))
            {
                Directory.CreateDirectory(sFinalDir);
            }

            var maxRetries = 4;
            for (var i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using FileStream outFile = File.Open(sFilePath, FileMode.Create, FileAccess.Write);
                    outFile.Write(bytes, 0, iFileLen);

                    break;
                }
                catch (IOException)
                {
                    if (i >= maxRetries || !File.Exists(sFilePath))
                    {
                        throw;
                    }

                    Thread.Sleep(250);
                }
            }

            return true;
        }

        public static void DecompressToDirectory(string sCompressedFile, string sDir)
        {
            var maxRetries = 4;
            for (var i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using FileStream inFile    = File.Open(sCompressedFile, FileMode.Open, FileAccess.Read);
                    using var        zipStream = new GZipStream(inFile, CompressionMode.Decompress, true);
                    while (DecompressFile(true, sDir, zipStream, out _))
                    {
                    }

                    ;

                    break;
                }
                catch (IOException)
                {
                    if (i >= maxRetries || !File.Exists(sCompressedFile))
                    {
                        throw;
                    }

                    Thread.Sleep(250);
                }
            }
        }

        public static IEnumerable<string> EnumerateContainedFiles(string sCompressedFile)
        {
            var maxRetries = 4;
            var paths      = new HashSet<string>();
            for (var i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using FileStream inFile =
                        File.Open(sCompressedFile, FileMode.Open, FileAccess.Read);
                    using var zipStream = new GZipStream(inFile, CompressionMode.Decompress, true);
                    while (DecompressFile(false, "", zipStream, out string fileName))
                    {
                        paths.Add(fileName);
                    }
                }
                catch (IOException)
                {
                    if (i >= maxRetries || !File.Exists(sCompressedFile))
                    {
                        throw;
                    }

                    Thread.Sleep(250);
                }
            }

            return paths;
        }
    }
}
