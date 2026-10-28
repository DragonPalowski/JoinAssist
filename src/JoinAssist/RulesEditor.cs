using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading;
using Microsoft.Win32;

namespace JoinAssist
{
    internal sealed class RulesEditor : DockPanel
    {
        readonly List<PersonalRule> rules;
        readonly ListBox list;
        readonly TextBlock status;
        readonly Func<SsmsConnection> capture;
        internal RulesEditor(IEnumerable<PersonalRule> source,Func<SsmsConnection> connection)
        {
            capture=connection;rules=source.Select(r=>r.Copy()).ToList();LastChildFill=true;
            var description=new TextBlock{Text="TOP e ordinamento per tabella, oppure scorciatoie SQL. Tab applica la proposta; Spazio è opzionale per ogni regola. Le modifiche diventano attive solo con Salva nelle impostazioni.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};SetDock(description,Dock.Top);Children.Add(description);
            var buttons=new WrapPanel{Margin=new Thickness(0,0,0,8)};SetDock(buttons,Dock.Top);Children.Add(buttons);
            Add(buttons,"Nuova SELECT",()=>Edit(null,PersonalRuleKind.SelectTable));Add(buttons,"Nuova scorciatoia",()=>Edit(null,PersonalRuleKind.Snippet));
            Add(buttons,"Modifica",()=>Edit(list.SelectedItem as PersonalRule,PersonalRuleKind.SelectTable,true));
            Add(buttons,"Prova…",()=>{var rule=list.SelectedItem as PersonalRule;if(rule==null)return;new RuleProbeDialog(rule.Copy(),capture){Owner=Window.GetWindow(this)}.ShowDialog();});
            Add(buttons,"Duplica",()=>{var r=list.SelectedItem as PersonalRule;if(r==null)return;var clone=r.Copy();clone.Id=Guid.NewGuid().ToString("N");clone.Name=("Copia di "+r.Name);if(clone.Name.Length>100)clone.Name=clone.Name.Substring(0,100);clone.AutoOnSpace=false;Edit(clone,clone.Kind);});
            Add(buttons,"Abilita / disabilita",()=>{var r=list.SelectedItem as PersonalRule;if(r!=null){r.Enabled=!r.Enabled;Refresh(r);}});
            Add(buttons,"Elimina",()=>{var r=list.SelectedItem as PersonalRule;if(r!=null){rules.Remove(r);Refresh();}});
            Add(buttons,"Importa…",Import);Add(buttons,"Esporta…",Export);
            status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};SetDock(status,Dock.Bottom);Children.Add(status);
            list=new ListBox{MinHeight=110,MaxHeight=210};Children.Add(list);list.MouseDoubleClick+=(s,e)=>{if(list.SelectedItem!=null)Edit(list.SelectedItem as PersonalRule,PersonalRuleKind.SelectTable,true);};Refresh();
        }
        void Add(Panel panel,string text,Action action)
        {var button=new Button{Content=text,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,5,5)};panel.Children.Add(button);button.Click+=(s,e)=>{try{action();}catch(Exception ex) when(ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException){status.Text=ex.Message;}};}
        void Edit(PersonalRule value,PersonalRuleKind kind,bool existing=false)
        {
            if(existing && value==null)return;
            var dialog=new RuleEditDialog(value??new PersonalRule{Kind=kind},capture,value==null);dialog.Owner=Window.GetWindow(this);
            if(dialog.ShowDialog()!=true)return;
            var updated=dialog.Read();
            var candidate=rules.Where(r=>!existing || r.Id!=value.Id).Select(r=>r.Copy()).ToList();candidate.Add(updated);PersonalRules.Validate(candidate);
            if(existing)rules.Remove(value);rules.Add(updated);Refresh(updated);
        }
        void Refresh(PersonalRule selected=null)
        {list.ItemsSource=null;list.ItemsSource=rules;list.SelectedItem=selected;status.Text=rules.Count+" / 50 regole. Ambito più specifico prioritario; alternative di pari ambito restano separate. Un conflitto impedisce l'applicazione automatica con Spazio.";}
        internal List<PersonalRule> Read(){PersonalRules.Validate(rules);return rules.Select(r=>r.Copy()).ToList();}
        void Import()
        {
            var file=new OpenFileDialog{Filter="Regole JoinAssist (*.json)|*.json",Title="Importa regole personali"};if(file.ShowDialog(Window.GetWindow(this))!=true)return;
            if(new FileInfo(file.FileName).Length>1048576)throw new ArgumentException("File troppo grande (massimo 1 MiB).");
            var imported=PersonalRules.Import(File.ReadAllText(file.FileName));var all=rules.Concat(imported).ToList();PersonalRules.Validate(all);rules.AddRange(imported);Refresh();status.Text="Importate "+imported.Count+" regole disattivate. Controllale e abilita quelle desiderate; Salva le impostazioni per attivarle.";
        }
        void Export()
        {var file=new SaveFileDialog{Filter="Regole JoinAssist (*.json)|*.json",FileName="JoinAssist-regole.json",Title="Esporta regole (contiene SQL e ambiti personali)"};if(file.ShowDialog(Window.GetWindow(this))==true){File.WriteAllText(file.FileName,PersonalRules.Export(Read()));status.Text="Regole esportate. Il file contiene il testo SQL configurato e gli ambiti server/database.";}}
    }
    internal sealed class RuleEditDialog : Window
    {
        readonly PersonalRule original;
        readonly Func<SsmsConnection> capture;
        readonly TextBox name,server,database,schema,table,top,shortcut,sql;
        readonly ComboBox order,column;
        readonly CheckBox enabled,automatic,descending;
        readonly CheckBox linkedFields;
        readonly TextBox preview;
        readonly TextBlock error;
        internal RuleEditDialog(PersonalRule rule,Func<SsmsConnection> connection,bool useCurrent=false)
        {
            original=rule.Copy();capture=connection;
            Title=rule.Kind==PersonalRuleKind.Snippet?"JoinAssist · Scorciatoia SQL":"JoinAssist · Regola SELECT";Width=660;Height=740;MinWidth=540;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;UiTheme.Dialog(this);
            var root=new DockPanel{Margin=new Thickness(18)};Content=root;
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            error=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,6)};footer.Children.Add(error);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(buttons);
            HelpGuide.Attach(this,buttons,()=>rule.Kind==PersonalRuleKind.Snippet?"snippets":"rules");
            var cancel=new Button{Content="Annulla",IsCancel=true,Margin=new Thickness(0,0,8,0),Padding=new Thickness(14,6,14,6)};buttons.Children.Add(cancel);cancel.Click+=(s,e)=>Close();
            var save=new Button{Content="Usa regola",IsDefault=true,Padding=new Thickness(14,6,14,6)};buttons.Children.Add(save);save.Click+=(s,e)=>{try{Read().Validate();DialogResult=true;}catch(ArgumentException ex){error.Text=ex.Message;}};
            var body=new StackPanel();root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            name=Field(body,"Nome della regola",rule.Name);
            var flags=new WrapPanel();body.Children.Add(flags);enabled=Check(flags,"Abilitata",rule.Enabled);automatic=Check(flags,"Applica anche con Spazio",rule.AutoOnSpace);
            body.Children.Add(new TextBlock{Text="Spazio applica solo una regola esatta senza alternative, con completamento automatico attivo. Non si applica a incolla o spostamenti del cursore. Nessun SQL viene eseguito.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,8)});
            server=Field(body,"Server (vuoto = tutti)",rule.Server);database=Field(body,"Database (vuoto = tutti)",rule.Database);
            var current=new Button{Content="Usa connessione corrente",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,6,0,8)};body.Children.Add(current);
            current.Click+=(s,e)=>{try{using(var c=GetConnection()){server.Text=c.Server;database.Text=c.Database;}}catch{error.Text="Apri una query collegata per leggere l'ambito corrente.";}};
            if(rule.Kind==PersonalRuleKind.SelectTable)
            {
                schema=Field(body,"Schema",rule.Schema);table=Field(body,"Tabella / vista",rule.Table);top=Field(body,"TOP, solo se assente (0 = non aggiungere)",rule.Top.ToString(System.Globalization.CultureInfo.InvariantCulture));
                body.Children.Add(new TextBlock{Text="Ordinamento, solo se assente",Margin=new Thickness(0,7,0,4)});
                order=new ComboBox{ItemsSource=new[]{"Nessuno","Prima colonna (ORDER BY 1)","Colonna specifica"},SelectedIndex=(int)rule.Order,MinHeight=27};body.Children.Add(order);
                column=new ComboBox{IsEditable=true,Text=rule.OrderColumn??"",MinHeight=27,Margin=new Thickness(0,5,0,0)};body.Children.Add(column);
                var load=new Button{Content="Carica colonne dalla connessione corrente",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,5,0,0)};body.Children.Add(load);load.Click+=LoadColumns;
                descending=Check(body,"Ordine decrescente (DESC)",rule.Descending);
                body.Children.Add(new TextBlock{Text="Si applica a SELECT * semplici con una sola sorgente del database corrente. TOP e ORDER BY esistenti vengono conservati, insieme a WHERE e commenti. JOIN, CTE, UNION, DISTINCT e SELECT INTO sono esclusi.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,8)});
            }
            else
            {
                shortcut=Field(body,"Scorciatoia (all'inizio di un'istruzione, dopo ; o GO)",rule.Shortcut);
                linkedFields=Check(body,"Abilita campi collegati {{nome:valore}} (Tab / Shift+Tab)",rule.LinkedFields);linkedFields.Checked+=(s,e)=>UpdatePreview();linkedFields.Unchecked+=(s,e)=>UpdatePreview();
                sql=Field(body,"SQL · $cursor$ oppure campi {{nome:valore}} e {{nome}} (Tab / Shift+Tab)",rule.Sql);sql.AcceptsReturn=true;sql.AcceptsTab=true;sql.Height=130;sql.TextWrapping=TextWrapping.NoWrap;sql.FontFamily=new FontFamily("Consolas");sql.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;sql.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;
            }
            body.Children.Add(new TextBlock{Text="Anteprima",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,10,0,4)});
            preview=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=80,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("Consolas")};body.Children.Add(preview);
            foreach(var field in new[]{name,server,database,schema,table,top,shortcut,sql}.Where(x=>x!=null))field.TextChanged+=(s,e)=>UpdatePreview();
            if(order!=null)order.SelectionChanged+=(s,e)=>UpdatePreview();if(column!=null)column.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((s,e)=>UpdatePreview()));
            if(descending!=null){descending.Checked+=(s,e)=>UpdatePreview();descending.Unchecked+=(s,e)=>UpdatePreview();}
            if(useCurrent && capture!=null)try{using(var c=GetConnection()){server.Text=c.Server;database.Text=c.Database;}}catch{}
            UpdatePreview();
        }
        static TextBox Field(Panel body,string label,string text)
        {body.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,3)});var box=new TextBox{Text=text??"",MinHeight=25};body.Children.Add(box);return box;}
        static CheckBox Check(Panel body,string label,bool value)
        {var box=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(0,7,16,5)};body.Children.Add(box);return box;}
        SsmsConnection GetConnection(){var value=capture==null?null:capture();if(value==null)throw new InvalidOperationException("Nessuna query collegata.");return value;}
        async void LoadColumns(object sender,RoutedEventArgs e)
        {
            var button=(Button)sender;button.IsEnabled=false;
            try
            {
                using(var c=GetConnection())using(var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    var catalog=await CatalogReader.ReadAsync(c.Connection,cancel.Token,c.CacheScope);var obj=catalog.Find(schema.Text.Trim(),table.Text.Trim());
                    if(obj==null)throw new InvalidOperationException("Tabella non trovata nel database della scheda corrente.");
                    await catalog.EnsureAsync(obj,cancel.Token);if(!c.IsStillActive())throw new InvalidOperationException("Connessione cambiata.");string selected=column.Text;column.ItemsSource=obj.Columns.Select(x=>x.Name).ToList();column.Text=selected;error.Text="Colonne caricate da "+c.Database+". L'ambito della regola resta quello indicato sopra.";
                }
            }
            catch{error.Text="Colonne non disponibili: controlla connessione, database e permessi sui metadati.";}
            finally{button.IsEnabled=true;}
        }
        internal PersonalRule Read()
        {
            var r=original.Copy();r.Name=name.Text.Trim();r.Enabled=enabled.IsChecked==true;r.AutoOnSpace=automatic.IsChecked==true;r.Server=server.Text.Trim();r.Database=database.Text.Trim();
            if(r.Kind==PersonalRuleKind.Snippet){r.Shortcut=shortcut.Text.Trim();r.Sql=sql.Text;r.LinkedFields=linkedFields.IsChecked==true;}
            else{int number;if(!int.TryParse(top.Text,out number))throw new ArgumentException("TOP deve essere un numero intero.");r.Schema=schema.Text.Trim();r.Table=table.Text.Trim();r.Top=number;r.Order=(RuleOrder)order.SelectedIndex;r.OrderColumn=column.Text.Trim();r.Descending=descending.IsChecked==true;}
            r.Validate();return r;
        }
        void UpdatePreview()
        {if(preview==null)return;try{var r=Read();preview.Text=PersonalRules.Preview(r);error.Text="La regola diventerà attiva con Salva nelle impostazioni.";}catch(ArgumentException ex){preview.Text="";error.Text=ex.Message;}}
    }
}
