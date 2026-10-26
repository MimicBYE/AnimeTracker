using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace AnimeTracker {
public partial class Tracker {
 FrameworkElement BuildArcs(Anime anime,Window owner,CancellationToken token) {
  var section=new Expander{Header="Арки и филлеры",Foreground=Ink,Margin=new Thickness(0,16,0,16)};var panel=new StackPanel();section.Content=panel;
  panel.Children.Add(Text("Номера серий относятся к этому сезону. Разметка не меняет прогресс просмотра.",12,Muted));
  string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","arcs.xml"),cache=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","episode-flags",anime.MalId+".xml");
  var catalog=ArcCatalog.Find(anime.MalId,anime.Id);var flags=catalog!=null?catalog.Flags:anime.MalId>0?ArcData.ReadCache(cache):new EpisodeFlags();
  List<AnimeArc> arcs=null;string error="";try{arcs=ArcData.Load(path);}catch(Exception ex){error="Не удалось прочитать арки: "+ex.Message;}
  var toolbar=new WrapPanel{Margin=new Thickness(0,10,0,10)};panel.Children.Add(toolbar);
  var add=new Button{Content="＋ Добавить арку",IsEnabled=arcs!=null&&!storageBlocked};toolbar.Children.Add(add);
  var update=new Button{Content="Обновить отметки серий",IsEnabled=anime.MalId>0,Visibility=catalog==null?Visibility.Visible:Visibility.Collapsed};toolbar.Children.Add(update);
  var onlyFiller=new CheckBox{Content="Только филлеры",Margin=new Thickness(6),VerticalAlignment=VerticalAlignment.Center};toolbar.Children.Add(onlyFiller);
  if(error.Length>0)panel.Children.Add(Text(error,12,Brushes.Firebrick));
  var info=Text("",12,Muted);panel.Children.Add(info);
  if(catalog!=null){
   info.Text="Локальный справочник · отметки до серии "+flags.Items.Keys.Max()+" · источник от "+catalog.Date;
   panel.Children.Add(Text("Именованные арки могут покрывать не все серии. Справочник обновляется вместе с приложением.",11,Muted));
   var links=new WrapPanel();panel.Children.Add(links);
   foreach(var pair in new[]{Tuple.Create("Источник арок ↗",catalog.Source),Tuple.Create("AnimeFillerList ↗",catalog.FlagsSource)}){var url=pair.Item2;links.Children.Add(Btn(pair.Item1,delegate{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});}catch{info.Text="Не удалось открыть браузер: "+url;}}));}
  }
  var rows=new StackPanel();panel.Children.Add(rows);bool busy=false,requested=false;
  Action render=null;render=delegate{
   if(token.IsCancellationRequested)return;rows.Children.Clear();int next=anime.Watched+1;string nextType;flags.Items.TryGetValue(next,out nextType);
   rows.Children.Add(Text(anime.Total>0&&anime.Watched>=anime.Total?"Все серии этого сезона отмечены просмотренными":"Следующая серия: "+next+" · "+ArcData.Label(nextType),14,Ink));
   var visible=ArcCatalog.Merge(catalog,arcs??new List<AnimeArc>(),anime.Id);int shown=0;
   foreach(var arc in visible){
    string classification=ArcData.Classification(arc,flags);var episodes=ArcCatalog.Episodes(arc);
    // Include mixed arcs containing filler episodes so the filter doesn't hide useful information.
    bool hasFiller=arc.Type=="filler"||(arc.Type=="auto"&&episodes.Any(n=>flags.Items.ContainsKey(n)&&flags.Items[n]=="filler"));
    if(onlyFiller.IsChecked==true&&!hasFiller)continue;shown++;
    var row=new DockPanel{Margin=new Thickness(0,8,0,8)};var selected=arc;
    var edit=Btn("Изменить",delegate{EditArc(anime,owner,selected,arcs,path);render();});edit.IsEnabled=arcs!=null&&!storageBlocked;DockPanel.SetDock(edit,Dock.Right);row.Children.Add(edit);
    bool personal=arcs!=null&&arcs.Any(a=>a.Id==arc.Id&&a.AnimeId==anime.Id);
    var texts=new StackPanel();texts.Children.Add(Text(arc.Name,16,Ink));
    texts.Children.Add(Text("Серии "+ArcCatalog.Format(episodes)+" · "+ArcData.Label(classification)+(personal?" · ваша разметка":" · справочник"),12,classification=="filler"?Brushes.Sienna:Muted));
    texts.Children.Add(Text("Просмотрено: "+episodes.Count(n=>n<=anime.Watched)+" / "+episodes.Count+(episodes.Contains(next)?" · текущая арка":""),11,Muted));
    if(hasFiller&&classification!="filler"&&arc.Type=="auto")texts.Children.Add(Text("Филлеры: "+ArcCatalog.Format(episodes.Where(n=>flags.Items.ContainsKey(n)&&flags.Items[n]=="filler")),11,Brushes.Sienna));
    row.Children.Add(texts);rows.Children.Add(row);
   }
   if(shown==0)rows.Children.Add(Text(onlyFiller.IsChecked==true?"Арки с отмеченными филлерами не найдены.":"Именованных арок пока нет. Их можно добавить вручную.",12,Muted));
   if(arcs!=null&&catalog!=null&&arcs.Any(a=>a.AnimeId==anime.Id&&a.CatalogKey!=""))rows.Children.Add(Btn("Сбросить правки справочных арок",delegate{if(storageBlocked)return;try{var updated=arcs.Where(a=>a.AnimeId!=anime.Id||a.CatalogKey=="").ToList();ArcData.Save(updated,path);arcs.Clear();arcs.AddRange(updated);render();}catch(Exception ex){info.Text=ex.Message;}}));
   var ranges=new Expander{Header="Отметки по сериям · "+(catalog==null?"Jikan / MyAnimeList":"AnimeFillerList"),Margin=new Thickness(0,12,0,0),Foreground=Ink};var list=new StackPanel();ranges.Content=list;rows.Children.Add(ranges);
   foreach(var range in ArcData.Ranges(flags)){if(onlyFiller.IsChecked==true&&range.Type!="filler")continue;list.Children.Add(Text((range.From==range.To?"Серия "+range.From:"Серии "+range.From+"–"+range.To)+" · "+ArcData.Label(range.Type),12,range.Type=="filler"?Brushes.Sienna:Muted));}
   if(flags.Items.Count==0)list.Children.Add(Text("Отметок пока нет. Источник может не содержать серии для этого тайтла.",12,Muted));
  };
  Func<bool,Task> load=async delegate(bool force){if(catalog!=null||busy||anime.MalId<=0||token.IsCancellationRequested)return;if(!force&&flags.Checked!=DateTime.MinValue&&DateTime.UtcNow-flags.Checked<TimeSpan.FromHours(6)){info.Text="Сохранённые отметки на "+flags.Checked.ToLocalTime().ToString("dd.MM HH:mm");return;}busy=true;update.IsEnabled=false;try{flags=await ArcData.Fetch(anime.MalId,cache,token,(page,total)=>{info.Text="Загрузка отметок: страница "+page+" / "+total;});if(!token.IsCancellationRequested)info.Text="Получено отметок: "+flags.Items.Count+" · Jikan / MyAnimeList";}catch(Exception){if(!token.IsCancellationRequested)info.Text="Источник сейчас недоступен. Сохранённые отметки и ваши арки доступны.";}finally{busy=false;if(!token.IsCancellationRequested){update.IsEnabled=true;render();}}};
  add.Click+=delegate{EditArc(anime,owner,null,arcs,path);render();};update.Click+=async delegate{await load(true);};onlyFiller.Checked+=delegate{render();};onlyFiller.Unchecked+=delegate{render();};
  section.Expanded+=async delegate(object sender,RoutedEventArgs e){if(e.OriginalSource!=section||requested)return;requested=true;render();if(anime.MalId<=0)info.Text="Для загрузки отметок нужен ID MyAnimeList. Арки можно добавить вручную.";else await load(false);};
  panel.Children.Add(Text("Смешанная: есть смешанные серии или серии разных типов. Неизвестные серии не считаются каноном. Фильтр показывает также арки, содержащие отдельные филлеры.",11,Muted));return section;
 }
 void EditArc(Anime anime,Window owner,AnimeArc existing,List<AnimeArc> arcs,string path) {
  var window=new Window{Title=existing==null?"Добавить арку":"Изменить арку",Owner=owner,Width=500,Height=470,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};StyleWindow(window);
  var panel=new StackPanel{Margin=new Thickness(24)};window.Content=panel;panel.Children.Add(Text("Название арки",12,Muted));
  var name=new TextBox{Text=existing==null?"":existing.Name,MaxLength=120,Margin=new Thickness(0,5,0,12)};panel.Children.Add(name);
  panel.Children.Add(Text("Серии · например: 1–12, 15, 18–24",12,Muted));var intervals=new TextBox{Text=existing==null?"1":ArcCatalog.Format(ArcCatalog.Episodes(existing)),Margin=new Thickness(0,5,0,12)};panel.Children.Add(intervals);
  panel.Children.Add(Text("Тип",12,Muted));var type=new ComboBox{ItemsSource=ArcData.Labels,SelectedIndex=existing==null?0:Array.IndexOf(ArcData.Types,existing.Type),Margin=new Thickness(0,5,0,12)};panel.Children.Add(type);
  panel.Children.Add(Text("Ваши правки имеют приоритет над справочником. Статус и прогресс просмотра не меняются.",12,Muted));var error=Text("",12,Brushes.Firebrick);panel.Children.Add(error);var buttons=new WrapPanel{Margin=new Thickness(0,16,0,0)};panel.Children.Add(buttons);
  Action<List<AnimeArc>> save=delegate(List<AnimeArc> updated){ArcData.Save(updated,path);arcs.Clear();arcs.AddRange(updated);window.Close();};
  buttons.Children.Add(Btn("Сохранить арку",delegate{if(storageBlocked)return;try{
   var changed=new AnimeArc{Id=existing==null?Guid.NewGuid().ToString():existing.Id,AnimeId=anime.Id,Name=name.Text.Trim(),Type=ArcData.Types[Math.Max(0,type.SelectedIndex)],CatalogKey=existing==null?"":existing.CatalogKey};
   ArcCatalog.SetEpisodes(changed,ArcCatalog.Episodes(intervals.Text));ArcData.Validate(changed);
   if(anime.Total>0&&changed.To>anime.Total)throw new ArgumentException("Диапазон выходит за количество серий этого сезона.");
   var numbers=ArcCatalog.Episodes(changed);if(arcs.Any(a=>a.AnimeId==anime.Id&&a.Id!=changed.Id&&!a.Hidden&&ArcCatalog.Episodes(a).Overlaps(numbers)))throw new ArgumentException("Серии пересекаются с другой вашей аркой.");
   save(ArcCatalog.Replace(arcs,changed));
  }catch(Exception ex){error.Text=ex.Message;}}));
  var cancel=Btn("Отмена",delegate{window.Close();});cancel.IsCancel=true;buttons.Children.Add(cancel);
  if(existing!=null)buttons.Children.Add(Btn(existing.CatalogKey==""?"Удалить арку":"Скрыть арку",delegate{if(storageBlocked)return;try{
   if(existing.CatalogKey=="")save(arcs.Where(a=>a.Id!=existing.Id||a.AnimeId!=anime.Id).ToList());
   else save(ArcCatalog.Replace(arcs,new AnimeArc{Id=existing.Id,AnimeId=anime.Id,Name=existing.Name,Type=existing.Type,From=existing.From,To=existing.To,Intervals=existing.Intervals,CatalogKey=existing.CatalogKey,Hidden=true}));
  }catch(Exception ex){error.Text=ex.Message;}}));window.ShowDialog();
 }
}
}