namespace Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

public enum WindowSoftInputModeAdjust { Pan, Resize, Unspecified }

/// <summary>MAUI's Android-only application settings. They mean nothing on the Switch 2, so they do nothing.</summary>
public static class Application
{
    public static void SetWindowSoftInputModeAdjust(BindableObject element, WindowSoftInputModeAdjust value)
    {
    }

    public static WindowSoftInputModeAdjust GetWindowSoftInputModeAdjust(BindableObject element) => WindowSoftInputModeAdjust.Unspecified;
}
