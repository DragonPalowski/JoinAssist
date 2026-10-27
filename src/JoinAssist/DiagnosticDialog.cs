using System;
using System.Windows;
using System.Windows.Controls;

namespace JoinAssist
{
    internal sealed class DiagnosticDialog : Window
    {
        readonly Func<string> read;
        readonly TextBox detail;
        readonly TextBlock notice;
        string report;
        internal DiagnosticDialog(Func<string> source):this(source,null,null){}
        internal DiagnosticDialog(Func<string> source,Action retry,Action reload)
        {
            read=source;Title="JoinAssist — Diagnostica";Width=560;Height=370;MinWidth=430;MinHeight=300;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;UiTheme.Dialog(this);
            var root=new DockPanel{Margin=new Thickness(18)};Content=root;
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            var refresh=new Button{Content="Aggiorna",Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,8,8,0)};buttons.Children.Add(refresh);refresh.Click+=(s,e)=>Refresh();
            if(retry!=null)WorkflowDialog.Button(buttons,"Riprova proposte",()=>{retry();Refresh();});
            if(reload!=null)WorkflowDialog.Button(buttons,"Ricarica metadati",()=>{reload();Refresh();});
            var copy=new Button{Content="Copia diagnostica",Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,8,0,0)};buttons.Children.Add(copy);
            notice=new TextBlock{Text="La copia contiene solo versioni e codice del problema, senza query o dettagli della connessione.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(notice,Dock.Bottom);root.Children.Add(notice);
            detail=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(10)};root.Children.Add(detail);
            copy.Click+=(s,e)=>{try{Clipboard.SetText(report);notice.Text="Diagnostica copiata.";}catch(System.Runtime.InteropServices.ExternalException){notice.Text="Appunti occupati: riprova.";}};
            Refresh();
        }
        void Refresh(){string status=read();report=Diagnostics.Report(status);detail.Text=status+Environment.NewLine+Environment.NewLine+report;}
    }
}
