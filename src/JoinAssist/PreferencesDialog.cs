using System;
using System.Windows;
using System.Windows.Controls;

namespace JoinAssist
{
    public sealed class PreferencesDialog : Window
    {
        readonly CheckBox automatic,enter,schema,quotes,aliases,uppercase,paths,insertDefaults,remember,keywords,filters;
        readonly TextBox delay,cache;
        readonly ComboBox shortcut,tolerance;
        readonly TextBlock error;
        readonly RulesEditor rules;
        readonly CheckBox quickActions;
        readonly TabControl tabs;
        public PreferencesDialog():this(null){}
        internal PreferencesDialog(Func<SsmsConnection> connection)
        {
            Title="JoinAssist — Impostazioni";Width=560;Height=485;MinWidth=490;MinHeight=420;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;UiTheme.Dialog(this);
            var root=new DockPanel{Margin=new Thickness(20)};Content=root;
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.Firebrick,Margin=new Thickness(0,8,0,8)};footer.Children.Add(error);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);
            var cancel=new Button{Content="Annulla",IsCancel=true,Padding=new Thickness(18,6,18,6),Margin=new Thickness(0,0,8,0)};buttons.Children.Add(cancel);cancel.Click+=(s,e)=>Close();
            var save=new Button{Content="Salva",IsDefault=true,Padding=new Thickness(18,6,18,6)};buttons.Children.Add(save);save.Click+=Save;
            tabs=new TabControl();root.Children.Add(tabs);
            HelpGuide.Attach(this,buttons,()=>new[]{"cache","format","cache","completion","rules"}[Math.Max(0,tabs.SelectedIndex)]);
            var body=Tab(tabs,"Completamento");
            var format=Tab(tabs,"Formato SQL");
            var metadata=Tab(tabs,"Cache");
            var search=Tab(tabs,"Ricerca");
            var personal=Tab(tabs,"Regole");
            var value=PreferenceStore.Current;
            rules=new RulesEditor(value.Rules,connection);personal.Children.Add(rules);
            automatic=Check(body,"Apri le proposte mentre scrivo",value.Automatic);
            enter=Check(body,"Accetta anche con Invio (Tab è sempre attivo)",value.AcceptEnter);
            keywords=Check(body,"Completa le parole chiave SQL con Tab (SELECT, FROM, WHERE…)",value.KeywordCompletion);
            filters=Check(body,"Prepara LIKE / NOT LIKE e IN / NOT IN con Tab, in base al tipo",value.FilterTemplates);
            quickActions=Check(body,"Abilita Ctrl+Alt+P per Azioni rapide (facoltativo)",value.QuickActionsShortcut);
            body.Children.Add(new TextBlock{Text="LIKE su testo: '%%'. IN su testo: ('', ''); su altri tipi noti: ( ,). Il cursore va nel primo valore. I valori già presenti restano invariati.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
            delay=Number(body,"Attesa prima delle proposte (70–2000 ms)",value.DelayMilliseconds);
            cache=Number(metadata,"Durata cache metadati (1–15 minuti)",value.CacheMinutes);
            body.Children.Add(new TextBlock{Text="Scorciatoia per richiamare le proposte",Margin=new Thickness(0,10,0,4)});
            shortcut=new ComboBox{ItemsSource=new[]{"Ctrl+Alt+Space","Ctrl+Shift+J","Ctrl+Alt+J"},SelectedItem=value.Shortcut,MinHeight=28};body.Children.Add(shortcut);
            metadata.Children.Add(new TextBlock{Text="I metadati restano in memoria, separati per connessione e database. Dopo una modifica allo schema usa JoinAssist → Aggiorna metadati.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0)});
            paths=Check(body,"Proponi percorsi con 2 JOIN e collegamenti tramite FK comuni",value.JoinPaths);
            body.Children.Add(new TextBlock{Text="I collegamenti tramite FK comuni inseriscono un solo JOIN tra le colonne che puntano alla stessa chiave. Sono proposte ricavate, non FK dirette, e possono dare risultati diversi dal percorso con due JOIN.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
            insertDefaults=Check(body,"Includi colonne con valore predefinito nella struttura INSERT",value.InsertIncludeDefaults);
            search.Children.Add(new TextBlock{Text="Tolleranza nella ricerca di tabelle e colonne",Margin=new Thickness(0,0,0,6)});
            tolerance=new ComboBox{ItemsSource=new[]{"Precisa","Equilibrata","Tollerante"},SelectedIndex=(int)value.SearchTolerance,MinHeight=28};search.Children.Add(tolerance);
            search.Children.Add(new TextBlock{Text="Precisa: nomi, frammenti e inizi di parola. Equilibrata: anche abbreviazioni e piccoli errori da 4 lettere, se manca un nome/prefisso diretto. Tollerante: correzioni anche con risultati diretti e da 3 lettere. Fino a due errori nei nomi più lunghi. Nomi esatti e prefissi corretti restano davanti alle correzioni.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)});
            remember=Check(search,"Ricorda le proposte che scelgo, per server e database",value.RememberChoices);
            search.Children.Add(new TextBlock{Text="La memoria resta su questo PC. Salva soltanto identificativi hash, conteggi e date delle scelte accettate; nessun testo SQL o credenziale. Disattivandola non influenza più le proposte. Azzera elimina subito tutte le scelte, anche se poi chiudi senza salvare.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
            var reset=new Button{Content="Azzera memoria delle scelte",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,6,12,6)};search.Children.Add(reset);
            reset.Click+=(s,e)=>{try{CompletionHistory.Current.Clear();error.Text="Memoria delle scelte azzerata.";}catch(Exception ex) when(ex is System.IO.IOException || ex is UnauthorizedAccessException){error.Text="Impossibile azzerare la memoria. Verifica i permessi della cartella utente.";}};
            schema=Check(format,"Inserisci sempre lo schema delle nuove tabelle",value.AlwaysSchema);
            quotes=Check(format,"Usa sempre le parentesi quadre nei nuovi nomi",value.QuoteNames);
            aliases=Check(format,"Assegna alias alle nuove tabelle dei JOIN",value.AutomaticAliases);
            uppercase=Check(format,"Scrivi le parole chiave generate in MAIUSCOLO",value.UppercaseKeywords);
            format.Children.Add(new TextBlock{Text="Le scelte valgono per tutte le schede e vengono salvate per il prossimo avvio. Schema, parentesi e alias necessari per evitare ambiguità vengono comunque mantenuti. Le query esistenti non vengono riformattate.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        }
        static StackPanel Tab(TabControl tabs,string title)
        {
            var panel=new StackPanel{Margin=new Thickness(14)};
            tabs.Items.Add(new TabItem{Header=title,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;
        }
        internal void SelectPage(string title)
        {
            foreach(TabItem tab in tabs.Items)if((string)tab.Header==title){tabs.SelectedItem=tab;break;}
        }
        static CheckBox Check(Panel body,string label,bool value) { var item=new CheckBox{Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap},IsChecked=value,Margin=new Thickness(0,7,0,7)};body.Children.Add(item);return item; }
        static TextBox Number(Panel body,string label,int value) { body.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,8,0,4)});var item=new TextBox{Text=value.ToString(System.Globalization.CultureInfo.InvariantCulture),MinHeight=26};body.Children.Add(item);return item; }
        internal Preferences Read()
        {
            int milliseconds,minutes;
            if(!int.TryParse(delay.Text,out milliseconds)||!int.TryParse(cache.Text,out minutes))throw new ArgumentException("Inserisci numeri interi per attesa e durata cache.");
            var value=new Preferences{Automatic=automatic.IsChecked==true,AcceptEnter=enter.IsChecked==true,DelayMilliseconds=milliseconds,CacheMinutes=minutes,Shortcut=shortcut.SelectedItem as string,
                AlwaysSchema=schema.IsChecked==true,QuoteNames=quotes.IsChecked==true,AutomaticAliases=aliases.IsChecked==true,UppercaseKeywords=uppercase.IsChecked==true,JoinPaths=paths.IsChecked==true,InsertIncludeDefaults=insertDefaults.IsChecked==true};
            value.SearchTolerance=(SearchTolerance)tolerance.SelectedIndex;value.RememberChoices=remember.IsChecked==true;
            value.KeywordCompletion=keywords.IsChecked==true;value.FilterTemplates=filters.IsChecked==true;
            value.Rules=rules.Read();
            value.QuickActionsShortcut=quickActions.IsChecked==true;
            value.Validate();return value;
        }
        void Save(object sender,RoutedEventArgs e)
        {
            try{PreferenceStore.Save(Read());Close();}
            catch(ArgumentException ex){error.Text=ex.Message;}
            catch(Exception ex) when(ex is System.IO.IOException || ex is UnauthorizedAccessException){error.Text="[JA-SETTINGS-02] Impossibile salvare le preferenze. Verifica i permessi della cartella utente.";}
        }
    }
}
