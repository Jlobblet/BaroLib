﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        public enum SaveType
        {
            Singleplayer,
            Multiplayer,
        }

        public static string MultiplayerSaveFolder =
            Path.Combine(SaveFolder, "Multiplayer");

        public static readonly string SubmarineDownloadFolder = Path.Combine("Submarines", "Downloaded");
        public static readonly string CampaignDownloadFolder = Path.Combine("Data", "Saves", "Multiplayer_Downloaded");

        private static readonly string OsxSaveFolder =
            Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                         "Library",
                         "Application Support",
                         "Daedalic Entertainment GmbH",
                         "Barotrauma");

        private static readonly string LinuxWindowsSaveFolder =
            Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Daedalic Entertainment GmbH",
                         "Barotrauma");

        public static string SaveFolder
            => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                   ? OsxSaveFolder
                   : LinuxWindowsSaveFolder;

        public static string TempPath
        {
#if SERVER
            get { return Path.Combine(SaveFolder, "temp_server"); }
#else
            get { return Path.Combine(SaveFolder, "temp"); }
#endif
        }

        public static XDocument LoadGameSessionDoc(string filePath)
        {
            DecompressToDirectory(filePath, TempPath);

            return XDocument.Load(Path.Combine(TempPath, "gamesession.xml"));
        }

        public static bool IsSaveFileCompatible(XDocument saveDoc) => saveDoc?.Root?.Attribute("version") != null;

        public static string GetSavePath(SaveType saveType, string saveName)
        {
            string folder = saveType == SaveType.Singleplayer ? SaveFolder : MultiplayerSaveFolder;
            return Path.Combine(folder, saveName);
        }

        public static void CompressStringToFile(string fileName, string value)
        {
            // A.
            // Convert the string to its byte representation.
            byte[] b = Encoding.UTF8.GetBytes(value);

            // B.
            // Use GZipStream to write compressed bytes to target file.
            using (FileStream f2 = File.Open(fileName, FileMode.Create))
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
            IEnumerable<string> sFiles  = Directory.GetFiles(sInDir, "*.*", SearchOption.AllDirectories);
            int                 iDirLen = sInDir[^1] == Path.DirectorySeparatorChar ? sInDir.Length : sInDir.Length + 1;

            using FileStream outFile = File.Open(sOutFile, FileMode.Create, FileAccess.Write);
            using var        str     = new GZipStream(outFile, CompressionMode.Compress);
            foreach (string sFilePath in sFiles)
            {
                string sRelativePath = sFilePath[iDirLen..];
                CompressFile(sInDir, sRelativePath, str);
            }
        }


        public static Stream DecompressFileToStream(string fileName)
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
            var bytes     = new byte[sizeof(int)];
            int bytesRead = Read(zipStream, bytes, sizeof(int));
            if (bytesRead < sizeof(int))
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
                Read(zipStream, bytes, sizeof(char));
                var c = BitConverter.ToChar(bytes, 0);
                sb.Append(c);
            }

            string sFileName = sb.ToString().Replace('\\', '/');

            fileName = sFileName;

            //Decompress file content
            bytes = new byte[sizeof(int)];
            Read(zipStream, bytes, sizeof(int));
            var iFileLen = BitConverter.ToInt32(bytes, 0);

            bytes = new byte[iFileLen];
            Read(zipStream, bytes, bytes.Length);

            string sFilePath = Path.Combine(sDir, sFileName);
            string sFinalDir = Path.GetDirectoryName(sFilePath);

            string sDirFull = (string.IsNullOrEmpty(sDir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(sDir))
                .CleanUpPathCrossPlatform(false);
            string sFinalDirFull =
                (string.IsNullOrEmpty(sFinalDir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(sFinalDir))
                .CleanUpPathCrossPlatform(false);

            if (!sFinalDirFull.StartsWith(sDirFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                                                    $"Error extracting \"{sFileName}\": cannot be extracted to parent directory");
            }

            if (!writeFile)
            {
                return true;
            }

            if (!Directory.Exists(sFinalDir))
            {
                Directory.CreateDirectory(sFinalDir ?? throw new InvalidOperationException());
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

        private static int Read(GZipStream zipStream, byte[] bytes, int amount)
        {
            var read = 0;

            // FIXME workaround for .NET6 causing save decompression to fail
#if NET6_0
            for (int i = 0; i < amount; i++)
            {
                int result = zipStream.ReadByte();
                if (result < 0) { break; }

                bytes[i] = (byte) result;
                read++;
            }
#else
            read = zipStream.Read(bytes, 0, amount);
#endif
            return read;
        }

        public static void DecompressToDirectory(string sCompressedFile, string sDir)
        {
            const int maxRetries = 4;
            for (var i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using FileStream inFile    = File.Open(sCompressedFile, FileMode.Open, FileAccess.Read);
                    using var        zipStream = new GZipStream(inFile, CompressionMode.Decompress, true);
                    while (DecompressFile(true, sDir, zipStream, out _))
                    {
                    }

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
                    using FileStream inFile    = File.Open(sCompressedFile, FileMode.Open, FileAccess.Read);
                    using var        zipStream = new GZipStream(inFile, CompressionMode.Decompress, true);
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

        public static void CopyFolder(string sourceDirName, string destDirName, bool copySubDirs,
                                      bool   overwriteExisting = false)
        {
            // Get the subdirectories for the specified directory.
            var dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                                                     "Source directory does not exist or could not be found: "
                                                     + sourceDirName);
            }

            IEnumerable<DirectoryInfo> dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            IEnumerable<FileInfo> files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                if (!overwriteExisting && File.Exists(tempPath))
                {
                    continue;
                }

                file.CopyTo(tempPath, true);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (!copySubDirs)
            {
                return;
            }

            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    CopyFolder(subdir.FullName, tempPath, true, overwriteExisting);
                }
            }
        }

        public static void DeleteDownloadedSubs()
        {
            if (Directory.Exists(SubmarineDownloadFolder))
            {
                ClearFolder(SubmarineDownloadFolder);
            }
        }

        public static void CleanUnnecessarySaveFiles()
        {
            if (Directory.Exists(CampaignDownloadFolder))
            {
                ClearFolder(CampaignDownloadFolder);
                Directory.Delete(CampaignDownloadFolder);
            }

            if (Directory.Exists(TempPath))
            {
                ClearFolder(TempPath);
                Directory.Delete(TempPath);
            }
        }

        public static void ClearFolder(string folderName, string[] ignoredFileNames = null)
        {
            var dir = new DirectoryInfo(folderName);

            foreach (FileInfo fi in dir.GetFiles())
            {
                if (ignoredFileNames != null)
                {
                    bool ignore = ignoredFileNames.Any(ignoredFile =>
                                                           Path.GetFileName(fi.FullName)
                                                               .Equals(Path.GetFileName(ignoredFile)));

                    if (ignore)
                    {
                        continue;
                    }
                }

                fi.IsReadOnly = false;
                fi.Delete();
            }

            foreach (DirectoryInfo di in dir.GetDirectories())
            {
                ClearFolder(di.FullName, ignoredFileNames);
                var maxRetries = 4;
                for (var i = 0; i <= maxRetries; i++)
                {
                    try
                    {
                        di.Delete();
                        break;
                    }
                    catch (IOException)
                    {
                        if (i >= maxRetries)
                        {
                            throw;
                        }

                        Thread.Sleep(250);
                    }
                }
            }
        }
    }
}
