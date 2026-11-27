#if BAVARIAN_USB_BASELINE
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using Android.Content;
using EdiabasLib;

namespace BmwDeepObd
{
    internal static class BavarianEcuSeed
    {
        private const long MaxFileBytes = 32L * 1024 * 1024;
        private const long MaxTotalBytes = 512L * 1024 * 1024;
        private static readonly object Sync = new object();
        private static readonly HashSet<string> Approved = new HashSet<string>(StringComparer.Ordinal)
        {
            "d50m57c0.prg", "d50m57d0.prg", "d50m57e0.prg",
            "d60m57a0.prg", "d60m57d0.prg", "d62m57a0.prg", "d62m57b0.prg",
            "d_egs.grp", "d_motor.grp", "d_motor2.grp",
            "gs19.prg", "gs19a.prg", "gs19b.prg", "gs19d.prg"
        };

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
                        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
                        string root = Path.Combine(context.FilesDir.AbsolutePath, "local-ecu");
                        Directory.CreateDirectory(root);
                        int added = 0;
                        int present = 0;
                        long total = 0;
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var entry in archive.Entries)
                        {
                            string name = entry.FullName.ToLowerInvariant();
                            if (!Approved.Contains(name) || !seen.Add(name) || entry.Length < 20 || entry.Length > MaxFileBytes)
                                throw new IOException("Invalid bundled ECU entry.");
                            total += entry.Length;
                            if (total > MaxTotalBytes) throw new IOException("Bundled library too large.");
                            string target = Path.Combine(root, name);
                            if (File.Exists(target)) { present++; continue; }
                            string stage = Path.Combine(root, Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(stage);
                            try
                            {
                                string staged = Path.Combine(stage, name);
                                using (var input = entry.Open())
                                using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write))
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
                                using (var ediabas = new EdiabasNet())
                                {
                                    ediabas.SetConfigProperty("EcuPath", stage);
                                    uint type = ediabas.GetFileType(staged);
                                    if ((type == 0) != name.EndsWith(".grp", StringComparison.Ordinal))
                                        throw new IOException("ECU header mismatch.");
                                }
                                File.Move(staged, target);
                                added++;
                            }
                            finally { Directory.Delete(stage, true); }
                        }
                        if (seen.Count != Approved.Count) return "Bundled ECU library incomplete.";
                        return "Bundled ECU files available: " + (added + present) + " / " + Approved.Count + " (offline).";
                    }
                }
                catch (Exception ex) { return BavarianLanguage.Error("Bundled ECU import failed", ex); }
            }
        }
    }
}
#endif
