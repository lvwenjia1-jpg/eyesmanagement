namespace OrderTextTrainer.Core.Services;

public static class RuntimeDataPathHelper
{
    public const string DataDirectoryName = "SmartRecognitionData";

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
}
