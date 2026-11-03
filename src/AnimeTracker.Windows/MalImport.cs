using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AnimeTracker {
public partial class Tracker {
 async void ImportMal() {
  if(storageBlocked) { ShowMessage("Сначала восстановите повреждённую библиотеку. Импорт не выполнялся."); return; }
  var picker=new Microsoft.Win32.OpenFileDialog { Title="Экспорт Anime List из MyAnimeList", Filter="MyAnimeList (*.xml;*.gz)|*.xml;*.gz", CheckFileExists=true };
  if(picker.ShowDialog(full)!=true) return;
  List<Anime> incoming;
  try { incoming=await System.Threading.Tasks.Task.Run(()=>MalImport.Read(picker.FileName)); }
  catch(Exception ex) { ShowMessage("Не удалось прочитать экспорт. Библиотека не изменена.\n"+ex.Message,"Импорт MyAnimeList"); return; }
  List<ImportRow> rows;
  try { rows=MalImport.Preview(library,incoming); } catch(Exception ex) { ShowMessage(ex.Message,"Импорт MyAnimeList"); return; }
  var dialog=new Window { Title="Импорт MyAnimeList — предварительный просмотр", Width=920, Height=620, MinWidth=640, MinHeight=420, Owner=full, WindowStartupLocation=WindowStartupLocation.CenterOwner };
  StyleWindow(dialog);
  var panel=new DockPanel { Margin=new Thickness(20) }; dialog.Content=panel;
  var heading=new StackPanel(); heading.Children.Add(Text("Импорт из MyAnimeList",24,Ink));
  int added=rows.Count(x=>x.Existing==null);
  heading.Children.Add(Text("В файле: "+rows.Count+" · новых: "+added+" · совпадений: "+(rows.Count-added),14,Muted));
  heading.Children.Add(Text(String.Join(" · ",incoming.GroupBy(x=>x.Status).Select(g=>MalImport.StatusLabel(g.Key)+": "+g.Count())),12,Muted));
  var overwrite=new CheckBox { Content="Для совпадений заменить статус на статус из XML", Foreground=Ink, Margin=new Thickness(0,12,0,8) };
  heading.Children.Add(overwrite); heading.Children.Add(Text("У новых записей сохраняются статусы XML. У совпадений по умолчанию остаётся местный статус; число просмотренных серий никогда не уменьшается. Оригинал экспорта и копия библиотеки сохранятся перед импортом.",12,Muted));
  heading.Margin=new Thickness(0,0,0,14); DockPanel.SetDock(heading,Dock.Top); panel.Children.Add(heading);
  var actions=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,14,0,0) };
  var import=Btn("Импортировать "+rows.Count+" записей",delegate {
   try {
    // Rebuild against current state so a concurrent widget increment is never lost.
    var result=service.CommitMal(picker.FileName,incoming,overwrite.IsChecked==true);
    string backup=result.Backup;
    undo=null; Refresh(); dialog.Close();
    ShowMessage("Импорт завершён. Новых записей: "+result.Added+". Совпадений: "+result.Matched+".\n\nИсходный экспорт и резервная копия:\n"+backup,"Импорт MyAnimeList");
   } catch(Exception ex) { ShowMessage("Импорт не завершён.\n"+ex.Message,"Импорт MyAnimeList"); }
  }); actions.Children.Add(import); var cancel=Btn("Отмена",delegate { dialog.Close(); }); cancel.IsCancel=true; actions.Children.Add(cancel); DockPanel.SetDock(actions,Dock.Bottom); panel.Children.Add(actions);
  var grid=new DataGrid { AutoGenerateColumns=false, IsReadOnly=true, CanUserAddRows=false, ItemsSource=rows, EnableRowVirtualization=true, HeadersVisibility=DataGridHeadersVisibility.Column, Background=ThemeBrush("#FFFFFF"), RowBackground=ThemeBrush("#FFFAF0"), Foreground=Ink, GridLinesVisibility=DataGridGridLinesVisibility.Horizontal };
  foreach(var column in new[]{new[]{"Аниме","Title"},new[]{"Статус XML","MalStatus"},new[]{"Серии XML","Progress"},new[]{"В библиотеке","Local"},new[]{"Примечание","Note"}}) grid.Columns.Add(new DataGridTextColumn { Header=column[0],Binding=new Binding(column[1]),Width=column[1]=="Title"?new DataGridLength(2,DataGridLengthUnitType.Star):new DataGridLength(1,DataGridLengthUnitType.Star) });
  panel.Children.Add(grid); dialog.ShowDialog();
 }
}
}
