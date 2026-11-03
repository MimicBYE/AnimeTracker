using System.Globalization;
using System.Reflection;
using AnimeTracker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Avalonia.Controls.Templates;
using Avalonia.Threading;


namespace AnimeTracker.Desktop;

public sealed partial class MainWindow:Window
{
    readonly AppStore store;
    readonly AppearanceBinding appearance;
    readonly Covers covers;
    readonly Func<Window,Task<string?>> chooseMalFile;
    readonly CancellationTokenSource lifetime=new();
    readonly TextBlock message=Paper.Text("",12,Paper.Muted);
    readonly TabControl tabs=new();
    readonly PaperListBox libraryRows=new(){Classes={"library"},Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
    readonly TextBlock librarySummary=Paper.Text("",12,Paper.Muted);
    readonly TextBlock libraryEmpty=Paper.Text("Пока ничего нет. Добавьте аниме или откройте настройки для импорта XML и переноса данных.",18);
    readonly DispatcherTimer searchTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    bool filtersDirty;
    readonly StackPanel queueBody=new();
    readonly StackPanel personalRows=new();
    TextBox search=null!;
    ComboBox status=null!,sort=null!;
    Button genres=null!;
    bool initializing;
    public MainWindow(AppStore store,Func<Window,Task<string?>>? chooseMalFile=null,ISystemAppearance? systemAppearance=null)
    {
        this.chooseMalFile=chooseMalFile??(owner=>Paper.OpenFile(owner,"Импорт из MyAnimeList","*.xml","*.gz"));
        this.store=store;appearance=new AppearanceBinding(store,systemAppearance??new DesktopSystemAppearance(),action=>{if(Dispatcher.UIThread.CheckAccess())action();else Dispatcher.UIThread.Post(action);},DesktopTheme.Apply);covers=new Covers(store);Title="Anime Tracker · "+DesktopIntegration.Name;Width=1040;Height=800;MinWidth=850;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        libraryRows.ItemTemplate=new FuncDataTemplate<Anime>((anime,_)=>anime==null?null:AnimeRow(anime));
        searchTimer.Tick+=(_,_)=>FlushPendingFilters();
        using(var icon=Avalonia.Platform.AssetLoader.Open(new Uri("avares://AnimeTracker/Assets/app.ico")))Icon=new WindowIcon(icon);
        var root=new Grid{RowDefinitions=new RowDefinitions("*,Auto"),Margin=new Thickness(12,7,12,0)};
        root.Children.Add(tabs);
        var settings=Paper.AsyncButton("",Settings);settings.Classes.Add("gear");settings.Content=new PathIcon{Data=StreamGeometry.Parse("M9.5 2H14.5L15.2 5L17.2 6.2L20.2 5.5L22.5 9.5L20.3 11.6V14L22.5 16L20.2 20L17.2 19.3L15.2 20.5L14.5 23.5H9.5L8.8 20.5L6.8 19.3L3.8 20L1.5 16L3.7 14V11.6L1.5 9.5L3.8 5.5L6.8 6.2L8.8 5ZM12 9A3.8 3.8 0 1 0 12 16.6A3.8 3.8 0 1 0 12 9Z"),Width=18,Height=18};
        Avalonia.Automation.AutomationProperties.SetName(settings,"Настройки");settings.HorizontalAlignment=HorizontalAlignment.Right;settings.VerticalAlignment=VerticalAlignment.Top;root.Children.Add(settings);
        var bottom=new Border{Padding=new Thickness(14,4),Child=message};Grid.SetRow(bottom,1);root.Children.Add(bottom);Content=root;
        tabs.ItemsSource=new[]{LibraryTab(),EveningTab(),CalendarTab(),SequelsTab(),PersonalTab(),GlobalTopTab()};
        store.Changed+=RefreshCommon;RefreshCommon();
        Closing+=(_,e)=>{FlushPendingFilters();if(!App.CurrentApp.Quitting){e.Cancel=true;if(DesktopIntegration.CloseExits)Dispatcher.UIThread.Post(App.CurrentApp.Quit);else Hide();}};
        Closed+=(_,_)=>{searchTimer.Stop();store.Changed-=RefreshCommon;appearance.Dispose();lifetime.Cancel();};
        KeyDown+=(_,e)=>{if((e.KeyModifiers.HasFlag(KeyModifiers.Meta)||e.KeyModifiers.HasFlag(KeyModifiers.Control))&&e.Key==Key.F){tabs.SelectedIndex=0;search.Focus();e.Handled=true;}};
    }
    public void SetMessage(string value)=>message.Text=value;
    void RefreshCommon(){RenderLibrary();RenderPersonal();refreshCalendar?.Invoke();message.Text=store.Error;}
    static DockPanel Pane(Control header,Control body){var pane=new DockPanel{Margin=new Thickness(26,22)};DockPanel.SetDock(header,Dock.Top);pane.Children.Add(header);pane.Children.Add(Paper.Scroll(body));return pane;}
    static TabItem Tab(string name,Control content){var tab=new TabItem{Header=name,Content=content};Avalonia.Automation.AutomationProperties.SetName(tab,name);return tab;}
    public Task AddFromWidget(){App.CurrentApp.OpenLibrary();return AddAnime();}
    async Task Act(Action action,Window? owner=null){try{action();}catch(Exception e){await Paper.Message(owner??this,e.Message,"Не удалось выполнить действие");}}
    async Task Modal(Window dialog,Window? owner=null){store.ModalDepth++;try{await dialog.ShowDialog(owner??this);}finally{store.ModalDepth--;}}
    async Task Edit(Anime anime){await EditAnime(anime);RefreshCommon();}
    TabItem LibraryTab()
    {
        initializing=true;
        search=new TextBox{PlaceholderText="Поиск по названию · ⌘F",Text=store.View.Search,MinWidth=230};
        status=Paper.Choice(new[]{"Все аниме"}.Concat(MalImport.StatusLabels),Array.IndexOf(MalImport.StatusKeys,store.View.Status)+1,150);
        sort=Paper.Choice(LibraryView.SortLabels,store.View.Sort,235);
        genres=Paper.AsyncButton("Жанры",ChooseGenres);
        var add=Paper.Primary(Paper.AsyncButton("＋ Добавить",AddAnime));
        var toolbar=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto,Auto"),ColumnSpacing=8,Margin=new Thickness(0,15,0,12)};
        var controls=new Control[]{search,status,Paper.Button("Виджет",()=>store.SetWidget(store.Widget.Pinned,store.Widget.Style,true)),add};for(int i=0;i<controls.Length;i++){Grid.SetColumn(controls[i],i);toolbar.Children.Add(controls[i]);}
        var header=Paper.Stack(Paper.Text("МОЯ КОЛЛЕКЦИЯ",11,Paper.Accent),Paper.Text("Библиотека аниме",30),toolbar,Paper.Actions(genres,sort));
        search.TextChanged+=(_,_)=>SaveFilters(false);status.SelectionChanged+=(_,_)=>SaveFilters(true);sort.SelectionChanged+=(_,_)=>SaveFilters(true);initializing=false;
        // A bounded viewport is essential: an outer ScrollViewer would disable virtualization.
        var pane=new DockPanel{Margin=new Thickness(26,22)};DockPanel.SetDock(header,Dock.Top);pane.Children.Add(header);
        DockPanel.SetDock(librarySummary,Dock.Top);pane.Children.Add(librarySummary);
        DockPanel.SetDock(libraryEmpty,Dock.Top);pane.Children.Add(libraryEmpty);pane.Children.Add(libraryRows);
        return Tab("Библиотека",pane);
    }
    bool CaptureFilters(){if(search==null)return false;string query=search.Text??"",state=status.SelectedIndex<=0?"":MalImport.StatusKeys[status.SelectedIndex-1];int order=Math.Max(0,sort.SelectedIndex);if(store.View.Search==query&&store.View.Status==state&&store.View.Sort==order)return false;store.View.Search=query;store.View.Status=state;store.View.Sort=order;filtersDirty=true;return true;}
    void SaveFilters(bool immediate){if(initializing||!CaptureFilters())return;searchTimer.Stop();if(immediate)FlushPendingFilters();else searchTimer.Start();}
    public void FlushPendingFilters(){searchTimer.Stop();if(!initializing)CaptureFilters();if(!filtersDirty)return;try{store.SaveView();filtersDirty=false;}catch(Exception e){SetMessage("Не удалось сохранить фильтры: "+e.Message);}RenderLibrary();}
    void RenderLibrary()
    {
        if(search==null)return;genres.Content=store.View.Genres.Count==0?"Все жанры ▾":"Жанры: "+store.View.Genres.Count+(store.View.MatchAll?" · все ▾":" · любой ▾");
        var items=LibraryView.Select(store.Library.Items,store.View.Search,store.View.Status,store.View.Genres,store.View.MatchAll,store.View.Sort);
        librarySummary.Text=items.Count+" аниме · "+items.Sum(a=>a.Watched)+" просмотренных серий";
        libraryEmpty.IsVisible=items.Count==0;libraryRows.IsVisible=items.Count>0;libraryRows.ItemsSource=items;
    }
    Control AnimeRow(Anime anime,int position=0)
    {
        var row=new DockPanel{LastChildFill=true};var actions=Paper.Actions(Paper.Primary(Paper.AsyncButton("+1",()=>Act(()=>store.Increment(anime)))),Paper.Quiet(Paper.AsyncButton("Карточка",()=>Edit(anime))));actions.VerticalAlignment=VerticalAlignment.Center;((Button)actions.Children[0]).IsEnabled=!store.ReadOnly&&!anime.Completed&&(anime.Total<=0||anime.Watched<anime.Total);DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
        var cover=covers.Image(anime,lifetime.Token);cover.Margin=new Thickness(0,0,14,0);DockPanel.SetDock(cover,Dock.Left);row.Children.Add(cover);
        var texts=new StackPanel{Spacing=5,Margin=new Thickness(0,0,14,0)};texts.Children.Add(Paper.Text((position>0?position+". ":"")+anime.Title,18));
        var names=string.Join(" · ",new[]{anime.English,anime.Japanese,anime.Romaji}.Where(s=>!string.IsNullOrWhiteSpace(s)&&s!=anime.Title).Distinct());if(names.Length>0)texts.Children.Add(Paper.Text(names,12,Paper.Muted));
        var progress=Paper.Text(MalImport.StatusLabel(anime.Status)+" · "+anime.Watched+" / "+(anime.Total>0?anime.Total.ToString():"?")+" серий"+(anime.AiringStatus=="ongoing"?" · вышло "+anime.EpisodesAired:""),13);progress.Margin=new Thickness(0,5,0,0);texts.Children.Add(progress);
        if(anime.Total>0)texts.Children.Add(new ProgressBar{Minimum=0,Maximum=anime.Total,Value=anime.Watched,Height=4,Foreground=Paper.Accent,Background=Paper.Color("#E8D59D"),Margin=new Thickness(0,2,0,3)});
        var details=new WrapPanel();var score=Paper.Text(anime.Score>0?"★ "+anime.Score+" / 10":"☆ Без оценки",12);score.Margin=new Thickness(0,0,16,4);details.Children.Add(score);if(anime.Genres.Length>0)details.Children.Add(Paper.Text(anime.Genres.Replace("|"," · "),12,Paper.Muted));texts.Children.Add(details);
        texts.PointerReleased+=async(_,e)=>{if(e.InitialPressMouseButton==MouseButton.Left)await Edit(anime);};row.Children.Add(texts);return Paper.Card(row);
    }
    async Task ChooseGenres()
    {
        FlushPendingFilters();
        var checkboxes=new List<CheckBox>();var list=new StackPanel{Spacing=6};var all=new CheckBox{Content="Требовать все выбранные жанры",IsChecked=store.View.MatchAll};
        foreach(var name in new[]{"Без жанров"}.Concat(store.Library.Items.SelectMany(LibraryView.Genres).Concat(store.View.Genres).Where(g=>g!="Без жанров").Distinct().OrderBy(g=>g))){var check=new CheckBox{Content=name,IsChecked=store.View.Genres.Contains(name)};checkboxes.Add(check);list.Children.Add(check);}
        var panel=new DockPanel();var dialog=Paper.Dialog(this,"Жанры",panel,450,540);var actions=Paper.Actions(Paper.AsyncButton("Применить",()=>Act(()=>{store.View.Genres=checkboxes.Where(c=>c.IsChecked==true).Select(c=>(string)c.Content!).ToList();store.View.MatchAll=all.IsChecked==true;store.SaveView();dialog.Close();RenderLibrary();})),Paper.Button("Снять все",()=>{foreach(var check in checkboxes)check.IsChecked=false;}),Paper.Button("Отмена",()=>dialog.Close()));DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);DockPanel.SetDock(all,Dock.Top);panel.Children.Add(all);panel.Children.Add(Paper.Scroll(list));await Modal(dialog);
    }
    TabItem QueueTab()=>Tab("Очередь",Paper.Scroll(queueBody));
    void RenderQueue()
    {
        queueBody.Children.Clear();queueBody.Margin=new Thickness(26,22);queueBody.Children.Add(Paper.Text("Посмотреть следующим",28));queueBody.Children.Add(Paper.Text("Порядок сохраняется. Завершённые тайтлы скрываются автоматически.",12,Paper.Muted));
        var available=store.Library.Items.Where(a=>WatchChoices.CanQueue(a)&&!store.Choices.Queue.Contains(a.Id)).OrderBy(a=>a.Title).ToArray();var choice=Paper.Choice(available.Select(a=>a.Title),available.Length>0?0:-1,300);
        queueBody.Children.Add(Paper.Actions(choice,Paper.AsyncButton("＋ В очередь",()=>Act(()=>{if(choice.SelectedIndex>=0)store.ChangeChoices(next=>next.Add(available[choice.SelectedIndex]));}))));
        var active=store.Choices.Active(store.Library);queueBody.Children.Add(Paper.Text("В очереди: "+active.Length,12,Paper.Muted));
        for(int i=0;i<active.Length;i++){var anime=active[i];var up=Paper.AsyncButton("↑",()=>Act(()=>store.ChangeChoices(next=>next.Move(anime.Id,-1,store.Library))));up.IsEnabled=i>0;var down=Paper.AsyncButton("↓",()=>Act(()=>store.ChangeChoices(next=>next.Move(anime.Id,1,store.Library))));down.IsEnabled=i<active.Length-1;
            queueBody.Children.Add(Paper.Card(Paper.Stack(AnimeRow(anime,i+1),Paper.Actions(up,down,Paper.AsyncButton("Убрать",()=>Act(()=>store.ChangeChoices(next=>next.Queue.Remove(anime.Id)))),Paper.AsyncButton(anime.Status=="watching"?"Продолжить":"Начать смотреть",async()=>{await Act(()=>store.Status(anime,"watching"));await Edit(anime);}),Paper.AsyncButton("В виджет",()=>Act(()=>store.SetWidget(anime.Id,store.Widget.Style)))))));}
    }
    async Task ImportMalDialog(Window owner)
    {
        var path=await chooseMalFile(owner);if(path==null)return;
        var incoming=MalImport.Read(path);var rows=MalImport.Preview(store.Library,incoming);var check=new CheckBox{Content="Для совпадений взять статус из XML",IsChecked=false};
        var preview=new StackPanel{Spacing=5};preview.Children.Add(Paper.Text("В файле: "+incoming.Count+" · новых: "+rows.Count(r=>r.Existing==null),18));preview.Children.Add(Paper.Text("Прогресс не уменьшается. Перед импортом сохраняются копии исходного файла и библиотеки.",12,Paper.Muted));preview.Children.Add(check);
        foreach(var row in rows)preview.Children.Add(Paper.Text(row.Title+" · "+row.MalStatus+" · "+row.Progress+" · "+row.Local,12));
        var panel=new DockPanel();var dialog=Paper.Dialog(owner,"Импорт MyAnimeList — предварительный просмотр",panel,740,570);var buttons=Paper.Actions(Paper.AsyncButton("Импортировать",()=>Act(()=>{store.CommitMal(path,incoming,check.IsChecked==true);dialog.Close();SetMessage("Импортировано записей: "+incoming.Count);},dialog)),Paper.Button("Отмена",()=>dialog.Close()));DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);panel.Children.Add(Paper.Scroll(preview));await Modal(dialog,owner);
    }
    public async Task Settings()
    {
        var enabled=new CheckBox{Content="Показывать виджет",IsChecked=store.Widget.Enabled};var style=Paper.Choice(new[]{"Бумага","Компактная полоска","Обложка с прогрессом"},store.Widget.Style,280);var startup=new CheckBox{Content="Запускать при входе в систему",IsChecked=DesktopIntegration.AutoStartEnabled,IsEnabled=DesktopIntegration.AutoStartAvailable};
        bool? startupBaseline=startup.IsChecked;
        var appearance=Paper.Choice(AppearanceOptions.Labels,store.Appearance.Index,280);Avalonia.Automation.AutomationProperties.SetName(appearance,"Тема приложения");Avalonia.Automation.AutomationProperties.SetName(style,"Оформление виджета");
        var panel=Paper.Stack(Paper.Text("Настройки",28),Paper.Card(Paper.Stack(Paper.Text("Оформление приложения",20),Paper.Field("Тема",appearance))),Paper.Card(Paper.Stack(Paper.Text("Виджет и запуск",20),enabled,Paper.Field("Оформление виджета",style),startup)));var layout=new DockPanel();var dialog=Paper.Dialog(this,"Настройки",layout,680,590);
        if(OperatingSystem.IsLinux())panel.Children.Add(Paper.Card(Paper.Stack(Paper.Text("Linux",20),Paper.Text("Виджет — отдельное окно. Его видимость при показе рабочего стола зависит от оболочки. Закрытие библиотеки завершает приложение; автозапуск открывает библиотеку и работает без трея.",13,Paper.Muted),Paper.AsyncButton("Добавить в меню приложений",async()=>{DesktopIntegration.InstallMenu();await Paper.Message(dialog,"Ярлык добавлен в меню. После перемещения папки приложения создайте его заново.");}))));
        var footer=Paper.Actions(Paper.Primary(Paper.AsyncButton("Применить",()=>Act(()=>{if(style.SelectedIndex!=store.Widget.Style||(enabled.IsChecked==true)!=store.Widget.Enabled)store.SetWidget(store.Widget.Pinned,style.SelectedIndex,enabled.IsChecked==true);if(DesktopIntegration.AutoStartAvailable&&(startup.IsChecked==true||startup.IsChecked!=DesktopIntegration.AutoStartEnabled))DesktopIntegration.SetAutoStart(startup.IsChecked==true);store.SetAppearance(AppearanceOptions.Modes[Math.Max(0,appearance.SelectedIndex)]);dialog.Close();}))),Paper.Button("Закрыть",()=>dialog.Close()));footer.Margin=new Thickness(0,14,0,0);DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);layout.Children.Add(Paper.Scroll(panel));
        var import=Paper.AsyncButton("Импорт XML из MyAnimeList",()=>ImportMalDialog(dialog));import.IsEnabled=!store.ReadOnly;
        var data=Paper.Stack(Paper.Text("Данные и резервные копии",20),Paper.Text("Импортируйте список MyAnimeList или перенесите папку data с Windows. Перед заменой данных сохраняется резервная копия.",13,Paper.Muted),Paper.Actions(import));
        data.Children.Add(Paper.Actions(Paper.Button("Открыть папку данных",()=>DesktopIntegration.OpenFolder(store.Root)),Paper.AsyncButton("Перенести папку из Windows",async()=>{
            var directories=await dialog.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Выберите папку data из Windows",AllowMultiple=false});var source=directories.FirstOrDefault()?.TryGetLocalPath();if(source==null)return;var imported=AnimeTracker.Library.Load(Path.Combine(source,"library.xml"));if(!File.Exists(Path.Combine(source,"library.xml")))throw new IOException("В папке нет library.xml.");if(await Paper.Message(dialog,"Будет перенесено аниме: "+imported.Items.Count+". Текущая папка данных будет сохранена отдельной резервной копией.","Перенос данных",true)){var backup=store.ImportWindowsFolder(source);await Paper.Message(dialog,"Готово. Предыдущие данные: "+backup);}
        }),Paper.AsyncButton("Резервная копия ZIP",async()=>{var target=await Paper.SaveFile(dialog,"Сохранить резервную копию","AnimeTracker-backup.zip","zip");if(target!=null){store.ExportBackup(target);await Paper.Message(dialog,"Резервная копия сохранена.");}})));
        panel.Children.Add(Paper.Card(Paper.Stack(Paper.Text("Обновления приложения",20),Paper.Text("Проверка, скачивание и установка без ручного копирования файлов",12,Paper.Muted),Paper.AsyncButton("Обновления…",()=>UpdatesDialog(dialog,()=>appearance.SelectedIndex!=store.Appearance.Index||style.SelectedIndex!=store.Widget.Style||(enabled.IsChecked==true)!=store.Widget.Enabled||(startup.IsEnabled&&startup.IsChecked!=startupBaseline))))));
        panel.Children.Add(Paper.Card(data));
        var version=typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??typeof(App).Assembly.GetName().Version?.ToString(2)??"";
        panel.Children.Add(Paper.Text("Anime Tracker · "+version,11,Paper.Muted));await Modal(dialog);
    }
}
