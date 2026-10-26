using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AnimeTracker {
public partial class Tracker {
 static void Reveal(FrameworkElement element,int delay=0) {
  var move=new TranslateTransform(); element.RenderTransform=move;
  var duration=TimeSpan.FromMilliseconds(280); var start=TimeSpan.FromMilliseconds(delay);
  element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,duration) { BeginTime=start,FillBehavior=FillBehavior.Stop });
  move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(12,0,duration) { BeginTime=start,EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut },FillBehavior=FillBehavior.Stop });
 }
 void AnimateTabs(TabControl tabs) {
  tabs.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e) {
   if(e.OriginalSource!=tabs) return; var item=tabs.SelectedItem as TabItem; var content=item==null?null:item.Content as FrameworkElement;
   if(content!=null) Reveal(content);
  };
 }
 void OpenAnimeCard(Anime anime,Window owner) {
  clickTimer.Stop();
  var window=new Window { Title=anime.Title,Width=760,Height=Math.Min(810,SystemParameters.WorkArea.Height-50),MinWidth=600,MinHeight=450,Owner=owner,WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner,ShowInTaskbar=owner==null };
  StyleWindow(window); var lifetime=new CancellationTokenSource(); bool closed=false;
  var root=new DockPanel { Margin=new Thickness(24) }; window.Content=root;
  var footer=new StackPanel { Margin=new Thickness(0,14,0,0) }; DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
  var error=Text("",12,Brushes.Firebrick); footer.Children.Add(error);
  var actions=new WrapPanel(); footer.Children.Add(actions);
  var body=new StackPanel(); var cardScroll=new SmoothScrollViewer { Content=body,Focusable=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; root.Children.Add(cardScroll);
  var hero=new DockPanel { Margin=new Thickness(0,0,0,20) }; body.Children.Add(hero);
  var cover=new Image { Width=126,Height=180,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,0,22,0) }; DockPanel.SetDock(cover,Dock.Left); hero.Children.Add(cover); LoadCover(cover,anime.CatalogId,anime.CoverUrl,lifetime.Token);
  var headline=new StackPanel(); hero.Children.Add(headline); headline.Children.Add(Text("МОЯ БИБЛИОТЕКА",11,Muted));
  var heading=Text(anime.Title,27,Ink); heading.FontFamily=new FontFamily("Georgia"); heading.Margin=new Thickness(0,8,0,8); headline.Children.Add(heading);
  headline.Children.Add(Text(String.Join(" · ",new[]{anime.English,anime.Japanese,anime.Romaji}.Where(s=>!String.IsNullOrWhiteSpace(s)&&s!=anime.Title).Distinct()),12,Muted));
  headline.Children.Add(Text(anime.Genres.Replace("|"," · "),12,Muted));
  var progress=new ProgressBar { Minimum=0,Maximum=Math.Max(1,anime.Total>0?anime.Total:anime.Watched),Value=anime.Watched,Height=5,Margin=new Thickness(0,18,0,8),Foreground=Accent,Background=new SolidColorBrush(Color.FromRgb(232,213,157)),BorderThickness=new Thickness(0) }; headline.Children.Add(progress);
  var progressLabel=Text(anime.Watched+" / "+(anime.Total>0?anime.Total.ToString():"?")+" серий · "+MalImport.StatusLabel(anime.Status),12,Muted); headline.Children.Add(progressLabel);
  var sheet=new Border { Background=new SolidColorBrush(Color.FromRgb(255,249,225)),CornerRadius=new CornerRadius(9),Padding=new Thickness(18),BorderBrush=new SolidColorBrush(Color.FromRgb(225,207,157)),BorderThickness=new Thickness(1) }; body.Children.Add(sheet);
  var fields=new StackPanel(); sheet.Child=fields;
  fields.Children.Add(Text("Прогресс и впечатления",19,Ink));
  var controls=new Grid { Margin=new Thickness(0,12,0,12) }; for(int i=0;i<4;i++) controls.ColumnDefinitions.Add(new ColumnDefinition()); fields.Children.Add(controls);
  var status=new ComboBox { ItemsSource=MalImport.StatusLabels,SelectedIndex=Array.IndexOf(MalImport.StatusKeys,anime.Status) };
  var watched=new TextBox { Text=anime.Watched.ToString() }; var total=new TextBox { Text=anime.Total.ToString() };
  var score=new ComboBox { ItemsSource=Ratings.Labels,SelectedIndex=anime.Score };
  var inputs=new FrameworkElement[]{status,watched,total,score}; var labels=new[]{"Статус","Просмотрено","Всего · 0 неизвестно","Моя оценка"};
  for(int i=0;i<4;i++) { var field=new StackPanel { Margin=new Thickness(0,0,8,0) }; field.Children.Add(Text(labels[i],11,Muted)); inputs[i].Margin=new Thickness(0,5,0,0); field.Children.Add(inputs[i]); Grid.SetColumn(field,i); controls.Children.Add(field); }
  status.SelectionChanged+=delegate { int n; if(status.SelectedIndex==1 && Int32.TryParse(total.Text,out n) && n>0) watched.Text=n.ToString(); };
  var plus=Btn("＋ Следующая серия",delegate { int w,t; if(Int32.TryParse(watched.Text,out w)&&Int32.TryParse(total.Text,out t)&&w>=0&&w<Int32.MaxValue&&(t==0||w<t)) { watched.Text=(w+1).ToString(); if(t>0&&w+1==t) status.SelectedIndex=1; } }); plus.HorizontalAlignment=HorizontalAlignment.Left; fields.Children.Add(plus);
  Action syncProgress=delegate { int w,t; bool valid=Int32.TryParse(watched.Text,out w)&Int32.TryParse(total.Text,out t); plus.IsEnabled=valid&&w>=0&&t>=0&&status.SelectedIndex!=1&&(t==0||w<t); if(!valid || w<0 || t<0) return; progress.Maximum=Math.Max(1,t>0?t:w); progress.BeginAnimation(ProgressBar.ValueProperty,new DoubleAnimation(Math.Min(w,progress.Maximum),TimeSpan.FromMilliseconds(240)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } }); progressLabel.Text=w+" / "+(t>0?t.ToString():"?")+" серий · "+MalImport.StatusLabels[Math.Max(0,status.SelectedIndex)]; };
  watched.TextChanged+=delegate { syncProgress(); }; total.TextChanged+=delegate { syncProgress(); }; status.SelectionChanged+=delegate { syncProgress(); }; syncProgress();
  fields.Children.Add(Text("Личная заметка",12,Muted));
  var notes=new TextBox { Text=anime.Notes,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=90,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=20000,Margin=new Thickness(0,6,0,0) }; fields.Children.Add(notes);
  var title=new TextBox { Text=anime.Title };
  var nameSection=new Expander { Header="Название в моей библиотеке",Content=title,Margin=new Thickness(0,14,0,0),Foreground=Muted }; fields.Children.Add(nameSection);
  var description=Text(anime.CatalogId>0?"Загружаю описание…":"Тайтл не связан с Shikimori.",13,Muted); description.Margin=new Thickness(0,8,0,18);
  body.Children.Add(BuildArcs(anime,window,lifetime.Token));
  body.Children.Add(Text("Об аниме",20,Ink)); body.Children.Add(description);
  var sequelRows=new StackPanel(); var sequels=new Expander { Header="Продолжения",Content=sequelRows,Foreground=Ink,Margin=new Thickness(0,0,0,12) }; body.Children.Add(sequels); bool sequelsLoaded=false;
  sequels.Expanded+=async delegate {
   if(sequelsLoaded || anime.CatalogId<=0) return; sequelsLoaded=true; sequelRows.Children.Add(Text("Загружаю…",12,Muted));
   try { var result=await Catalog.Sequels(new[]{anime.CatalogId},lifetime.Token); if(closed) return; sequelRows.Children.Clear();
    foreach(var entry in result[anime.CatalogId]) { var e=entry; var row=new DockPanel { Margin=new Thickness(0,8,0,0) }; var owned=Ratings.Find(library,e);
     var button=new Button { Content=owned==null?"＋ В планы":"Открыть",IsEnabled=!storageBlocked }; DockPanel.SetDock(button,Dock.Right); row.Children.Add(button); row.Children.Add(Text(e.Title,14,Ink)); sequelRows.Children.Add(row);
     button.Click+=delegate { try { var found=Ratings.Find(library,e); if(found!=null) OpenAnimeCard(found,window); else { Ratings.AddPlanned(library,e,dataPath); button.Content="Открыть"; Refresh(); } } catch(Exception ex) { error.Text=ex.Message; } };
    } if(sequelRows.Children.Count==0) sequelRows.Children.Add(Text("В каталоге пока нет продолжений.",12,Muted)); Reveal(sequelRows);
   } catch(Exception) { if(!closed) { sequelRows.Children.Clear(); sequelRows.Children.Add(Text("Не удалось загрузить. Сверните и раскройте раздел для повтора.",12,Muted)); sequelsLoaded=false; } }
  };
  actions.Children.Add(Btn("Сохранить",delegate { if(storageBlocked) return; int w,t; if(!Int32.TryParse(watched.Text,out w)||!Int32.TryParse(total.Text,out t)) { error.Text="Количество серий должно быть целым числом."; return; } try { CardEdits.Save(library,anime,title.Text,w,t,MalImport.StatusKeys[Math.Max(0,status.SelectedIndex)],Math.Max(0,score.SelectedIndex),notes.Text,dataPath); undo=null; Refresh(); window.Close(); } catch(Exception ex) { error.Text="Не удалось сохранить: "+ex.Message; } }));
  var cancel=Btn("Закрыть без сохранения",delegate { window.Close(); }); cancel.IsCancel=true; actions.Children.Add(cancel);
  QueueCardButton(anime,window,actions);
  actions.Children.Add(Btn("В виджет",delegate { SaveWidgetOptions(anime.Id,widgetOptions.Style); }));
  actions.Children.Add(Btn("Удалить",delegate { if(storageBlocked) return; try { new Library { Left=library.Left,Top=library.Top,Items=library.Items.Where(a=>a!=anime).ToList() }.Save(dataPath); library.Items.Remove(anime); undo=delegate { library.Items.Add(anime); }; Refresh(); window.Close(); } catch(Exception ex) { error.Text=ex.Message; } }));
  window.Loaded+=async delegate { cardScroll.Focus(); cardScroll.ResetToTop(); Reveal(hero); Reveal(sheet,65); if(anime.CatalogId<=0) return;
   try { var entries=await Catalog.ByIds(new[]{anime.CatalogId},lifetime.Token,true); if(closed) return; var entry=entries.FirstOrDefault(); description.Text=entry==null||String.IsNullOrWhiteSpace(entry.Description)?"Описание в каталоге отсутствует.":entry.Description; Reveal(description); }
   catch(Exception) { if(!closed) description.Text="Описание сейчас недоступно. Личные данные можно редактировать без интернета."; }
  };
  window.Closed+=delegate { closed=true; lifetime.Cancel(); lifetime.Dispose(); }; window.ShowDialog();
 }
}
}
