using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JoinAssist
{
    internal sealed class DeletePlanDialog:Window
    {
        internal DeletePlanDialog(DeletePlan plan)
        {
            Title="JoinAssist · DELETE con dipendenze";Width=940;Height=700;MinWidth=600;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            var root=new DockPanel{Margin=new Thickness(16)};Content=root;
            var heading=new TextBlock{Text="Anteprima dello script · nessuna DELETE eseguita dal plugin",FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
            DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
            var hint=new TextBlock{Text="Lo script fissa le righe selezionate, elimina prima i figli e termina con ROLLBACK per una prova. Controlla filtro e dipendenze; inserire lo script sostituisce solo la DELETE corrente. Nessun conteggio preventivo sui dati.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
            DockPanel.SetDock(hint,Dock.Top);root.Children.Add(hint);
            var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            var cancel=new Button{Content="Annulla",IsCancel=true,Padding=new Thickness(16,6,16,6),Margin=new Thickness(4)};
            HelpGuide.Attach(this,footer,()=>"delete");
            var settings=new Button{Content="Impostazioni DELETE…",Padding=new Thickness(12,6,12,6),Margin=new Thickness(4)};
            footer.Children.Add(settings);settings.Click+=(s,e)=>{var dialog=new PreferencesDialog();dialog.SelectPage("DELETE");dialog.Owner=this;dialog.ShowDialog();};
            var insert=new Button{Content="Inserisci script di prova",IsEnabled=plan.Ready,Padding=new Thickness(16,6,16,6),Margin=new Thickness(4)};
            footer.Children.Add(cancel);footer.Children.Add(insert);cancel.Click+=(s,e)=>Close();insert.Click+=(s,e)=>DialogResult=true;
            var summary=new TextBlock{Text=plan.Summary,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
            DockPanel.SetDock(summary,Dock.Top);root.Children.Add(summary);
            var messages=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=110,
                Text=string.Join(Environment.NewLine,plan.Problems.Concat(plan.Notices).Distinct()),Margin=new Thickness(0,0,0,8),Visibility=plan.Problems.Count+plan.Notices.Count==0?Visibility.Collapsed:Visibility.Visible};
            DockPanel.SetDock(messages,Dock.Top);root.Children.Add(messages);
            var tabs=new TabControl();root.Children.Add(tabs);
            var details=new DockPanel{Margin=new Thickness(10)};
            details.Children.Add(new ListBox{ItemsSource=plan.Steps,DisplayMemberPath="Caption",HorizontalContentAlignment=HorizontalAlignment.Stretch});
            tabs.Items.Add(new TabItem{Header="Dipendenze e ordine",Content=details});
            tabs.Items.Add(new TabItem{Header="Script SQL",Content=new TextBox{IsReadOnly=true,AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),FontSize=12,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Text=plan.Script??"Piano non generato. Consulta i motivi nella scheda Dipendenze e ordine.",Margin=new Thickness(10)}});
            tabs.Items.Add(new TabItem{Header="Tabelle e percorsi",Content=Exploration(plan)});
            if(!plan.Ready){heading.Text="Piano non generato · risolvere i casi segnalati prima di procedere";hint.Text="Consulta tabelle, percorsi e motivi del blocco. Nessuno script parziale; la DELETE originale resta invariata. Dopo aver salvato nuovi limiti, chiudi e riapri il comando per ricalcolare.";if(plan.Dependencies.Count>0)tabs.SelectedIndex=2;}
            UiTheme.Dialog(this);
        }
        static FrameworkElement Exploration(DeletePlan plan)
        {
            var root=new DockPanel{Margin=new Thickness(10)};
            var hint=new TextBlock{Text="Dipendenze strutturali NO ACTION / CASCADE dalla tabella iniziale alle figlie. L'elenco non conta le righe del WHERE e non è un ordine di cancellazione. Seleziona una tabella per il percorso minimo e tutte le sue FK entranti nell'elenco esplorato.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
            DockPanel.SetDock(hint,Dock.Top);root.Children.Add(hint);
            var label=new TextBlock{Text="Filtra tabelle per nome o FK del percorso",Margin=new Thickness(0,0,0,4)};DockPanel.SetDock(label,Dock.Top);root.Children.Add(label);
            var filter=new TextBox{Name="DeleteTableFilter",MinHeight=26,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(filter,Dock.Top);root.Children.Add(filter);
            var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(5)});grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.Children.Add(grid);
            var list=new ListBox{Name="DeleteDependencies",ItemsSource=plan.Dependencies,DisplayMemberPath="Caption"};grid.Children.Add(list);
            var splitter=new GridSplitter{Height=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,ResizeDirection=GridResizeDirection.Rows};Grid.SetRow(splitter,1);grid.Children.Add(splitter);
            var detail=new TextBox{Name="DeleteDependencyDetails",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Text=plan.Describe(null)};Grid.SetRow(detail,2);grid.Children.Add(detail);
            list.SelectionChanged+=(s,e)=>detail.Text=plan.Describe(list.SelectedItem as DeleteDependency);
            filter.TextChanged+=(s,e)=>{string text=filter.Text.Trim();var selected=list.SelectedItem;list.ItemsSource=plan.Dependencies.Where(d=>d.Table.Sql.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0 || d.Via!=null && (d.Via.Key.Name??"").IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0).ToList();list.SelectedItem=selected;if(list.SelectedIndex<0 && list.Items.Count>0)list.SelectedIndex=0;};
            if(list.Items.Count>0)list.SelectedIndex=0;
            return root;
        }
    }
}
