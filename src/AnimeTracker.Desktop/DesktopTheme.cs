using AnimeTracker;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AnimeTracker.Desktop;

internal static class DesktopTheme
{
    static readonly Dictionary<string,SolidColorBrush> brushes=new(StringComparer.OrdinalIgnoreCase);
    static Application? appliedApp;
    public static bool Dark {get;private set;}
    public static SolidColorBrush Brush(string light)
    {
        if(!brushes.TryGetValue(light,out var brush)){brush=new SolidColorBrush(Color.Parse(PaperPalette.Resolve(light,Dark)));brushes[light]=brush;}
        return brush;
    }
    public static void Apply(bool dark)
    {
        if(appliedApp!=null&&ReferenceEquals(appliedApp,Application.Current)&&Dark==dark)return;
        Dark=dark;
        foreach(var light in PaperPalette.LightColors)Brush(light).Color=Color.Parse(PaperPalette.Resolve(light,dark));
        if(Application.Current is not {} app)return;
        foreach(var light in PaperPalette.LightColors)app.Resources[PaperPalette.Key(light)]=Brush(light);
        app.Resources["SystemAccentColor"]=Brush("#B9862C").Color;
        app.Resources["SystemControlHighlightAccentBrush"]=Brush("#B9862C");
        Set("#FFF8DF","ComboBoxDropDownBackground","ComboBoxBackgroundUnfocused","ComboBoxBackgroundPointerOver","ComboBoxBackgroundPressed","TextControlBackgroundFocused","TextControlBackgroundPointerOver");
        Set("#D6C18B","ComboBoxDropDownBorderBrush","ComboBoxBackgroundBorderBrushUnfocused","ComboBoxBorderBrushPointerOver","ComboBoxBorderBrushPressed","TextControlBorderBrushPointerOver");
        Set("#B9862C","ComboBoxBackgroundBorderBrushFocused","TextControlBorderBrushFocused");
        Set("#47361D","ComboBoxForegroundFocused","ComboBoxForegroundFocusedPressed","ComboBoxDropDownGlyphForeground","ComboBoxDropDownGlyphForegroundFocused","ComboBoxDropDownGlyphForegroundFocusedPressed","TextControlForegroundFocused","TextControlForegroundPointerOver","TextControlForeground");
        Set("#E8CF79","ListBoxItemBackgroundSelected","ListBoxItemBackgroundSelectedPointerOver","ListBoxItemBackgroundSelectedPressed");
        Set("#EFDA8D","ListBoxItemBackgroundPointerOver","ListBoxItemBackgroundPressed");
        Set("#47361D","ListBoxItemForegroundSelected","ListBoxItemForegroundSelectedPointerOver","ListBoxItemForegroundSelectedPressed");
        app.RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light;
        appliedApp=app;
    }
    static void Set(string light,params string[] keys){foreach(var key in keys)Application.Current!.Resources[key]=Brush(light);}
}
