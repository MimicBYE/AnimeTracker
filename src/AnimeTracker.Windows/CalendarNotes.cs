using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace AnimeTracker {
public partial class Tracker {
 void EditNotes(Anime anime,Window owner) {
  var window=new Window { Title="Заметки — "+anime.Title,Width=570,Height=450,MinWidth=400,MinHeight=300,Owner=owner,WindowStartupLocation=WindowStartupLocation.CenterOwner }; StyleWindow(window);
  var panel=new DockPanel { Margin=new Thickness(24) }; window.Content=panel;
  var title=Text(anime.Title,20,Ink); title.Margin=new Thickness(0,0,0,12); DockPanel.SetDock(title,Dock.Top); panel.Children.Add(title);
  var text=new TextBox { Text=anime.Notes,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=20000 };
  var bottom=new StackPanel { Margin=new Thickness(0,12,0,0) }; DockPanel.SetDock(bottom,Dock.Bottom); panel.Children.Add(bottom);
  bottom.Children.Add(Text("Личная заметка · сохраняется только на этом компьютере",11,Muted));
  var error=Text("",12,ThemeBrush("#B22222")); bottom.Children.Add(error); var actions=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0) }; bottom.Children.Add(actions);
  var save=Btn("Сохранить",delegate { if(storageBlocked) return; try { service.Notes(anime,text.Text); undo=null; Refresh(); window.Close(); } catch(Exception ex) { error.Text="Не удалось сохранить: "+ex.Message; } }); save.IsEnabled=!storageBlocked; actions.Children.Add(save);
  var cancel=Btn("Отмена",delegate { window.Close(); }); cancel.IsCancel=true; actions.Children.Add(cancel); panel.Children.Add(text);
  window.Loaded+=delegate { text.Focus(); text.CaretIndex=text.Text.Length; }; window.ShowDialog();
 }
 TabItem BuildCalendarTab(Window owner) {
  var tab=new TabItem { Header="Календарь" }; var panel=new DockPanel { Margin=new Thickness(28,20,28,20) }; tab.Content=panel;
  var header=new StackPanel(); header.Children.Add(Text("Календарь выхода",26,Ink)); header.Children.Add(Text("Ближайшие серии из «Смотрю» и «В планах» · время московское (UTC+3)",12,Muted));
  var controls=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,10) }; header.Children.Add(controls);
  var back=new Button { Content="← Неделя" }; var today=new Button { Content="Сегодня" }; var next=new Button { Content="Неделя →" }; var update=new Button { Content="Обновить" }; controls.Children.Add(back); controls.Children.Add(today); controls.Children.Add(next); controls.Children.Add(update);
  var view=new ComboBox { ItemsSource=new[]{"Месяц","Неделя"},SelectedIndex=0,Width=105,Margin=new Thickness(6,0,0,0) }; controls.Children.Add(view);
  var planned=new CheckBox { Content="Включать планы",IsChecked=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0) }; controls.Children.Add(planned);
  var info=Text("",12,Muted); header.Children.Add(info); DockPanel.SetDock(header,Dock.Top); panel.Children.Add(header);
  var rows=new StackPanel { Margin=new Thickness(0,12,0,0) }; var scroll=new SmoothScrollViewer { Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; panel.Children.Add(scroll);
  string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","schedule.xml"); var schedule=service.Schedule;
  var lifetime=new CancellationTokenSource(); bool closed=false,busy=false; DateTime week=CalendarNotes.Moscow(DateTimeOffset.UtcNow).Date; string message="";
  Func<Anime[]> selected=()=>library.Items.Where(a=>a.Status=="watching" || (planned.IsChecked==true && a.Status=="planned")).ToArray();
  Action render=null;
  Func<Anime,string,UIElement> card=delegate(Anime a,string caption) {
   var dock=new DockPanel { Margin=new Thickness(0,6,0,8),Background=ThemeBrush(Color.FromRgb(255,249,225)) };
   var button=Btn(a.Notes.Length>0?"Заметки •":"Заметки",delegate { OpenAnimeCard(a,owner); render(); }); button.Margin=new Thickness(10); button.VerticalAlignment=VerticalAlignment.Center; DockPanel.SetDock(button,Dock.Right); dock.Children.Add(button);
   var texts=new StackPanel { Margin=new Thickness(12) }; texts.Children.Add(Text(a.Title,16,Ink)); texts.Children.Add(Text(caption+" · "+MalImport.StatusLabel(a.Status),12,Muted)); dock.Children.Add(texts); return dock;
  };
  render=delegate {
   if(closed) return; rows.Children.Clear(); var items=selected();
   bool month=view.SelectedIndex==0; var culture=CultureInfo.GetCultureInfo("ru-RU");
   var monthStart=new DateTime(week.Year,week.Month,1); var start=month?CalendarNotes.Week(monthStart):CalendarNotes.Week(week);
   int days=month?((int)(monthStart.AddMonths(1).AddDays(-1)-start).TotalDays/7+1)*7:7;
   back.Content=month?"← Месяц":"← Неделя"; next.Content=month?"Месяц →":"Неделя →";
   info.Text=(month?monthStart.ToString("MMMM yyyy",culture):start.ToString("dd.MM")+" — "+start.AddDays(6).ToString("dd.MM.yyyy"))+" · "+message;
   var now=CalendarNotes.Moscow(DateTimeOffset.UtcNow);
   var grid=new Grid(); for(int col=0;col<7;col++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
   grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
   for(int row=0;row<days/7;row++) grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
   var names=new[]{"ПН","ВТ","СР","ЧТ","ПТ","СБ","ВС"};
   for(int col=0;col<7;col++) { var name=Text(names[col],12,col>=5?Accent:Muted); name.Margin=new Thickness(8,0,0,8); Grid.SetColumn(name,col); grid.Children.Add(name); }
   rows.Children.Add(grid);
   for(int i=0;i<days;i++) {
    var day=start.AddDays(i); bool isToday=day.Date==now.Date,otherMonth=month && day.Month!=monthStart.Month;
    var cell=new Border { MinHeight=month?82:240,Margin=new Thickness(2),Padding=new Thickness(6),CornerRadius=new CornerRadius(4),Background=ThemeBrush(isToday?Color.FromRgb(245,225,164):otherMonth?Color.FromRgb(244,235,205):Color.FromRgb(255,249,225)),BorderBrush=isToday?Accent:ThemeBrush(Color.FromRgb(225,207,157)),BorderThickness=new Thickness(isToday?2:1) };
    var content=new StackPanel(); cell.Child=content; Grid.SetColumn(cell,i%7); Grid.SetRow(cell,i/7+1); grid.Children.Add(cell);
    var heading=Text(day.Day.ToString()+(isToday?" · сегодня":""),12,otherMonth?Muted:Ink); heading.Margin=new Thickness(0,0,0,5); content.Children.Add(heading);
    var events=items.Where(a=>schedule.ContainsKey(a.CatalogId) && schedule[a.CatalogId].Next.HasValue && CalendarNotes.Moscow(schedule[a.CatalogId].Next.Value).Date==day).OrderBy(a=>schedule[a.CatalogId].Next).ToArray();
    foreach(var a in events) {
     var entry=schedule[a.CatalogId]; var date=CalendarNotes.Moscow(entry.Next.Value);
     var button=Btn("",delegate { OpenAnimeCard(a,owner); render(); }); button.Margin=new Thickness(0,2,0,2); button.Padding=new Thickness(5); button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
     button.Background=ThemeBrush(Color.FromRgb(235,215,152)); button.ToolTip=a.Title+"\n"+date.ToString("dd.MM HH:mm")+" МСК · "+MalImport.StatusLabel(a.Status)+(date<now?"\nДата прошла — обновите расписание":"")+"\nНажмите, чтобы открыть карточку";
     var eventText=new StackPanel(); eventText.Children.Add(Text(date.ToString("HH:mm"),12,Ink));
     var title=Text(a.Title,11,Ink); title.MaxHeight=44; title.TextTrimming=TextTrimming.CharacterEllipsis; eventText.Children.Add(title); button.Content=eventText; content.Children.Add(button);
    }
   }
   var unknown=items.Where(a=>!schedule.ContainsKey(a.CatalogId) || !schedule[a.CatalogId].Next.HasValue).ToArray();
   if(unknown.Length>0) {
    var unknownRows=new StackPanel(); var group=new Expander { Header="Без точной даты · "+unknown.Length,Content=unknownRows,Foreground=Ink,Margin=new Thickness(0,18,0,0) }; rows.Children.Add(group);
    foreach(var a in unknown) { ScheduleEntry entry; schedule.TryGetValue(a.CatalogId,out entry); unknownRows.Children.Add(card(a,a.CatalogId<=0?"Не связано с Shikimori":entry==null?"Расписание ещё не загружено":entry.Status=="released"?"Выпуск завершён": "Shikimori не указал дату следующей серии")); }
   }
  };
  Action scheduleUpdated=delegate {
   if(closed) return; schedule=service.Schedule;
   var stamps=selected().Where(a=>schedule.ContainsKey(a.CatalogId)).Select(a=>schedule[a.CatalogId].Checked).ToArray();
   message=stamps.Length==0?"Даты обновляются вместе с проверкой новых серий":"Данные на "+CalendarNotes.Moscow(new DateTimeOffset(DateTime.SpecifyKind(stamps.Min(),DateTimeKind.Utc))).ToString("dd.MM HH:mm")+" · только ближайшие серии";
   if(episodeUpdateError.Length>0) message=episodeUpdateError;
   render();
  };
  ScheduleChanged+=scheduleUpdated;
  Func<bool,Task> load=async delegate(bool force) {
   if(closed || busy) return; busy=true; update.IsEnabled=false; scheduleUpdated();
   try { await RefreshEpisodes(force); }
   finally { busy=false; if(!closed) { update.IsEnabled=true; scheduleUpdated(); } }
  };
  back.Click+=delegate { week=view.SelectedIndex==0?new DateTime(week.Year,week.Month,1).AddMonths(-1):week.AddDays(-7); scroll.ResetToTop(); render(); }; next.Click+=delegate { week=view.SelectedIndex==0?new DateTime(week.Year,week.Month,1).AddMonths(1):week.AddDays(7); scroll.ResetToTop(); render(); }; today.Click+=delegate { week=CalendarNotes.Moscow(DateTimeOffset.UtcNow).Date; scroll.ResetToTop(); render(); };
  view.SelectionChanged+=delegate { scroll.ResetToTop(); render(); };
  update.Click+=async delegate { await load(true); }; planned.Checked+=async delegate { await load(false); }; planned.Unchecked+=delegate { render(); };
  tab.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent,new RoutedEventHandler(async delegate(object s,RoutedEventArgs e) { if(e.OriginalSource==tab) await load(false); }));
  owner.Closed+=delegate { closed=true; ScheduleChanged-=scheduleUpdated; lifetime.Cancel(); lifetime.Dispose(); }; return tab;
 }
}
}
