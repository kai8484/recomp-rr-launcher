using WheelWizard.Services;

namespace WheelWizard.Test.Features.Settings;

[Collection("SettingsFeature")]
public class PathManagerTests
{
    [Fact]
    public void DefaultWheelWizardAppdataFolderPath_PointsToDataFolder()
    {
        var path = PathManager.DefaultWheelWizardAppdataFolderPath;
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.EndsWith("data", path);
    }
}
