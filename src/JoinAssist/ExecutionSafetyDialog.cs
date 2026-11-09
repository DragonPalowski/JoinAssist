using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace JoinAssist
{
    public sealed class ExecutionSafetyDialog : Window
    {
        internal ExecutionFinding Navigate;
        internal ExecutionSafetyDialog(ExecutionReview review,bool selected)
        {
            Title="JoinAssist — Controllo prima dell'esecuzione";Width=640;Height=425;MinWidth=490;MinHeight=330;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;UiTheme.Dialog(this);
            var root=new DockPanel{Margin=new Thickness(20)};Content=root;
            var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            var cancel=new Button{Content="Annulla",IsCancel=true,IsDefault=true,Padding=new Thickness(15,7,15,7),Margin=new Thickness(8)};footer.Children.Add(cancel);cancel.Click+=(s,e)=>{DialogResult=false;};
            var run=new Button{Content="Esegui comunque",Padding=new Thickness(15,7,15,7),Margin=new Thickness(8)};footer.Children.Add(run);run.Click+=(s,e)=>{DialogResult=true;};
            HelpGuide.Attach(this,footer,()=>"execution-safety");
            var heading=new TextBlock{Text=review.Findings.Count>0?"Attenzione: "+review.Findings.Count+" istruzioni UPDATE/DELETE senza WHERE":"Impossibile completare il controllo della query",FontSize=17,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
            var description=new TextBlock{Text=(selected?"Controllato il testo selezionato.":"Controllata l'intera scheda SQL.")+" Verifica le istruzioni prima di continuare. TOP, JOIN e transazioni non eliminano questo avviso."+(review.Uncertainty==null?"":"\n"+review.Uncertainty),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(description,Dock.Top);root.Children.Add(description);
            var go=new Button{Content="Vai all'istruzione e annulla",Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,8),IsEnabled=review.Findings.Count>0};DockPanel.SetDock(go,Dock.Bottom);root.Children.Add(go);
            var list=new ListBox{ItemsSource=review.Findings.Take(200).ToList(),SelectedIndex=review.Findings.Count>0?0:-1};root.Children.Add(list);UiTheme.Surface(list);
            var row=new Style(typeof(ListBoxItem),list.ItemContainerStyle??list.TryFindResource(typeof(ListBoxItem)) as Style);
            row.Setters.Add(new Setter(Control.BackgroundProperty,System.Windows.Media.Brushes.Transparent));
            row.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey)));
            var chosen=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};chosen.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension(SystemColors.HighlightBrushKey)));chosen.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(SystemColors.HighlightTextBrushKey)));row.Triggers.Add(chosen);list.ItemContainerStyle=row;
            go.Click+=(s,e)=>{Navigate=list.SelectedItem as ExecutionFinding;DialogResult=false;};
            Loaded+=(s,e)=>cancel.Focus();
        }
    }
}
