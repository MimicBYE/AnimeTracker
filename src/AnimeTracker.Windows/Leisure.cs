using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Xml.Linq;

namespace AnimeTracker {
public partial class Tracker {
 readonly string widgetOptionsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","widget-settings.xml");
 WidgetOptions widgetOptions;
 DockPanel widgetSummary;
 ProgressBar widgetProgressBar;
 Button widgetPrevious,widgetNext;
 Anime WidgetAnime() { return WidgetOptions.Selected(library,widgetOptions==null?"":widgetOptions.Pinned); }
 Size CompactSize() { var size=WidgetOptions.Size(widgetOptions==null?0:widgetOptions.Style);return new Size(size.Width,size.Height); }
 bool SaveWidgetOptions(string pinned,int style,bool? enabled=null) {
  try { var next=new WidgetOptions {Pinned=pinned,Style=style,Enabled=enabled??widgetOptions.Enabled}; next.Save(widgetOptionsPath); widgetOptions=next; widgetHidden=!next.Enabled; ApplyWidgetStyle();if(widgetHidden)widget.Hide();else{widget.Show();AttachDesktop();} Refresh(); return true; }
  catch(Exception ex) { MessageBox.Show("Не удалось сохранить настройки виджета:\n"+ex.Message); return false; }
 }
 void CycleWidget(int direction) {
  var current=WidgetAnime(); var items=library.Items.Where(a=>a.Status=="watching" || a==current).OrderBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();
  if(items.Length<2) return; int index=Array.IndexOf(items,current); var next=items[(index+direction+items.Length)%items.Length]; SaveWidgetOptions(next.Id,widgetOptions.Style); Reveal(compactTitle);
 }
 void ApplyWidgetStyle() {
  if(widget==null || widgetSummary==null) return; int style=widgetOptions.Style;
  DockPanel.SetDock(compactCover,style==2?Dock.Top:Dock.Left);
  // Dock order matters: the poster takes its own full row above the controls.
  widgetSummary.Children.Remove(compactCover); widgetSummary.Children.Insert(0,compactCover);
  compactCover.Width=style==2?230:40; compactCover.Height=style==2?136:58;
  compactCover.Stretch=style==2?Stretch.Uniform:Stretch.UniformToFill;
  compactTitle.TextWrapping=style==2?TextWrapping.Wrap:TextWrapping.NoWrap;
  compactTitle.MaxHeight=style==2?38:Double.PositiveInfinity;
  compactCover.Margin=style==2?new Thickness(0,0,0,8):new Thickness(0,0,9,0);
  compactCover.Visibility=style==1 || compactCoverId<=0?Visibility.Collapsed:Visibility.Visible;
  SetExpanded(expanded,false); ClampPosition();
 }
 void WidgetSettings(Window owner) { OpenSettings(); }
 void AnimateWidgetProgress(Anime anime) {
  if(WidgetAnime()!=anime) return;
  Reveal(compactProgress); var scale=new ScaleTransform(1,1); compactPlus.RenderTransformOrigin=new Point(.5,.5); compactPlus.RenderTransform=scale;
  var bounce=new DoubleAnimation(1.18,1,TimeSpan.FromMilliseconds(330)) {EasingFunction=new BackEase {Amplitude=.3,EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop}; scale.BeginAnimation(ScaleTransform.ScaleXProperty,bounce); scale.BeginAnimation(ScaleTransform.ScaleYProperty,bounce);
  if(anime.Completed) { compactProgress.Text="✓ ПРОСМОТРЕНО · "+anime.Watched+" серий"; compactProgress.Foreground=Accent; Reveal(compactProgress); }
 }
 TabItem BuildEveningTab(Window owner) {
  var tab=new TabItem {Header="На вечер"}; var panel=new DockPanel {Margin=new Thickness(28,20,28,20)}; tab.Content=panel;
  var header=new StackPanel(); DockPanel.SetDock(header,Dock.Top); panel.Children.Add(header); header.Children.Add(Text("Что посмотреть сегодня?",27,Ink)); header.Children.Add(Text("Выберите список для подбора · настроение определяется по жанрам",12,Muted));
  var source=new ComboBox {ItemsSource=new[]{"В планах","Топ Shikimori","Моя очередь"},SelectedIndex=0,Width=220,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,6,0,0)}; header.Children.Add(Text("Откуда выбирать",11,Muted));header.Children.Add(source);
  var filters=new Grid {Margin=new Thickness(0,18,0,16)}; for(int i=0;i<4;i++) filters.ColumnDefinitions.Add(new ColumnDefinition()); header.Children.Add(filters);
  var mood=new ComboBox {ItemsSource=EveningChoice.Moods,SelectedIndex=0};
  var genre=new ComboBox {ItemsSource=new[]{"Все жанры"}.Concat(library.Items.Where(a=>a.Status=="planned").SelectMany(a=>LibraryView.Genres(a)).Distinct().OrderBy(x=>x)).ToArray(),SelectedIndex=0};
  var format=new ComboBox {ItemsSource=new[]{"Любой формат","Фильм целиком","Короткий сериал ≤13","Сериал"},SelectedIndex=0};
  var time=new ComboBox {ItemsSource=new[]{"Без ограничения","15 минут","30 минут","1 час","2 часа","3 часа"},SelectedIndex=0}; int[] budgets={0,15,30,60,120,180};
  var controls=new[]{mood,genre,format,time}; var labels=new[]{"Настроение","Жанр","Формат","Свободное время"}; for(int i=0;i<4;i++) {var field=new StackPanel {Margin=new Thickness(0,0,8,0)}; field.Children.Add(Text(labels[i],11,Muted)); controls[i].Margin=new Thickness(0,6,0,0); field.Children.Add(controls[i]); Grid.SetColumn(field,i); filters.Children.Add(field);}
  var hiddenButton=Btn("Скрытые аниме",delegate{ShowHiddenAnime(owner);});hiddenButton.HorizontalAlignment=HorizontalAlignment.Left;hiddenButton.Margin=new Thickness(0,0,0,8);header.Children.Add(hiddenButton);
  var info=Text("",12,Muted); header.Children.Add(info);
  var output=new StackPanel {Margin=new Thickness(0,18,0,0)}; panel.Children.Add(new SmoothScrollViewer {Content=output,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
  var meta=new Dictionary<int,CatalogEntry>(); string cache=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","evening-cache.xml");
  try { if(File.Exists(cache)) foreach(var x in XElement.Load(cache).Elements("anime")) {int id=(int)x.Attribute("id"); meta[id]=new CatalogEntry {Id=id,Kind=(string)x.Attribute("kind")??"",Duration=(int?)x.Attribute("duration")??0,Episodes=(int?)x.Attribute("episodes")??0,EpisodesAired=(int?)x.Attribute("aired")??0,AiringStatus=(string)x.Attribute("status")??"",Genres=(string)x.Attribute("genres")??""};} } catch(Exception) {meta.Clear();}
  Func<int[]> metadataIds=()=>library.Items.Where(a=>(source.SelectedIndex==2?watchChoices.Queue.Contains(a.Id):a.Status=="planned")&&a.CatalogId>0).Select(a=>a.CatalogId).Distinct().OrderBy(id=>id).ToArray();
  var topEntries=new List<CatalogEntry>(); var prerequisites=new Dictionary<int,List<CatalogEntry>>(); bool topLoaded=false;
  var excluded=new HashSet<string>(); var random=new Random(); Anime current=null; bool closed=false,busy=false,loaded=false; string loadedIds=""; var lifetime=new CancellationTokenSource();
  Action pick=null; pick=delegate {
   if(closed) return; output.Children.Clear();hiddenButton.Content="Скрытые аниме ("+watchChoices.Hidden.Count+")";
   var pool=source.SelectedIndex==0?library.Items.Where(item=>item.Status=="planned"):source.SelectedIndex==2?(IEnumerable<Anime>)watchChoices.Active(library):topEntries.Where(entry=>EveningPrerequisites.Ready(entry,library,prerequisites)).Select(entry=>Ratings.Find(library,entry)??new Anime{Id="top:"+entry.Id,CatalogId=entry.Id,MalId=entry.MalId,Title=entry.Title,Genres=entry.Genres,CoverUrl=entry.CoverUrl,Total=entry.Episodes,EpisodesAired=entry.EpisodesAired,AiringStatus=entry.AiringStatus,Status="planned"});
   var candidates=pool.Where(candidate=>!excluded.Contains(candidate.Id)&&!watchChoices.IsHidden(candidate)&&EveningChoice.Matches(candidate,source.SelectedIndex==1?topEntries.FirstOrDefault(entry=>entry.Id==candidate.CatalogId||(entry.MalId>0&&entry.MalId==candidate.MalId)):(meta.ContainsKey(candidate.CatalogId)?meta[candidate.CatalogId]:null),mood.SelectedIndex,genre.SelectedIndex<=0?"":(string)genre.SelectedItem,format.SelectedIndex,budgets[Math.Max(0,time.SelectedIndex)],source.SelectedIndex!=0)).ToArray();
   if(candidates.Length==0) {current=null; bool empty=source.SelectedIndex==2?watchChoices.Active(library).Length==0:source.SelectedIndex==0&&!library.Items.Any(item=>item.Status=="planned"); output.Children.Add(Text(empty?(source.SelectedIndex==2?"В очереди пока пусто":"В списке «В планах» пока пусто"):(source.SelectedIndex==1&&!topLoaded?"Загрузите топ Shikimori":"Подходящих вариантов пока нет"),22,Ink)); output.Children.Add(Text(empty?"Добавьте аниме в выбранный список или выберите другой источник подбора.":"Нажмите «Обновить данные», измените условия или верните пропущенные варианты. Просмотренные аниме и анонсы исключены. Для продолжений нужно завершить предыдущие сезоны; непроверенные связи не участвуют. При ограничении времени нужна известная длительность.",13,Muted)); if(excluded.Count>0) output.Children.Add(Btn("Вернуть пропущенные",delegate {excluded.Clear(); pick();})); return;}
   current=source.SelectedIndex==2?candidates[0]:candidates[random.Next(candidates.Length)]; var a=current; int watched=EveningChoice.Progress(a,source.SelectedIndex==1); CatalogEntry details; meta.TryGetValue(a.CatalogId,out details);if(source.SelectedIndex==1)details=topEntries.FirstOrDefault(entry=>entry.Id==a.CatalogId||(entry.MalId>0&&entry.MalId==a.MalId))??details;
   var sheet=new Border {Background=new SolidColorBrush(Color.FromRgb(255,249,225)),Padding=new Thickness(22),CornerRadius=new CornerRadius(10),BorderBrush=new SolidColorBrush(Color.FromRgb(225,207,157)),BorderThickness=new Thickness(1)}; output.Children.Add(sheet);
   var dock=new DockPanel(); sheet.Child=dock; var cover=new Image {Width=140,Height=200,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,0,22,0)}; DockPanel.SetDock(cover,Dock.Left); dock.Children.Add(cover); LoadCover(cover,a.CatalogId,a.CoverUrl,lifetime.Token);
   var text=new StackPanel(); dock.Children.Add(text); text.Children.Add(Text(a.Title,25,Ink)); text.Children.Add(Text(a.Genres.Replace("|"," · "),12,Muted));
   string duration=details==null||details.Duration<=0?"Длительность неизвестна":details.Kind=="movie"?"Фильм · "+((long)details.Duration*Math.Max(1,details.Episodes-watched))+" мин":"Серия · "+details.Duration+" мин";
   text.Children.Add(Text(duration,14,Ink)); int budget=budgets[Math.Max(0,time.SelectedIndex)];
   if(budget>0&&details!=null&&details.Duration>0&&details.Kind!="movie") {int count=budget/details.Duration; if(details.Episodes>0) count=Math.Min(count,details.Episodes-watched); if(details.AiringStatus=="ongoing") count=Math.Min(count,Math.Max(0,details.EpisodesAired-watched)); text.Children.Add(Text("Серий за это время: "+count,13,Muted));}
   text.Children.Add(Text("Подходит под выбранные условия · ещё вариантов: "+(candidates.Length-1),12,Muted));
   var actions=new WrapPanel {Margin=new Thickness(0,18,0,0)}; text.Children.Add(actions);
   var secondary=new WrapPanel {Margin=new Thickness(0,10,0,0)};text.Children.Add(secondary);
   bool inLibrary=library.Items.Contains(a);
   if(!inLibrary) actions.Children.Add(Primary(Btn("＋ В планы",delegate {if(storageBlocked)return;try{var entry=topEntries.First(e=>e.Id==a.CatalogId);Ratings.AddPlanned(library,entry,dataPath);Refresh();info.Text="Добавлено в планы: "+a.Title;pick();}catch(Exception ex){info.Text=ex.Message;}})));
   else {
    actions.Children.Add(Primary(Btn(a.Status=="watching"?"Продолжить смотреть":"Начать смотреть",delegate {if(a.Status=="watching"){OpenAnimeCard(a,owner);pick();return;}if(storageBlocked)return;try{Ratings.UpdatePersonal(library,a,"watching",null,dataPath);Refresh();pick();}catch(Exception ex){info.Text=ex.Message;}})));
    actions.Children.Add(Btn("Карточка",delegate {OpenAnimeCard(a,owner);pick();}));
   }
   var queueButton=Btn(inLibrary&&watchChoices.Queue.Contains(a.Id)?"Уже в очереди":"＋ В очередь",delegate{
    if(watchChoicesBlocked)return;try{var owned=a;if(!library.Items.Contains(owned)){if(storageBlocked)return;owned=Ratings.AddPlanned(library,topEntries.First(e=>e.Id==a.CatalogId),dataPath);Refresh();}ChangeWatchChoices(next=>next.Add(owned),owner);}catch(Exception ex){info.Text=ex.Message;}
   });queueButton.IsEnabled=!watchChoicesBlocked&&!(inLibrary&&watchChoices.Queue.Contains(a.Id))&&(!storageBlocked||inLibrary);secondary.Children.Add(Quiet(queueButton));
   var hide=Quiet(Btn("Не предлагать",delegate{ChangeWatchChoices(next=>next.Hide(a,details),owner);}));hide.IsEnabled=!watchChoicesBlocked;secondary.Children.Add(hide);
   actions.Children.Add(Quiet(Btn("Другой вариант",delegate {excluded.Add(a.Id); pick();})));
   output.Children.Add(Btn("Вернуть пропущенные ("+excluded.Count+")",delegate {excluded.Clear(); pick();})); Reveal(sheet);
  };
  var reload=Btn("Обновить данные",delegate {}); reload.Margin=new Thickness(0,8,0,0); reload.HorizontalAlignment=HorizontalAlignment.Left; header.Children.Add(reload);
  Action refreshGenres=delegate {var selected=genre.SelectedItem as string;var available=source.SelectedIndex==1?topEntries.SelectMany(e=>e.Genres.Split('|')).Where(g=>g.Length>0):(source.SelectedIndex==2?(IEnumerable<Anime>)watchChoices.Active(library):library.Items.Where(a=>a.Status=="planned")).SelectMany(a=>LibraryView.Genres(a));var choices=new[]{"Все жанры"}.Concat(available.Distinct().OrderBy(x=>x)).ToArray();genre.ItemsSource=choices;genre.SelectedIndex=Math.Max(0,Array.IndexOf(choices,selected));};
  Func<System.Threading.Tasks.Task> load=async delegate {
   if(busy||closed)return; busy=true; source.IsEnabled=false; reload.IsEnabled=false; info.Text="Уточняю формат и длительность в Shikimori…";
   try {if(source.SelectedIndex==1){var fresh=new List<CatalogEntry>();for(int page=1;page<=4;page++){info.Text="Загрузка топа Shikimori: "+page+" / 4";var entries=await Catalog.Top(page,lifetime.Token);if(closed)return;fresh.AddRange(entries);if(entries.Count<50)break;}fresh=fresh.GroupBy(e=>e.Id).Select(g=>g.First()).ToList();info.Text="Проверяю предыдущие сезоны…";var checkedLinks=await EveningPrerequisites.Load(fresh,library,lifetime.Token,count=>{info.Text="Проверяю цепочки предыдущих сезонов: "+count;});if(closed)return;prerequisites=checkedLinks;topEntries=fresh;foreach(var entry in topEntries)meta[entry.Id]=entry;topLoaded=true;refreshGenres();info.Text="Подбор из первых "+topEntries.Count+" позиций топа Shikimori · просмотренные и продолжения без пройденных сезонов исключены";return;}var ids=metadataIds(); for(int offset=0;offset<ids.Length;offset+=50) {var entries=await Catalog.ByIds(ids.Skip(offset).Take(50),lifetime.Token); if(closed)return; foreach(var e in entries) meta[e.Id]=e;}
    new XElement("evening",meta.Values.Select(e=>new XElement("anime",new XAttribute("id",e.Id),new XAttribute("kind",e.Kind),new XAttribute("duration",e.Duration),new XAttribute("episodes",e.Episodes),new XAttribute("aired",e.EpisodesAired),new XAttribute("status",e.AiringStatus),new XAttribute("genres",e.Genres)))).Save(cache+".tmp"); if(File.Exists(cache)) File.Replace(cache+".tmp",cache,cache+".bak"); else File.Move(cache+".tmp",cache);
    info.Text="Данные обновлены · время указано без перерывов"; loaded=true; loadedIds=String.Join(",",ids.OrderBy(id=>id));
   } catch(Exception) {if(!closed) info.Text="Каталог недоступен. Используются сохранённые данные; неизвестная длительность не оценивается.";}
   finally {busy=false; if(!closed) {source.IsEnabled=true; reload.IsEnabled=true; pick();}}
  };
  foreach(var control in controls) control.SelectionChanged+=delegate {excluded.Clear(); pick();};
  Action choicesUpdated=delegate{refreshGenres();pick();};WatchChoicesChanged+=choicesUpdated;
  source.SelectionChanged+=async delegate {excluded.Clear();refreshGenres();pick();if(!busy&&(source.SelectedIndex==1?!topLoaded:(!loaded||loadedIds!=String.Join(",",metadataIds()))))await load();};
  reload.Click+=async delegate {await load();};
  tab.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent,new RoutedEventHandler(async delegate(object s,RoutedEventArgs e) {if(e.OriginalSource!=tab)return;refreshGenres();pick();if(source.SelectedIndex==1?!topLoaded:(!loaded||loadedIds!=String.Join(",",metadataIds())))await load();}));  owner.Closed+=delegate {closed=true;WatchChoicesChanged-=choicesUpdated;lifetime.Cancel();lifetime.Dispose();}; return tab;
 }
}
}
