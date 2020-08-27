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

            using var f2 = new FileStream(filepath, FileMode.OpenOrCreate);
            using var gz = new GZipStream(f2, CompressionMode.Compress, false);
            gz.Write(b, 0, b.Length);
        }

        public static void CompressFile(string sDir,
                                        string sRelativePath,
                                        GZipStream zipStream)
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
            IEnumerable<string> sFiles =
                Directory.GetFiles(sInDir, "*.*", SearchOption.AllDirectories);
            int iDirLen = sInDir[^1] == Path.DirectorySeparatorChar
                              ? sInDir.Length
                              : sInDir.Length + 1;

            using FileStream outFile =
                File.Open(sOutFile, FileMode.Create, FileAccess.Write);
            using var str = new GZipStream(outFile, CompressionMode.Compress);
            foreach (string sFilePath in sFiles)
            {
                string sRelativePath = sFilePath.Substring(iDirLen);
                CompressFile(sInDir, sRelativePath, str);
            }
        }

        public static bool DecompressFile(string sDir, GZipStream zipStream)
        {
            //Decompress file name
            var bytes = new byte[sizeof(int)];
            int read = zipStream.Read(bytes, 0, sizeof(int));
            if (read < sizeof(int))
            {
                return false;
            }

            int iNameLen = BitConverter.ToInt32(bytes, 0);
            if (iNameLen > 255)
            {
                throw new
                    Exception($"Failed to decompress \"{sDir}\" (file name length > 255). The file may be corrupted.");
            }

            bytes = new byte[sizeof(char)];
            var sb = new StringBuilder();
            for (int i = 0; i < iNameLen; i++)
            {
                zipStream.Read(bytes, 0, sizeof(char));
                char c = BitConverter.ToChar(bytes, 0);
                sb.Append(c);
            }

            string sFileName = sb.ToString();

            //Decompress file content
            bytes = new byte[sizeof(int)];
            zipStream.Read(bytes, 0, sizeof(int));
            int iFileLen = BitConverter.ToInt32(bytes, 0);

            bytes = new byte[iFileLen];
            zipStream.Read(bytes, 0, bytes.Length);

            string sFilePath = Path.Combine(sDir, sFileName);
            string sFinalDir = Path.GetDirectoryName(sFilePath);
            if (!Directory.Exists(sFinalDir))
            {
                Directory.CreateDirectory(sFinalDir);
            }

            const int maxRetries = 4;
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using FileStream outFile =
                        File.Open(sFilePath, FileMode.Create, FileAccess.Write);
                    outFile.Write(bytes, 0, iFileLen);
                    break;
                }
                catch (IOException e)
                {
                    if (i >= maxRetries || !File.Exists(sFilePath))
                    {
                        throw;
                    }

                    Console
                        .WriteLine($"Failed decompress file \"{sFilePath}\" {{{e.Message}}}, retrying in 250 ms...");
                    Thread.Sleep(250);
                }
            }

            return true;
        }

        public static void DecompressToDirectory(string sCompressedFile, string sDir)
        {
            Console.WriteLine($"Decompressing {sCompressedFile} to {sDir}...");
            const int maxRetries = 4;
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using FileStream inFile =
                        File.Open(sCompressedFile, FileMode.Open, FileAccess.Read);
                    using var zipStream =
                        new GZipStream(inFile, CompressionMode.Decompress, true);
                    while (DecompressFile(sDir, zipStream))
                    {
                    }

                    break;
                }
                catch (IOException e)
                {
                    if (i >= maxRetries || !File.Exists(sCompressedFile))
                    {
                        throw;
                    }

                    Console
                        .WriteLine($"Failed decompress file \"{sCompressedFile}\" {{{e.Message}}}, retrying in 250 ms...");
                    Thread.Sleep(250);
                }
            }
        }
    }
}
