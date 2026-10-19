using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace JoinAssist
{
    internal sealed class StarDialog : Window
    {
        readonly List<ColumnChoice> choices;
        readonly TextBox preview;
        readonly ListBox list;
        readonly Button insert;
        internal string Insertion {get;private set;}
        internal StarDialog(List<ColumnChoice> columns)
        {
            choices=columns;Title="JoinAssist · Espandi colonne";Width=800;Height=590;MinWidth=500;MinHeight=350;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            var root=new DockPanel{Margin=new Thickness(16)};Content=root;
            var hint=new TextBlock{Text="Seleziona le colonne da inserire al posto dell'asterisco. Ordine della sorgente; alias esistenti mantenuti.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(hint,Dock.Top);root.Children.Add(hint);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            var all=new Button{Content="Tutte",Margin=new Thickness(4),Padding=new Thickness(12,5,12,5)};var none=new Button{Content="Nessuna",Margin=new Thickness(4),Padding=new Thickness(12,5,12,5)};
            insert=new Button{Content="Inserisci colonne",IsDefault=true,Margin=new Thickness(12,4,4,4),Padding=new Thickness(12,5,12,5)};var cancel=new Button{Content="Annulla",IsCancel=true,Margin=new Thickness(4),Padding=new Thickness(12,5,12,5)};
            foreach(var button in new[]{all,none,insert,cancel})buttons.Children.Add(button);
            preview=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=100,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(preview,Dock.Bottom);root.Children.Add(preview);
            list=new ListBox{ItemsSource=choices};
            var check=new FrameworkElementFactory(typeof(CheckBox));check.SetBinding(CheckBox.IsCheckedProperty,new Binding("Selected"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});check.SetBinding(CheckBox.ContentProperty,new Binding("Sql"));check.SetBinding(CheckBox.ToolTipProperty,new Binding("Description"));check.SetValue(FrameworkElement.MarginProperty,new Thickness(5));
            list.ItemTemplate=new DataTemplate{VisualTree=check};root.Children.Add(list);
            list.AddHandler(CheckBox.CheckedEvent,new RoutedEventHandler((s,e)=>Dispatcher.BeginInvoke(new Action(UpdatePreview))));list.AddHandler(CheckBox.UncheckedEvent,new RoutedEventHandler((s,e)=>Dispatcher.BeginInvoke(new Action(UpdatePreview))));
            all.Click+=(s,e)=>Select(true);none.Click+=(s,e)=>Select(false);
            insert.Click+=(s,e)=>{Insertion=StarExpansion.Text(choices);DialogResult=true;};
            UiTheme.Dialog(this);UpdatePreview();
        }
        void Select(bool selected){foreach(var choice in choices)choice.Selected=selected;list.Items.Refresh();UpdatePreview();}
        void UpdatePreview(){int count=choices.Count(c=>c.Selected);insert.IsEnabled=count>0;preview.Text=count==0?"Seleziona almeno una colonna.":StarExpansion.Text(choices);}
    }
    internal sealed class ObjectInfoDialog : Window
    {
        internal ObjectInfoDialog(List<ObjectInfo> objects)
        {
            Title="JoinAssist · Tabelle e colonne";Width=820;Height=540;MinWidth=500;MinHeight=300;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            var root=new DockPanel{Margin=new Thickness(16)};Content=root;
            var select=new ComboBox{ItemsSource=objects,DisplayMemberPath="Name",Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(select,Dock.Top);root.Children.Add(select);
            var close=new Button{Content="Chiudi",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(16,5,16,5),Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
            var tabs=new TabControl();root.Children.Add(tabs);
            var columns=new ListBox{HorizontalContentAlignment=HorizontalAlignment.Stretch};
            var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(FrameworkElement.MarginProperty,new Thickness(8,5,8,5));
            var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Sql"));name.SetValue(TextBlock.FontWeightProperty,FontWeights.Medium);name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);row.AppendChild(name);
            var detail=new FrameworkElementFactory(typeof(TextBlock));detail.SetBinding(TextBlock.TextProperty,new Binding("Description"));detail.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);row.AppendChild(detail);
            columns.ItemTemplate=new DataTemplate{VisualTree=row};
            var relationships=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(8)};
            tabs.Items.Add(new TabItem{Header="Colonne",Content=columns});tabs.Items.Add(new TabItem{Header="Relazioni FK",Content=relationships});
            select.SelectionChanged+=(s,e)=>{var item=select.SelectedItem as ObjectInfo;columns.ItemsSource=item?.Columns;relationships.Text=item?.Relationships??"";};
            if(objects.Count>0)select.SelectedIndex=0;
            close.Click+=(s,e)=>Close();UiTheme.Dialog(this);
        }
    }
}
