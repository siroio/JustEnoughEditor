using UnityEditor;

namespace JustEnoughEditor
{
    public static class JEEPreferencesProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Just Enough Editor", SettingsScope.User)
            {
                label = "Just Enough Editor",
                guiHandler = _ => JEESettingsWindow.DrawSettingsContent()
            };
        }
    }
}
