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
            var insert=new Button{Content="Inserisci script di prova",IsEnabled=plan.Ready,Padding=new Thickness(16,6,16,6),Margin=new Thickness(4)};
            footer.Children.Add(cancel);footer.Children.Add(insert);cancel.Click+=(s,e)=>Close();insert.Click+=(s,e)=>DialogResult=true;
            var tabs=new TabControl();root.Children.Add(tabs);
            var details=new DockPanel{Margin=new Thickness(10)};
            var messages=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=150,
                Text=string.Join(Environment.NewLine,plan.Problems.Concat(plan.Notices).Distinct()),Margin=new Thickness(0,0,0,8),Visibility=plan.Problems.Count+plan.Notices.Count==0?Visibility.Collapsed:Visibility.Visible};
            DockPanel.SetDock(messages,Dock.Top);details.Children.Add(messages);
            details.Children.Add(new ListBox{ItemsSource=plan.Steps,DisplayMemberPath="Caption",HorizontalContentAlignment=HorizontalAlignment.Stretch});
            tabs.Items.Add(new TabItem{Header="Dipendenze e ordine",Content=details});
            tabs.Items.Add(new TabItem{Header="Script SQL",Content=new TextBox{IsReadOnly=true,AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas"),FontSize=12,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Text=plan.Script??"Piano non generato. Consulta i motivi nella scheda Dipendenze e ordine.",Margin=new Thickness(10)}});
            if(!plan.Ready){heading.Text="Piano non generato · risolvere i casi segnalati prima di procedere";hint.Text="Consulta i motivi riportati sotto. Non viene proposto uno script parziale e la DELETE originale resta invariata.";}
            UiTheme.Dialog(this);
        }
    }
}
