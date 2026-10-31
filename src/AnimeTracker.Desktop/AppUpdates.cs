using AnimeTracker;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace AnimeTracker.Desktop;

public sealed partial class App
{
    public AppUpdates? Updates {get;private set;}
    void InitializeUpdates()
    {
        if(!OperatingSystem.IsMacOS()&&!OperatingSystem.IsLinux())return;
        var platform=OperatingSystem.IsMacOS()?"macos-arm64":OperatingSystem.IsLinux()?"linux-x64":"windows-x64";
        var state=OperatingSystem.IsLinux()?Path.Combine(LinuxPaths.StateHome,"updates"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library","Caches","AnimeTracker.Updates");
        Updates=new AppUpdates(Store.Files,Environment.ProcessPath!,platform,state,typeof(App).Assembly.GetName().Version!.ToString(3));
        Updates.Changed+=()=>{if(Updates.Available!=null)LibraryWindow.SetMessage("Доступна версия "+Updates.Available.Version+" · ⚙ → Обновления");};
    }
}

public sealed partial class MainWindow
{
    async Task UpdatesDialog(Window owner,Func<bool> draft)
    {
        var updates=App.CurrentApp.Updates;if(updates==null){await Paper.Message(owner,"Обновления доступны в установленной сборке приложения.");return;}
        var status=Paper.Text("",13,Paper.Muted);var notes=Paper.Text("",13);var automatic=new CheckBox{Content="Проверять вместе с обновлением серий",IsChecked=updates.Options.Automatic};var development=new CheckBox{Content="Получать предварительные версии",IsChecked=updates.Options.Development};
        var layout=new DockPanel();var dialog=Paper.Dialog(owner,"Обновления Anime Tracker",layout,650,610);
        var check=Paper.AsyncButton("Проверить",()=>updates.Check(true,lifetime.Token));
        var install=Paper.Primary(Paper.AsyncButton("Скачать и установить",async()=>{
            if(draft()){await Paper.Message(dialog,"Сначала примените или сбросьте изменения в настройках приложения.","Несохранённые настройки");return;}
            if(automatic.IsChecked!=updates.Options.Automatic||development.IsChecked!=updates.Options.Development){await Paper.Message(dialog,"Сначала сохраните настройки обновлений.");return;}
            if(!await Paper.Message(dialog,"Приложение скачает проверенный пакет, сохранит резервную копию и перезапустится. Библиотека и настройки сохранятся.","Установить "+updates.Available?.Version+"?",true))return;
            try{
                var progress=new Progress<double>(value=>{if(dialog.IsVisible)status.Text="Загрузка: "+Math.Round(value)+"%";});var plan=await updates.Prepare(progress,lifetime.Token);
                if(Program.Arguments.Contains("--no-tray"))plan.Arguments=plan.Arguments.Concat(new[]{"--no-tray"}).ToArray();plan.Save();
                FlushPendingFilters();AppUpdates.Launch(plan);App.CurrentApp.Quit();
            }catch(OperationCanceledException){}catch(Exception error){await Paper.Message(dialog,error.Message,"Обновление не установлено");}
        }));
        var heading=Paper.Stack(Paper.Text("Обновления приложения",25),Paper.Text("Установлена версия "+updates.Build.packageVersion,13,Paper.Muted),automatic,development,Paper.Text("Предварительные версии могут содержать незавершённые изменения. Установка всегда запускается кнопкой.",12,Paper.Muted),Paper.AsyncButton("Сохранить",()=>Act(()=>updates.SetOptions(automatic.IsChecked==true,development.IsChecked==true),dialog)));
        DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var footer=Paper.Stack(status,Paper.Actions(check,install,Paper.Button("Отмена загрузки",updates.Cancel),Paper.Button("Закрыть",()=>dialog.Close())));footer.Margin=new Avalonia.Thickness(0,14,0,0);DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);layout.Children.Add(Paper.Scroll(notes));
        void Refresh(){status.Text=updates.Status;check.IsEnabled=!updates.Busy;install.IsEnabled=!updates.Busy&&updates.Available!=null;automatic.IsEnabled=development.IsEnabled=!updates.Busy;notes.Text=updates.Available==null?"Проверка и установка выполняются без ручного копирования файлов.":"Новая версия: "+updates.Available.Version+"\n\n"+(updates.Available.Notes??"")[..Math.Min(16000,(updates.Available.Notes??"").Length)];}
        updates.Changed+=Refresh;dialog.Closed+=(_,_)=>{updates.Changed-=Refresh;updates.Cancel();};Refresh();await Modal(dialog,owner);
    }
}
