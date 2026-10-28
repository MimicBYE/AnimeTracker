using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnimeTracker {
public partial class Tracker {
 TabItem BuildTopTab(bool global,Window window) {
  if(!global) return BuildPersonalTop(window);
  var tab=new TabItem { Header=global?"Топ Shikimori":"Личный топ" };
  var panel=new DockPanel { Margin=new Thickness(28,20,28,20) }; tab.Content=panel;
  var header=new StackPanel(); header.Children.Add(Text(global?"Топ Shikimori":"Мой топ",26,Ink));
  header.Children.Add(Text(global?"Общий рейтинг каталога · оценки Shikimori":"Все оценённые аниме · от 10 к 1 · без фильтров библиотеки",12,Muted));
  var info=Text("",12,Muted); info.Margin=new Thickness(0,10,0,10); header.Children.Add(info); DockPanel.SetDock(header,Dock.Top); panel.Children.Add(header);
  var rows=new StackPanel(); var scroll=new SmoothScrollViewer { Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
  var lifetime=new CancellationTokenSource(); bool closed=false,busy=false; int page=1;
  window.Closed+=delegate { closed=true; lifetime.Cancel(); lifetime.Dispose(); };
  if(!global) {
   Action render=null;
   render=delegate {
    rows.Children.Clear(); var items=Ratings.Top(library); info.Text=items.Length==0?"Пока нет оценок. Откройте «Изменить» у аниме и поставьте оценку.":"Оценено: "+items.Length+" · средняя оценка: "+items.Average(a=>a.Score).ToString("0.0");
    for(int i=0;i<items.Length;i++) {
     var a=items[i]; var row=new DockPanel { Margin=new Thickness(0,0,0,14) };
     var edit=Btn("Изменить",delegate { Edit(a,window); render(); }); DockPanel.SetDock(edit,Dock.Right); row.Children.Add(edit);
     var cover=new Image { Width=42,Height=60,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,0,12,0) }; DockPanel.SetDock(cover,Dock.Left); row.Children.Add(cover); LoadCover(cover,a.CatalogId,a.CoverUrl,lifetime.Token);
     var text=new StackPanel(); text.Children.Add(Text((i+1)+". "+a.Title,16,Ink)); text.Children.Add(Text("★ "+a.Score+" / 10 · "+MalImport.StatusLabel(a.Status),12,Muted)); text.Cursor=System.Windows.Input.Cursors.Hand; text.MouseLeftButtonUp+=delegate { OpenAnimeCard(a,window); render(); }; row.Children.Add(text); rows.Children.Add(row);
    }
   };
   tab.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e) { if(e.OriginalSource==tab && !closed) render(); }));
  } else {
   var navigation=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0) }; DockPanel.SetDock(navigation,Dock.Bottom); panel.Children.Add(navigation);
   var prev=new Button { Content="← Назад",IsEnabled=false }; var next=new Button { Content="Далее →",IsEnabled=false }; var retry=new Button { Content="Обновить" }; navigation.Children.Add(prev); navigation.Children.Add(next); navigation.Children.Add(retry);
   bool loaded=false;
   var refreshActions=new System.Collections.Generic.List<Action>();
   Func<int,Task> load=async delegate(int requested) {
    if(busy || closed) return; busy=true; prev.IsEnabled=next.IsEnabled=retry.IsEnabled=false; info.Text="Загружаю рейтинг…";
    try {
     var entries=await Catalog.Top(requested,lifetime.Token); if(closed) return;
     rows.Children.Clear(); refreshActions.Clear(); page=requested; scroll.ResetToTop();
     for(int i=0;i<entries.Count;i++) {
      var a=entries[i]; var row=new DockPanel { Margin=new Thickness(0,0,0,14) };
      var actions=new StackPanel { Width=145,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center }; DockPanel.SetDock(actions,Dock.Right); row.Children.Add(actions);
      Action updateActions=delegate {
       actions.Children.Clear(); var owned=Ratings.Find(library,a);
       if(owned!=null) {
        actions.Children.Add(Text("Мой статус",11,Muted));
        var status=new ComboBox { ItemsSource=MalImport.StatusLabels,SelectedIndex=Array.IndexOf(MalImport.StatusKeys,owned.Status),Padding=new Thickness(4),Margin=new Thickness(0,3,0,5),IsEnabled=!storageBlocked,ToolTip="Изменить мой статус" };
        actions.Children.Add(status); actions.Children.Add(Text("Моя оценка",11,Muted));
        var score=new ComboBox { ItemsSource=Ratings.Labels,SelectedIndex=owned.Score,Padding=new Thickness(4),Margin=new Thickness(0,3,0,0),IsEnabled=!storageBlocked,ToolTip="Изменить мою оценку" };
        actions.Children.Add(score);
        var feedback=Text("",10,Muted); actions.Children.Add(feedback);
        bool restoring=false;
        Action<string,int?> save=delegate(string value,int? rating) {
         if(restoring || storageBlocked) return;
         try { service.Personal(owned,value,rating); undo=null; Refresh(); feedback.Text="Сохранено"; }
         catch(Exception ex) {
          restoring=true; status.SelectedIndex=Array.IndexOf(MalImport.StatusKeys,owned.Status); score.SelectedIndex=owned.Score; restoring=false;
          feedback.Text="Не сохранено"; MessageBox.Show(window,"Не удалось сохранить изменения:\n"+ex.Message,"Ошибка сохранения");
         }
        };
        status.SelectionChanged+=delegate { if(status.SelectedIndex>=0) save(MalImport.StatusKeys[status.SelectedIndex],null); };
        score.SelectionChanged+=delegate { if(score.SelectedIndex>=0) save(null,score.SelectedIndex); };
       } else {
        var add=new Button { Content="＋ В планы",IsEnabled=!storageBlocked };
        if(storageBlocked) add.ToolTip="Запись отключена из-за ошибки загрузки библиотеки.";
        actions.Children.Add(add);
       }
      };
      // Rebind the one available add action whenever its local state changes.
      Action bindAdd=null; bindAdd=delegate {
       updateActions(); var add=actions.Children.OfType<Button>().FirstOrDefault(); if(add==null) return;
       add.Click+=delegate {
        if(storageBlocked) return;
        add.IsEnabled=false;
        try { service.Add(a); undo=null; Refresh(); bindAdd(); }
        catch(Exception ex) { add.IsEnabled=true; MessageBox.Show(window,"Не удалось добавить аниме:\n"+ex.Message,"Ошибка сохранения"); }
       };
      }; bindAdd(); refreshActions.Add(bindAdd);
      var cover=new Image { Width=48,Height=70,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,0,12,0) }; DockPanel.SetDock(cover,Dock.Left); row.Children.Add(cover); LoadCover(cover,a.Id,a.CoverUrl,lifetime.Token);
      var text=new StackPanel(); text.Children.Add(Text(((page-1)*50+i+1)+". "+a.Title,16,Ink)); text.Children.Add(Text("★ "+a.Score.ToString("0.00")+" / 10 · "+a.Info,12,Muted)); text.Children.Add(Text(a.Names,11,Muted)); text.Cursor=System.Windows.Input.Cursors.Hand; text.MouseLeftButtonUp+=delegate { var owned=Ratings.Find(library,a); if(owned!=null) { OpenAnimeCard(owned,window); bindAdd(); } }; row.Children.Add(text); rows.Children.Add(row);
     }
     loaded=true; info.Text="Страница "+page+" · обновлено "+DateTime.Now.ToString("HH:mm")+(entries.Count==0?" · тайтлов больше нет":""); next.IsEnabled=entries.Count==50;
    } catch(OperationCanceledException) { if(!closed) info.Text="Каталог не ответил вовремя. Нажмите «Обновить»."; }
    catch(Exception) { if(!closed) info.Text="Не удалось загрузить Shikimori. Проверьте интернет и нажмите «Обновить»."; }
    finally { busy=false; if(!closed) { prev.IsEnabled=page>1; retry.IsEnabled=true; } }
   };
   prev.Click+=async delegate { await load(page-1); }; next.Click+=async delegate { await load(page+1); }; retry.Click+=async delegate { await load(page); };
   tab.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent,new RoutedEventHandler(async delegate(object sender,RoutedEventArgs e) {
    if(e.OriginalSource!=tab || closed) return;
    if(!loaded) await load(1); else foreach(var refresh in refreshActions) refresh();
   }));
  }
  panel.Children.Add(scroll); return tab;
 }
}
}
