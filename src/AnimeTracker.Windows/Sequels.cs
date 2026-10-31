using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnimeTracker {
public partial class Tracker {
 TabItem BuildSequelsTab(Window owner) {
  var tab=new TabItem { Header="Продолжения" };
  var panel=new DockPanel { Margin=new Thickness(28,20,28,20) }; tab.Content=panel;
  var header=new StackPanel(); header.Children.Add(Text("Что смотреть дальше",26,Ink));
  header.Children.Add(Text("Продолжения просмотренных аниме по связям Shikimori",12,Muted));
  var controls=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,10) };
  var update=new Button { Content="Обновить" }; controls.Children.Add(update);
  var airing=new ComboBox { ItemsSource=new[]{"Любой выпуск","Вышли","Выходят","Анонсированы"},SelectedIndex=0,Width=170,Margin=new Thickness(6,0,14,0) }; controls.Children.Add(airing);
  var include=new CheckBox { Content="Показать просмотренные",VerticalAlignment=VerticalAlignment.Center,Foreground=Ink }; controls.Children.Add(include); header.Children.Add(controls);
  var info=Text("Откройте вкладку, чтобы найти продолжения.",12,Muted); info.Margin=new Thickness(0,0,0,12); header.Children.Add(info); DockPanel.SetDock(header,Dock.Top); panel.Children.Add(header);
  var rows=new StackPanel(); var scroll=new SmoothScrollViewer { Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; panel.Children.Add(scroll);
  var links=new Dictionary<int,List<CatalogEntry>>(); var lifetime=new CancellationTokenSource(); bool closed=false,loading=false; string lastKey=null,report="";
  Func<int[]> sourceIds=()=>library.Items.Where(a=>a.Status=="completed" && a.CatalogId>0).Select(a=>a.CatalogId).Distinct().OrderBy(id=>id).ToArray();
  Action render=null;
  render=delegate {
   if(closed) return; rows.Children.Clear();
   var suggestions=SequelSuggestions.Select(library,links,include.IsChecked==true,new[]{"","released","ongoing","anons"}[Math.Max(0,airing.SelectedIndex)]);
   info.Text=report+(loading?"":" · показано: "+suggestions.Count);
   if(suggestions.Count==0 && !loading) rows.Children.Add(Text("Нет продолжений для выбранных условий. Можно включить просмотренные или обновить каталог.",15,Muted));
   foreach(var suggestion in suggestions) {
    var entry=suggestion.Entry; var row=new DockPanel { Margin=new Thickness(0,0,0,14),Background=ThemeBrush(Color.FromRgb(255,249,225)) };
    var actions=new StackPanel { Width=155,Margin=new Thickness(12),VerticalAlignment=VerticalAlignment.Center }; DockPanel.SetDock(actions,Dock.Right); row.Children.Add(actions);
    var owned=Ratings.Find(library,entry);
    if(owned!=null) {
     actions.Children.Add(Text("Мой статус: "+MalImport.StatusLabel(owned.Status),12,Ink)); actions.Children.Add(Text(owned.Score>0?"★ "+owned.Score+" / 10":"Без оценки",11,Muted));
     actions.Children.Add(Btn("Изменить",delegate { Edit(owned,owner); render(); }));
    } else {
     var add=new Button { Content="＋ В планы",IsEnabled=!storageBlocked }; actions.Children.Add(add);
     add.Click+=delegate { if(storageBlocked) return; try { service.Add(entry); undo=null; Refresh(); render(); } catch(Exception ex) { ShowMessage(owner,"Не удалось добавить аниме:\n"+ex.Message,"Ошибка сохранения"); } };
    }
    var cover=new Image { Width=52,Height=76,Stretch=Stretch.UniformToFill,Margin=new Thickness(10) }; DockPanel.SetDock(cover,Dock.Left); row.Children.Add(cover); LoadCover(cover,entry.Id,entry.CoverUrl,lifetime.Token);
    var text=new StackPanel { Margin=new Thickness(0,10,0,10) }; text.Children.Add(Text(entry.Title,16,Ink));
    text.Children.Add(Text("Продолжение: "+String.Join(" · ",suggestion.Sources),12,Muted));
    string state=entry.AiringStatus=="released"?"Вышло":entry.AiringStatus=="ongoing"?"Выходит":entry.AiringStatus=="anons"?"Анонсировано":"Выпуск не уточнён";
    text.Children.Add(Text(state+" · "+entry.Info,12,Muted)); row.Children.Add(text); rows.Children.Add(row);
   }
  };
  Func<bool,Task> load=async delegate(bool force) {
   if(closed || loading) return; var ids=sourceIds(); string key=String.Join(",",ids);
   if(!force && key==lastKey) { render(); return; }
   loading=true; update.IsEnabled=false; int checkedCount=0,failed=0;
   var fresh=new Dictionary<int,List<CatalogEntry>>();
   try {
    for(int i=0;i<ids.Length;i+=10) {
     info.Text="Проверяю просмотренные аниме: "+i+" / "+ids.Length+"…";
     var batch=ids.Skip(i).Take(10).ToArray();
     try { var result=await Catalog.Sequels(batch,lifetime.Token); foreach(var pair in result) fresh[pair.Key]=pair.Value; checkedCount+=result.Count; failed+=batch.Count(id=>!result.ContainsKey(id)); }
     catch(OperationCanceledException) { if(closed) return; failed+=batch.Length; }
     catch(Exception) { failed+=batch.Length; }
     if(closed) return;
    }
    // Keep previous results for failed batches; never present partial data as complete.
    if(force) foreach(var pair in links) if(ids.Contains(pair.Key) && !fresh.ContainsKey(pair.Key)) fresh[pair.Key]=pair.Value;
    links=fresh; lastKey=failed==0?key:null;
    int unlinked=library.Items.Count(a=>a.Status=="completed" && a.CatalogId<=0);
    report=ids.Length==0?"Нет просмотренных аниме, связанных с Shikimori":"Проверено: "+checkedCount+" / "+ids.Length;
    if(failed>0) report+=" · не удалось проверить: "+failed+". Нажмите «Обновить»";
    if(unlinked>0) report+=" · без связи с каталогом: "+unlinked;
   } finally { loading=false; if(!closed) { update.IsEnabled=true; render(); } }
  };
  tab.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent,new RoutedEventHandler(async delegate(object s,RoutedEventArgs e) { if(e.OriginalSource==tab) await load(false); }));
  update.Click+=async delegate { await load(true); };
  airing.SelectionChanged+=delegate { render(); }; include.Checked+=delegate { render(); }; include.Unchecked+=delegate { render(); };
  owner.Closed+=delegate { closed=true; lifetime.Cancel(); lifetime.Dispose(); };
  return tab;
 }
}
}
