using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace JoinAssist
{
    internal sealed class UninstallDialog : Window
    {
        readonly Func<Task<RemovalTarget>> discover;
        readonly Func<Task<bool>> updatePending;
        readonly Action<RemovalTarget> launch;
        readonly TextBlock status;
        readonly Button remove, retry;
        bool busy, closed, launched;
        RemovalTarget prepared;
        internal UninstallDialog() : this(ExtensionRemoval.DiscoverAsync, async () =>
        {
            var p = await new DeferredScheduler().InspectAsync();
            return p != null && p.Pending;
        }, ExtensionRemoval.Launch)
        {
        }

        internal UninstallDialog(Func<Task<RemovalTarget>> read, Func<Task<bool>> pending, Action<RemovalTarget> start)
        {
            discover = read;
            updatePending = pending;
            launch = start;
            Title = "JoinAssist — Disinstalla";
            Width = 620;
            Height = 420;
            MinWidth = 490;
            MinHeight = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UiTheme.Dialog(this);
            var root = new DockPanel
            {
                Margin = new Thickness(20)
            };
            Content = root;
            var buttons = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            HelpGuide.Attach(this, buttons, () => "uninstall");
            retry = WorkflowDialog.Button(buttons, "Riprova", () => Run(false));
            remove = WorkflowDialog.Button(buttons, "Avvia disinstallazione", () => Run(true));
            remove.IsEnabled = false;
            WorkflowDialog.Button(buttons, "Chiudi", Close);
            var body = new StackPanel();
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            body.Children.Add(new TextBlock { Text = "Rimuovi JoinAssist da SSMS", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
            body.Children.Add(new TextBlock { Text = "Salva le query aperte. Il pulsante avvia l'installer Microsoft delle estensioni per togliere soltanto JoinAssist da questa installazione di SSMS. Completa la procedura e chiudi SSMS quando richiesto dall'installer. JoinAssist non chiude il programma né conferma i passaggi al posto tuo.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            body.Children.Add(new TextBlock { Text = "Regole, impostazioni e preferiti locali vengono conservati. Se hai un aggiornamento JoinAssist in attesa, annullalo da Aggiornamenti… prima di procedere, poi premi Riprova.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            status = new TextBlock
            {
                Text = "Identificazione dell'installazione SSMS…",
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(status);
            Loaded += (s, e) => Run(false);
            Closed += (s, e) => closed = true;
        }

        async void Run(bool uninstall)
        {
            await PrepareAsync(uninstall);
        }

        internal async Task PrepareAsync(bool uninstall)
        {
            if (busy || closed || launched)
                return;
            busy = true;
            remove.IsEnabled = retry.IsEnabled = false;
            try
            {
                if (await updatePending())
                {
                    if (!closed)
                        status.Text = "Un aggiornamento JoinAssist è in attesa o in avvio. Annullalo da Aggiornamenti… e premi Riprova prima di disinstallare.";
                    return;
                }

                var target = await discover();
                if (closed)
                    return;
                // Discover again for the actual click; never reuse a stale installation target.
                if (uninstall)
                {
                    if (prepared == null || prepared.InstanceId != target.InstanceId || prepared.AllUsers != target.AllUsers || !string.Equals(prepared.Host, target.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        prepared = null;
                        status.Text = "La destinazione o l'ambito sono cambiati. Premi Riprova e controlla la nuova destinazione prima di disinstallare.";
                        return;
                    }

                    if (await updatePending())
                    {
                        if (!closed)
                            status.Text = "È stato programmato un aggiornamento: annullalo prima di disinstallare.";
                        return;
                    }

                    if (closed)
                        return;
                    launch(target);
                    launched = true;
                    status.Text = "Procedura Microsoft avviata. Completa la disinstallazione e chiudi SSMS quando richiesto. L'esito finale viene mostrato dall'installer.";
                }
                else
                {
                    prepared = target;
                    status.Text = "Destinazione: " + target.Host + "\n" + (target.AllUsers ? "JoinAssist è installato per tutti gli utenti: la rimozione riguarda tutti gli utenti di questa installazione SSMS e può richiedere privilegi amministrativi." : "Rimozione dell'estensione per l'utente corrente.") + "\nPronto per aprire la procedura Microsoft.";
                    remove.IsEnabled = true;
                }
            }
            catch (Exception ex)when (ex is InvalidOperationException || ex is ArgumentException || ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is System.Security.SecurityException)
            {
                if (!closed)
                    status.Text = "Disinstallazione non avviata. " + (ex is InvalidOperationException ? ex.Message : "Verifica i permessi e l'installazione di SSMS, poi riprova.");
            }
            finally
            {
                busy = false;
                if (!closed)
                    retry.IsEnabled = !launched;
            }
        }
    }
}
