using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace JoinAssist
{
    internal sealed class UpdateDialog : Window
    {
        readonly UpdateClient client = new UpdateClient();
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        readonly TextBlock status;
        readonly TextBox notes;
        readonly Button check, download, folder;
        UpdateRelease release;
        string downloaded;
        bool closed, busy;

        internal UpdateDialog()
        {
            Title = "JoinAssist — Aggiornamenti";
            Width = 640; Height = 490; MinWidth = 540; MinHeight = 390;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;UiTheme.Dialog(this);
            var grid = new Grid { Margin = new Thickness(18) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
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
            var hint = new TextBlock { Text = "Il controllo contatta GitHub solo su tua richiesta. Non invia query o dati del database.\nDopo il download: salva le query, chiudi tutte le finestre di SSMS e apri il file VSIX.",
                Margin = new Thickness(0,12,0,12), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(hint, 3); grid.Children.Add(hint);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            check = MakeButton("Ricontrolla", async (s,e) => await CheckAsync());
            download = MakeButton("Scarica aggiornamento", async (s,e) => await DownloadAsync());
            folder = MakeButton("Apri cartella", (s,e) => OpenFolder());
            var page = MakeButton("Release su GitHub", (s,e) => OpenPage());
            buttons.Children.Add(check); buttons.Children.Add(download); buttons.Children.Add(folder); buttons.Children.Add(page);
            Grid.SetRow(buttons, 4); grid.Children.Add(buttons);
            download.IsEnabled = folder.IsEnabled = false;
            Loaded += async (s,e) => await CheckAsync();
            Closed += (s,e) => { closed = true; cancel.Cancel(); client.Dispose(); cancel.Dispose(); };
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

        void ShowError(Exception error)
        {
            status.Text = error is OperationCanceledException ? "Operazione scaduta o annullata. Riprova o apri le release su GitHub." :
                error is InvalidOperationException ? error.Message : error is HttpRequestException ? "Impossibile contattare GitHub. Verifica la connessione e riprova." :
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
            if (downloaded == null) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + downloaded + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        }
    }
}
