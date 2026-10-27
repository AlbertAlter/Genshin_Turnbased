using System.IO;
using OfficeOpenXml;

public partial class DataManager
{
    public TeamBonusRules TeamBonusRules = new TeamBonusRules();

    private void LoadTeamBonus()
    {
        using var stream = new FileStream(Path.Combine(ChartsPath, "TeamBonus.xlsx"), FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        TeamBonusRules = TeamBonusTableLoader.Load(package.Workbook);
    }
}
