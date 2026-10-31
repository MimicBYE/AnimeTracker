using AnimeTracker;
using Avalonia;
using Avalonia.Platform;

namespace AnimeTracker.Desktop;

internal sealed class DesktopSystemAppearance:ISystemAppearance
{
    readonly IPlatformSettings? settings=Application.Current?.PlatformSettings;
    public event Action? Changed;
    public DesktopSystemAppearance(){if(settings!=null)settings.ColorValuesChanged+=ColorsChanged;}
    public bool Dark {get{try{return settings?.GetColorValues().ThemeVariant==PlatformThemeVariant.Dark;}catch(NotSupportedException){return false;}catch(InvalidOperationException){return false;}}}
    void ColorsChanged(object? sender,PlatformColorValues values)=>Changed?.Invoke();
    public void Dispose(){if(settings!=null)settings.ColorValuesChanged-=ColorsChanged;}
}
