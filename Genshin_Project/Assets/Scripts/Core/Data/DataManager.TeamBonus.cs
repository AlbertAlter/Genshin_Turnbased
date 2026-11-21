using System.IO;
using OfficeOpenXml;

public partial class DataManager
{
    public TeamBonusRules TeamBonusRules = new TeamBonusRules();

    private void LoadTeamBonus()
    {
        string path = Path.Combine(ChartsPath, "TeamBonus.xlsx");
        using var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = OpenDataWorkbook(path, stream: stream);
        TeamBonusRules = TeamBonusTableLoader.Load(package.Workbook);
    }
}
