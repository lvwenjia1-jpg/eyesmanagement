namespace OrderTextTrainer.Core.Services;

public static class RuntimeDataPathHelper
{
    public const string DataDirectoryName = "SmartRecognitionData";
    public const int DefaultBackupRetentionCount = 3;

    public static string GetDataDirectoryPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, DataDirectoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string GetDataFilePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty.", nameof(fileName));
        }

        var fullPath = Path.Combine(GetDataDirectoryPath(), fileName);
        TryMigrateLegacyFile(fileName, fullPath);
        return fullPath;
    }

    private static void TryMigrateLegacyFile(string fileName, string fullPath)
    {
        if (File.Exists(fullPath))
        {
            return;
        }

        var legacyPath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            File.Copy(legacyPath, fullPath, overwrite: false);
        }
        catch
        {
        }
    }

    public static bool WriteAllTextWithBackup(string fullPath, string content, int maxBackupCount = DefaultBackupRetentionCount)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(fullPath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (File.Exists(fullPath))
        {
            var existingContent = File.ReadAllText(fullPath);
            if (string.Equals(existingContent, content, StringComparison.Ordinal))
            {
                PruneBackupFiles(fullPath, maxBackupCount);
                return false;
            }

            BackupExistingFile(fullPath, maxBackupCount);
        }

        File.WriteAllText(fullPath, content);
        return true;
    }

    private static void BackupExistingFile(string fullPath, int maxBackupCount)
    {
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > 0)
        {
            var backupPath = $"{fullPath}.bak-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(fullPath, backupPath, overwrite: false);
        }

        PruneBackupFiles(fullPath, maxBackupCount);
    }

    private static void PruneBackupFiles(string fullPath, int maxBackupCount)
    {
        var fileInfo = new FileInfo(fullPath);
        var directory = fileInfo.Directory;
        if (directory is null)
        {
            return;
        }

        foreach (var staleBackup in directory
                     .GetFiles($"{fileInfo.Name}.bak-*")
                     .OrderByDescending(item => item.LastWriteTimeUtc)
                     .Skip(Math.Max(0, maxBackupCount)))
        {
            try
            {
                staleBackup.Delete();
            }
            catch
            {
            }
        }
    }
}
