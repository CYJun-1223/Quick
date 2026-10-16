namespace Quick;

internal readonly record struct ShortcutPreference(Keys Modifiers, Keys Key);

internal static class QuickSettingsStore
{
    private const string SettingsRegistryPath = "Software\\Quick";
    private const string StartupRegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string StartupValueName = "Quick";
    private const string AutoUpdateValueName = "AutoUpdateEnabled";
    private const string ShortcutKeyValueName = "Key";
    private const string ShortcutModifiersValueName = "Modifiers";

    internal static ShortcutPreference LoadShortcut(Keys defaultModifiers, Keys defaultKey)
    {
        var modifiers = defaultModifiers;
        var key = defaultKey;

        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
            if (settings?.GetValue(ShortcutKeyValueName) is int keyValue && keyValue > 0 && keyValue <= 0xFFFF)
            {
                key = (Keys)keyValue;
            }

            if (settings?.GetValue(ShortcutModifiersValueName) is int modifierValue)
            {
                var configuredModifiers = (Keys)modifierValue & (Keys.Alt | Keys.Control | Keys.Shift);
                if (configuredModifiers != Keys.None)
                {
                    modifiers = configuredModifiers;
                }
            }
        }
        catch
        {
        }

        return new ShortcutPreference(modifiers, key);
    }

    internal static bool TrySaveShortcut(Keys modifiers, Keys key, out string error)
    {
        error = string.Empty;
        try
        {
            using var settings = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath);
            if (settings is null)
            {
                error = "Could not save the shortcut setting.";
                return false;
            }

            settings.SetValue(ShortcutKeyValueName, (int)key);
            settings.SetValue(ShortcutModifiersValueName, (int)modifiers);
            return true;
        }
        catch
        {
            error = "Could not save the shortcut setting.";
            return false;
        }
    }

    internal static bool IsLaunchAtStartupEnabled(string executablePath)
    {
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(StartupRegistryPath);
            return settings?.GetValue(StartupValueName) is string value &&
                   string.Equals(value.Trim().Trim('"'), executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TrySetLaunchAtStartup(string executablePath, bool enabled, out string error)
    {
        error = string.Empty;
        try
        {
            if (enabled)
            {
                using var settings = Registry.CurrentUser.CreateSubKey(StartupRegistryPath);
                if (settings is null)
                {
                    error = "Could not update the Windows startup setting.";
                    return false;
                }

                settings.SetValue(StartupValueName, $"\"{executablePath}\"");
            }
            else
            {
                using var settings = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: true);
                settings?.DeleteValue(StartupValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            error = "Could not update the Windows startup setting.";
            return false;
        }
    }

    internal static bool IsAutoUpdateEnabled()
    {
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
            // On unless the user turned it off; an unreadable registry stays off.
            return settings?.GetValue(AutoUpdateValueName) is not int value || value != 0;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TrySetAutoUpdate(bool enabled, out string error)
    {
        error = string.Empty;
        try
        {
            using var settings = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath);
            if (settings is null)
            {
                error = "Could not update the automatic update setting.";
                return false;
            }

            settings.SetValue(AutoUpdateValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            error = "Could not update the automatic update setting.";
            return false;
        }
    }
}
