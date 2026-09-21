using System.IO;
using OfficeOpenXml;

public partial class DataManager
{
    private void LoadEnemyShieldRules()
    {
        string path = Path.Combine(ChartsPath, "EnemyShield.xlsx");
        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        ExcelWorksheet sheet = package.Workbook.Worksheets["Type1"];
        EnemyShieldRules = EnemyShieldRuleTableLoader.Load(sheet);
    }
}
