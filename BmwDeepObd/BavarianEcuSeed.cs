#if BAVARIAN_USB_BASELINE
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using Android.Content;

namespace BmwDeepObd
{
    internal static class BavarianEcuSeed
    {
        private const long MaxFileBytes = 32L * 1024 * 1024;
        private const long MaxTotalBytes = 512L * 1024 * 1024;
        private const int ExpectedFiles = 675, ExpectedPrg = 259, ExpectedGrp = 416;
        private const string ExpectedSha256 = "01353bea4400aedf508ee3eb2822e70ddab0ccbb41b27d139747c8cb42ad6611";
        private static readonly object Sync = new object();
        private static readonly Regex SafeName = new Regex(@"^[a-z0-9][a-z0-9_.-]{0,100}\.(prg|grp)$", RegexOptions.CultureInvariant);

        public static string Initialize(Context context)
        {
            lock (Sync)
            {
                Stream asset;
                try { asset = context.Assets.Open("bavarian_ecu.zip"); }
                catch (Exception) { return "Bundled local ECU library: not included."; }
                try
                {
                    using (asset)
                    using (var buffer = new MemoryStream())
                    {
                        byte[] chunk = new byte[65536];
                        int n;
                        while ((n = asset.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            if (buffer.Length + n > MaxTotalBytes) throw new IOException("Bundled archive too large.");
                            buffer.Write(chunk, 0, n);
                        }
                        buffer.Position = 0;
                        if (!string.Equals(Convert.ToHexString(SHA256.HashData(buffer)), ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Bundled archive hash mismatch.");
                        buffer.Position = 0;
                        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
                        if (archive.Entries.Count != ExpectedFiles) throw new IOException("Bundled file count mismatch.");
                        string root = Path.Combine(context.FilesDir.AbsolutePath, "local-ecu");
                        Directory.CreateDirectory(root);
                        int added = 0, present = 0, conflicts = 0, prg = 0, grp = 0;
                        long total = 0;
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var entry in archive.Entries)
                        {
                            string name = entry.FullName;
                            if (!SafeName.IsMatch(name) || !seen.Add(name) || entry.Length < 20 || entry.Length > MaxFileBytes)
                                throw new IOException("Invalid bundled ECU entry.");
                            bool isGroup = name.EndsWith(".grp", StringComparison.Ordinal);
                            if (isGroup) grp++; else prg++;
                            total += entry.Length;
                            if (total > MaxTotalBytes) throw new IOException("Bundled library too large.");
                            string target = Path.Combine(root, name);
                            if (File.Exists(target))
                            {
                                using var existing = File.OpenRead(target);
                                using var expected = entry.Open();
                                if (existing.Length == entry.Length && CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing), SHA256.HashData(expected)))
                                    present++;
                                else conflicts++;
                                continue;
                            }
                            string stage = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
                            try
                            {
                                using (var input = entry.Open())
                                using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write))
                                {
                                    long length = 0;
                                    while ((n = input.Read(chunk, 0, chunk.Length)) > 0)
                                    {
                                        length += n;
                                        if (length > entry.Length || length > MaxFileBytes) throw new IOException("Invalid entry size.");
                                        output.Write(chunk, 0, n);
                                    }
                                    if (length != entry.Length) throw new IOException("Incomplete bundled file.");
                                }
                                using (var input = File.OpenRead(stage))
                                {
                                    byte[] header = new byte[20];
                                    input.ReadExactly(header);
                                    if ((BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16)) == 0) != isGroup)
                                        throw new IOException("ECU header mismatch.");
                                }
                                File.Move(stage, target);
                                added++;
                            }
                            finally { if (File.Exists(stage)) File.Delete(stage); }
                        }
                        if (prg != ExpectedPrg || grp != ExpectedGrp) throw new IOException("Bundled file type count mismatch.");
                        return "E60 V74 ECU library: " + (added + present) + " / " + ExpectedFiles +
                            " verified (offline). PRG: " + prg + ", GRP: " + grp +
                            (conflicts > 0 ? ". Existing conflicting files preserved: " + conflicts : ".");
                    }
                }
                catch (Exception ex) { return BavarianLanguage.Error("Bundled ECU import failed", ex); }
            }
        }
    }
}
#endif
