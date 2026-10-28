using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace AnimeTracker {
public partial class Tracker {
 WatchChoices watchChoices {get{return service.Choices;}}
 bool watchChoicesBlocked {get{return service.ChoicesBlocked;}}
 event Action WatchChoicesChanged;

 bool ChangeWatchChoices(Action<WatchChoices> edit,Window owner){
  if(watchChoicesBlocked)return false;try{service.ChangeChoices(edit);}catch(Exception ex){MessageBox.Show(owner,"Не удалось сохранить очередь или скрытые аниме:\n"+ex.Message,"Ошибка сохранения");return false;}
  if(WatchChoicesChanged!=null)WatchChoicesChanged();return true;
 }
 void QueueCardButton(Anime anime,Window owner,Panel panel){
  var button=new Button{Margin=new Thickness(0,0,6,5)};Action bind=delegate{button.Content=watchChoices.Queue.Contains(anime.Id)?"Убрать из очереди":"＋ В очередь";button.IsEnabled=!watchChoicesBlocked&&(watchChoices.Queue.Contains(anime.Id)||WatchChoices.CanQueue(anime));};
  bind();button.Click+=delegate{if(ChangeWatchChoices(next=>{if(next.Queue.Contains(anime.Id))next.Queue.Remove(anime.Id);else next.Add(anime);},owner))bind();};panel.Children.Add(button);
 }
 TabItem BuildQueueTab(Window owner){
  var tab=new TabItem{Header="Очередь"};var panel=new DockPanel{Margin=new Thickness(28,20,28,20)};tab.Content=panel;
  var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);header.Children.Add(Text("Посмотреть следующим",27,Ink));header.Children.Add(Text("Ваш порядок просмотра · завершённые и удалённые тайтлы скрываются автоматически",12,Muted));
  var toolbar=new DockPanel{Margin=new Thickness(0,14,0,12)};header.Children.Add(toolbar);var select=new ComboBox{DisplayMemberPath="Title",Margin=new Thickness(0,0,8,0)};
  var add=Btn("＋ В очередь",delegate{var option=select.SelectedItem as QueueOption;if(option!=null)ChangeWatchChoices(next=>next.Add(option.Anime),owner);});add.IsEnabled=!watchChoicesBlocked;DockPanel.SetDock(add,Dock.Right);toolbar.Children.Add(add);toolbar.Children.Add(select);
  var info=Text("",12,Muted);header.Children.Add(info);var rows=new StackPanel{Margin=new Thickness(0,12,0,0)};panel.Children.Add(new SmoothScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
  bool closed=false;var lifetime=new CancellationTokenSource();Action render=null;
  render=delegate{
   if(closed)return;rows.Children.Clear();var previous=select.SelectedItem as QueueOption;var chosen=previous==null?null:previous.Anime;var available=library.Items.Where(a=>WatchChoices.CanQueue(a)&&!watchChoices.Queue.Contains(a.Id)).OrderBy(a=>a.Status=="planned"?0:1).ThenBy(a=>a.Title).ToArray();select.ItemsSource=available.Select(a=>new QueueOption{Anime=a}).ToArray();select.SelectedIndex=available.Length==0?-1:Math.Max(0,Array.FindIndex(available,a=>chosen!=null&&a.Id==chosen.Id));add.IsEnabled=!watchChoicesBlocked&&available.Length>0;
   var active=watchChoices.Active(library);info.Text=watchChoicesBlocked?"Список недоступен для изменений: не удалось прочитать сохранённый файл.":"В очереди: "+active.Length;
   if(active.Length==0)rows.Children.Add(Text("Добавьте тайтл из списка сверху, карточки аниме или подбора «На вечер».",15,Muted));
   for(int i=0;i<active.Length;i++){
    var anime=active[i];var row=new DockPanel{Background=new SolidColorBrush(Color.FromRgb(255,249,225)),Margin=new Thickness(0,0,0,12)};var actions=new WrapPanel{Width=210,Margin=new Thickness(10),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
    var up=Btn("↑",delegate{ChangeWatchChoices(next=>next.Move(anime.Id,-1,library),owner);});up.IsEnabled=!watchChoicesBlocked&&i>0;actions.Children.Add(up);var down=Btn("↓",delegate{ChangeWatchChoices(next=>next.Move(anime.Id,1,library),owner);});down.IsEnabled=!watchChoicesBlocked&&i<active.Length-1;actions.Children.Add(down);
    var remove=Btn("Убрать",delegate{ChangeWatchChoices(next=>next.Queue.Remove(anime.Id),owner);});remove.IsEnabled=!watchChoicesBlocked;actions.Children.Add(remove);
    actions.Children.Add(Btn(anime.Status=="watching"?"Продолжить":"Начать",delegate{if(storageBlocked)return;try{if(anime.Status!="watching"){service.Personal(anime,"watching",null);Refresh();}OpenAnimeCard(anime,owner);render();}catch(Exception ex){info.Text=ex.Message;}}));
    actions.Children.Add(Btn("Карточка",delegate{OpenAnimeCard(anime,owner);render();}));actions.Children.Add(Btn("В виджет",delegate{SaveWidgetOptions(anime.Id,widgetOptions.Style);}));
    var cover=new Image{Width=55,Height=80,Stretch=Stretch.Uniform,Margin=new Thickness(12)};DockPanel.SetDock(cover,Dock.Left);row.Children.Add(cover);LoadCover(cover,anime.CatalogId,anime.CoverUrl,lifetime.Token);
    var texts=new StackPanel{Margin=new Thickness(0,12,0,12)};texts.Children.Add(Text((i+1)+". "+anime.Title,17,Ink));texts.Children.Add(Text(MalImport.StatusLabel(anime.Status)+" · "+anime.Watched+" / "+(anime.Total>0?anime.Total.ToString():"?")+" серий",12,Muted));if(watchChoices.IsHidden(anime))texts.Children.Add(Text("Скрыто в вечернем подборе",11,Muted));row.Children.Add(texts);rows.Children.Add(row);
   }
  };
  WatchChoicesChanged+=render;tab.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e){if(e.OriginalSource==tab)render();}));owner.Closed+=delegate{closed=true;WatchChoicesChanged-=render;lifetime.Cancel();lifetime.Dispose();};return tab;
 }
 void ShowHiddenAnime(Window owner){
  var window=new Window{Title="Скрытые в подборе на вечер",Owner=owner,Width=610,Height=470,MinWidth=480,MinHeight=320,WindowStartupLocation=WindowStartupLocation.CenterOwner};StyleWindow(window);var panel=new DockPanel{Margin=new Thickness(22)};window.Content=panel;var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);header.Children.Add(Text("Не предлагать на вечер",25,Ink));header.Children.Add(Text("Тайтлы остаются в библиотеке и очереди. Верните их, чтобы снова участвовать в подборе.",12,Muted));var rows=new StackPanel{Margin=new Thickness(0,14,0,0)};panel.Children.Add(new SmoothScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Action render=null;render=delegate{rows.Children.Clear();if(watchChoices.Hidden.Count==0)rows.Children.Add(Text("Скрытых аниме нет.",14,Muted));foreach(var entry in watchChoices.Hidden.OrderBy(a=>a.Title)){var item=entry;var row=new DockPanel{Margin=new Thickness(0,0,0,10)};var restore=Btn("Вернуть",delegate{if(ChangeWatchChoices(next=>next.Hidden.RemoveAll(a=>a.LibraryId==item.LibraryId&&a.CatalogId==item.CatalogId&&a.MalId==item.MalId),window))render();});restore.IsEnabled=!watchChoicesBlocked;DockPanel.SetDock(restore,Dock.Right);row.Children.Add(restore);row.Children.Add(Text(item.Title,15,Ink));rows.Children.Add(row);}};render();window.ShowDialog();
 }
}
}
