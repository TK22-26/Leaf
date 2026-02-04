using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views.Settings;

/// <summary>
/// Interface for settings section controls that can load and save their settings.
/// </summary>
public interface ISettingsSectionControl
{
    /// <summary>
    /// Loads settings from the provided AppSettings and CredentialService.
    /// </summary>
    void LoadSettings(AppSettings settings, CredentialService credentialService);

    /// <summary>
    /// Saves settings to the provided AppSettings and CredentialService.
    /// </summary>
    void SaveSettings(AppSettings settings, CredentialService credentialService);
}
