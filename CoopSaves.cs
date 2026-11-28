using System;
using System.IO;
using System.Linq;

namespace DeadCellsMultiplayerMod;

internal static class CoopSaves
{
    internal static string? BackUpSlot(string slotPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(slotPath))!;
        var slot = Path.GetFileNameWithoutExtension(slotPath);
        var files = Directory.EnumerateFiles(directory, slot + ".*").ToArray();
        if (files.Length == 0)
            return null;

        var backup = Path.Combine(directory, "backups", slot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        foreach (var file in files)
            File.Copy(file, Path.Combine(backup, Path.GetFileName(file)));
        return backup;
    }
}
