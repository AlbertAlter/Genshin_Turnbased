using System.IO;
using System.Text;

public interface IRoguelikeSaveFileSystem
{
    bool FileExists(string path);
    void CreateDirectory(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    void MoveFile(string sourcePath, string destinationPath);
    void ReplaceFile(string sourcePath, string destinationPath, string backupPath);
    void DeleteFile(string path);
}

public sealed class SystemRoguelikeSaveFileSystem : IRoguelikeSaveFileSystem
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

    public bool FileExists(string path) { return File.Exists(path); }
    public void CreateDirectory(string path) { Directory.CreateDirectory(path); }
    public string ReadAllText(string path) { return File.ReadAllText(path, Encoding.UTF8); }
    public void WriteAllText(string path, string contents) { File.WriteAllText(path, contents, Utf8WithoutBom); }
    public void MoveFile(string sourcePath, string destinationPath) { File.Move(sourcePath, destinationPath); }
    public void ReplaceFile(string sourcePath, string destinationPath, string backupPath)
    {
        File.Replace(sourcePath, destinationPath, backupPath, true);
    }
    public void DeleteFile(string path) { File.Delete(path); }
}
