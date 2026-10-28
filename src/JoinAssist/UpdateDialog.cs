using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace JoinAssist
{
    internal sealed class UpdateDialog : Window
    {
        readonly UpdateClient client = new UpdateClient();
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly TextBlock status;
        readonly TextBox notes;
        readonly Button check, download, folder;
        readonly Button schedule,unschedule;
        readonly TextBlock pendingStatus;
        readonly DeferredScheduler scheduler=new DeferredScheduler();
        readonly DispatcherTimer pendingTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        DeferredPlan pending;
        bool inspecting;
        UpdateRelease release;
        string downloaded;
        bool closed, busy;

        internal UpdateDialog()
        {
            Title = "JoinAssist — Aggiornamenti";
            Width = 700; Height = 590; MinWidth = 540; MinHeight = 470;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;UiTheme.Dialog(this);
            var grid = new Grid { Margin = new Thickness(18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = grid;
            var heading = new TextBlock { Text = "Versione installata: " + UpdateClient.DisplayVersion(UpdateClient.InstalledVersion) +
                "\nFonte: github.com/DragonPalowski/JoinAssist", Margin = new Thickness(0,0,0,12), TextWrapping = TextWrapping.Wrap };
            grid.Children.Add(heading);
            status = new TextBlock { Text = "Controllo aggiornamenti…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
            Grid.SetRow(status, 1); grid.Children.Add(status);
            notes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8), Text = "Le note della release verranno mostrate qui come testo." };
            Grid.SetRow(notes, 2); grid.Children.Add(notes);
            pendingStatus=new TextBlock{Text="Nessuna installazione in attesa.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
            Grid.SetRow(pendingStatus,3);grid.Children.Add(pendingStatus);
            var hint = new TextBlock { Text = "GitHub viene contattato solo su richiesta, senza query o dati del database.\n«Installa alla prossima chiusura» scarica e verifica il pacchetto, poi attende tutte le istanze SSMS (massimo 24 ore). Non chiude il programma. L'installer Microsoft richiederà i normali passaggi di conferma.",
                Margin = new Thickness(0,12,0,12), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(hint, 4); grid.Children.Add(hint);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            HelpGuide.Attach(this,buttons,()=>"updates");
            check = MakeButton("Ricontrolla", async (s,e) => await CheckAsync());
            download = MakeButton("Scarica aggiornamento", async (s,e) => await DownloadAsync());
            folder = MakeButton("Apri cartella", (s,e) => OpenFolder());
            var page = MakeButton("Release su GitHub", (s,e) => OpenPage());
            schedule=MakeButton("Installa alla prossima chiusura",async(s,e)=>await ScheduleAsync());
            unschedule=MakeButton("Annulla installazione in attesa",async(s,e)=>await UnscheduleAsync());
            buttons.Children.Add(check); buttons.Children.Add(download); buttons.Children.Add(folder); buttons.Children.Add(page);
            buttons.Children.Add(schedule);buttons.Children.Add(unschedule);
            Grid.SetRow(buttons, 5); grid.Children.Add(buttons);
            download.IsEnabled = folder.IsEnabled = schedule.IsEnabled=unschedule.IsEnabled=false;
            Loaded += async (s,e) => {await RefreshPendingAsync();await CheckAsync();if(!closed)pendingTimer.Start();};
            pendingTimer.Tick+=async(s,e)=>{if(!busy)await RefreshPendingAsync();};
            Closed += (s,e) => { closed = true;pendingTimer.Stop(); cancel.Cancel(); client.Dispose(); cancel.Dispose(); };
        }

        static Button MakeButton(string text, RoutedEventHandler click)
        {
            var button = new Button { Content = text, Margin = new Thickness(4,0,0,4), Padding = new Thickness(10,5,10,5) };
            button.Click += click; return button;
        }

        void SetBusy(bool value)
        {
            busy = value; check.IsEnabled = !value;
            download.IsEnabled = !value && release != null && release.Version > UpdateClient.InstalledVersion && downloaded == null;
            folder.IsEnabled = !value && downloaded != null;
            bool available=release!=null && release.Version>UpdateClient.InstalledVersion;
            bool waiting=pending!=null && pending.Pending;
            schedule.IsEnabled=!value && available && !waiting && !(pending!=null && pending.State=="Launched" && pending.Version==UpdateClient.DisplayVersion(release.Version));
            unschedule.IsEnabled=!value && pending!=null && (pending.State=="Waiting" || pending.State=="Preparing");
            if(waiting)download.IsEnabled=false;
            if(!value && pending!=null && File.Exists(pending.Package))folder.IsEnabled=true;
        }

        async Task CheckAsync()
        {
            if (closed || busy) return;
            release = null; downloaded = null; SetBusy(true);
            status.Text = "Controllo dell'ultima release pubblica su GitHub…";
            try
            {
                var result = await client.CheckAsync(cancel.Token);
                if (closed) return;
                release = result;
                if (result == null) { status.Text = "Non ci sono release pubblicate."; notes.Text = ""; }
                else
                {
                    status.Text = result.Version > UpdateClient.InstalledVersion ? "Disponibile JoinAssist " + result.Tag.TrimStart('v') + ". Puoi scaricarlo qui." :
                        "JoinAssist è aggiornato. Ultima release pubblica: " + result.Tag.TrimStart('v') + ".";
                    notes.Text = result.Notes;
                }
            }
            catch (Exception ex) { if (!closed) ShowError(ex); }
            finally { if (!closed) SetBusy(false); }
        }

        async Task DownloadAsync()
        {
            if (closed || busy || release == null || release.Version <= UpdateClient.InstalledVersion || downloaded != null) return;
            SetBusy(true); status.Text = "Download in corso…";
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JoinAssist", "Updates");
                string file = await client.DownloadAsync(release, root, cancel.Token, new Progress<int>(p => { if (!closed) status.Text = "Download: " + p + "% — verifica SHA-256 al termine"; }));
                if (closed) return;
                downloaded = file;
                status.Text = "Scaricato e verificato. Salva le query, chiudi tutte le finestre di SSMS e apri " + release.FileName + ".";
                notes.Text = "File verificato:\n" + file + "\n\nSHA-256: " + release.Sha256 + "\n\n" + release.Notes;
            }
            catch (Exception ex) { if (!closed) ShowError(ex); }
            finally { if (!closed) SetBusy(false); }
        }
        async Task RefreshPendingAsync()
        {
            if(closed || inspecting)return;inspecting=true;
            try{var result=await scheduler.InspectAsync();if(!closed){pending=result;pendingStatus.Text=DeferredScheduler.Describe(pending);SetBusy(busy);}}
            catch(Exception ex){if(!closed)pendingStatus.Text="Impossibile leggere l'attesa dell'aggiornamento ("+ex.GetType().Name+"). Il download manuale resta disponibile.";}
            finally{inspecting=false;}
        }
        async Task ScheduleAsync()
        {
            if(closed || busy || release==null || release.Version<=UpdateClient.InstalledVersion || pending!=null && pending.Pending)return;
            if(downloaded==null)await DownloadAsync();
            if(closed || busy || downloaded==null)return;
            SetBusy(true);status.Text="Preparazione dell'installazione alla chiusura…";
            try
            {
                var plan=await scheduler.ScheduleAsync(release,downloaded,cancel.Token);
                if(!closed){pending=plan;pendingStatus.Text=DeferredScheduler.Describe(plan);status.Text="Aggiornamento programmato. Puoi chiudere questa finestra e continuare a lavorare.";}
            }
            catch(Exception ex){if(!closed)ShowError(ex);}
            finally{if(!closed)SetBusy(false);}
        }
        async Task UnscheduleAsync()
        {
            if(closed || busy || pending==null)return;SetBusy(true);
            try
            {
                bool stopped=await scheduler.CancelAsync(pending.Id);
                if(!closed){status.Text=stopped?"Installazione alla chiusura annullata.":"L'installer è già in avvio oppure l'attesa è terminata.";await RefreshPendingAsync();}
            }
            catch(Exception ex){if(!closed)ShowError(ex);}
            finally{if(!closed)SetBusy(false);}
        }

        void ShowError(Exception error)
        {
            status.Text = error is OperationCanceledException ? "Operazione scaduta o annullata. Riprova o apri le release su GitHub." :
                error is InvalidOperationException || error is InvalidDataException ? error.Message : error is HttpRequestException ? "Impossibile contattare GitHub. Verifica la connessione e riprova." :
                error is IOException || error is UnauthorizedAccessException ? "Impossibile salvare il download nella cartella locale. Verifica spazio e permessi." :
                "Impossibile completare l'aggiornamento (" + error.GetType().Name + "). Apri le release su GitHub.";
        }

        void OpenPage()
        {
            try { Process.Start(new ProcessStartInfo(release == null ? UpdateClient.Repository + "/releases" : release.Page.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        }

        void OpenFolder()
        {
            string file=downloaded??pending?.Package;if(file==null)return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        }
    }
}
