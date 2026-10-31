using System.Reflection;
using AnimeTracker;
using AnimeTracker.Desktop;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static Program;

internal static class ThemeTests
{
    static Color ColorOf(IBrush? brush)=>((ISolidColorBrush)brush!).Color;
    static Color Expected(string light,bool dark)=>Color.Parse(PaperPalette.Resolve(light,dark));
    static Button FindButton(Window window,string text)=>window.GetLogicalDescendants().OfType<Button>().Distinct().Single(b=>b.Content as string==text);
    static ComboBox ThemeChoice(Window window)=>window.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c=>AutomationProperties.GetName(c)=="Тема приложения");
    static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
    static void Capture(Control control,string? folder,string name,double width,double height)
    {
        if(folder==null)return;Directory.CreateDirectory(folder);control.Measure(new Size(width,height));control.Arrange(new Rect(0,0,width,height));control.UpdateLayout();
        using var bitmap=new RenderTargetBitmap(new PixelSize((int)width,(int)height),new Vector(96,96));bitmap.Render(control);bitmap.Save(Path.Combine(folder,name+".png"),PngBitmapEncoderOptions.Default);
    }
    internal static object Run(string root,string? screenshots)
    {
        var store=new AppStore(Path.Combine(root,"theme"));store.SetWidget("",0,false);
        foreach(var title in new[]{"Бумажная история","Путешествие к звёздам","Последний летний день"}){var anime=store.AddManual(title);store.Update(anime,title,7,24,"watching",8,"Синтетические данные");anime.English="Paper story";anime.Genres="Приключения|Драма";}
        store.Save();var original=File.ReadAllBytes(store.LibraryPath);
        var window=new MainWindow(store){Opacity=0,ShowInTaskbar=false,ShowActivated=false};window.Show();Dispatcher.UIThread.RunJobs();window.UpdateLayout();
        var widget=new WidgetWindow(store);var content=(Panel)window.Content!;content.Background=window.Background;
        var combo=content.GetLogicalDescendants().OfType<ComboBox>().First();var search=content.GetLogicalDescendants().OfType<TextBox>().First();var oldForeground=search.Foreground;
        try
        {
            foreach(bool dark in new[]{false,true,false,true})
            {
                store.SetAppearance(dark?"dark":"light");Dispatcher.UIThread.RunJobs();window.UpdateLayout();
                Check(window.ActualThemeVariant==(dark?ThemeVariant.Dark:ThemeVariant.Light),"Fluent theme variant");
                Check(ColorOf(window.Background)==Expected("#F5E5A6",dark),"Window background");
                Check(ReferenceEquals(oldForeground,search.Foreground)&&ColorOf(search.Foreground)==Expected("#47361D",dark),"Existing text foreground");
                Check(ColorOf(search.Background)==Expected("#FFF8DF",dark)&&ColorOf(search.CaretBrush)==Expected("#47361D",dark),"Text input/caret");
                Check(ColorOf(((Border)widget.Content!).Background)==Expected("#F5E5A6",dark),"Existing widget surface");
                combo.ApplyTemplate();var popup=combo.GetVisualDescendants().Concat(combo.GetLogicalDescendants().OfType<Visual>()).OfType<Popup>().Distinct().Single(p=>p.Name=="PART_Popup");
                var popupBorder=(Border)popup.Child!;popupBorder.Opacity=0;combo.IsDropDownOpen=true;Dispatcher.UIThread.RunJobs();
                Check(popup.IsOpen&&ColorOf(popupBorder.Background)==Expected("#FFF8DF",dark),"Native combo popup");combo.IsDropDownOpen=false;
                var primary=FindButton(window,"＋ Добавить");Check(ColorOf(primary.Background)==Expected("#DDB44A",dark)&&ColorOf(primary.Foreground)==Expected("#241B0B",dark),"Primary button contrast");
                Capture(content,screenshots,"desktop-"+(dark?"dark":"light"),1040,800);
                Capture((Control)widget.Content!,screenshots,"desktop-widget-"+(dark?"dark":"light"),widget.Width,widget.Height);
            }
            var task=window.Settings();var dialog=window.OwnedWindows.Single();dialog.Opacity=0;dialog.ShowInTaskbar=false;Dispatcher.UIThread.RunJobs();
            ThemeChoice(dialog).SelectedIndex=0;Check(store.Appearance.Dark,"Settings draft applied early");Click(FindButton(dialog,"Закрыть"));PumpUntil(()=>task.IsCompleted);Check(store.Appearance.Dark,"Cancel applied theme");
            // Keep startup untouched even on a developer's macOS/Linux session.
            task=window.Settings();dialog=window.OwnedWindows.Single();dialog.Opacity=0;Dispatcher.UIThread.RunJobs();ThemeChoice(dialog).SelectedIndex=0;
            var startup=dialog.GetLogicalDescendants().OfType<CheckBox>().Single(c=>c.Content as string=="Запускать при входе в систему");
            if(startup.IsChecked!=true){Click(FindButton(dialog,"Применить"));PumpUntil(()=>task.IsCompleted);Check(!store.Appearance.Dark&&!new AppStore(store.Files).Appearance.Dark,"Apply/restart theme");}
            else {Click(FindButton(dialog,"Закрыть"));PumpUntil(()=>task.IsCompleted);store.SetAppearance("light");}
            Check(File.ReadAllBytes(store.LibraryPath).SequenceEqual(original),"Theme UI changed personal XML");
            return new{checks="repeated live light/dark switch, Fluent variant, existing text/caret, widget, native combo popup, primary contrast, Settings draft/cancel/apply/restart; personal XML unchanged",startup="real OS startup registration is never changed; Apply checked when startup is off",screenshots="synthetic fixture, shared Avalonia renderer; not manual native macOS/Linux acceptance"};
        }
        finally{foreach(var child in window.OwnedWindows.ToArray())child.Close();typeof(App).GetProperty("Quitting")!.SetValue(App.CurrentApp,true);widget.Close();window.Close();}
    }
}
