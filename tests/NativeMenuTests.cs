using System;
using System.Collections;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinAssist;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;

static class NativeMenuTests
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception("Compact menu: "+label);}
    internal static void Run(Assembly plugin)
    {
        var manager=new ResourceManager("JoinAssist.Resources",plugin);
        object resource=manager.GetObject("Menus.ctmenu",System.Globalization.CultureInfo.InvariantCulture);
        Check(resource is byte[] && ((byte[])resource).Length>100 || resource is Stream && ((Stream)resource).Length>100,"Microsoft menu table stored in managed resources");manager.ReleaseAllResources();
        string pkgdef=File.ReadAllText(Path.ChangeExtension(plugin.Location,"pkgdef"));
        Check(pkgdef.Contains("Menus.ctmenu") && pkgdef.Contains(JoinAssistPackage.PackageId) && pkgdef.Contains("AllowsBackgroundLoad"),"registration generated from actual package attributes");
        Check(plugin.GetType("JoinAssist.JoinMarginProvider")==null,"old editor margin provider removed");
        var provider=typeof(JoinEditorProvider).GetCustomAttributes(typeof(ExportAttribute),false).Cast<ExportAttribute>().Single();
        Check(provider.ContractType==typeof(IWpfTextViewCreationListener),"completion attaches without a visible margin");
        using(var container=new ServiceContainer())using(var service=new OleMenuCommandService(container))
        {
            bool invoked=false;
            var method=typeof(JoinAssistPackage).GetMethod("Add",BindingFlags.Static|BindingFlags.NonPublic);
            method.Invoke(null,new object[]{service,0x0400,new Action(()=>invoked=true),null});
            var command=service.FindCommand(new CommandID(new Guid("a2cba72d-020e-4a42-9000-c21be31bf419"),0x0400));
            Check(command!=null,"native command registered");command.Invoke();Check(invoked,"native command reaches callback");
        }
        var template=(DataTemplate)plugin.GetType("JoinAssist.ProposalPresentation").GetMethod("CompactTemplate",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        template.Seal();
        var panel=(StackPanel)template.LoadContent();var title=(TextBlock)panel.Children[0];var detail=(TextBlock)panel.Children[1];
        panel.DataContext=new JoinProposal{Target="Customers",Source="Orders",ForeignKeyName="FK_Orders_Customers",Predicate="Orders.CustomerId = Customers.Id"};
        title.GetBindingExpression(TextBlock.TextProperty).UpdateTarget();detail.GetBindingExpression(TextBlock.TextProperty).UpdateTarget();
        var frame=new System.Windows.Threading.DispatcherFrame();panel.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(title.Text=="Customers" && detail.Text.Contains("FK_Orders_Customers") && !detail.Text.Contains("Orders.CustomerId"),"compact row keeps FK identity; predicate remains in selected preview");
        var theme=plugin.GetType("JoinAssist.UiTheme").GetMethod("Dialog",BindingFlags.NonPublic|BindingFlags.Static);
        var window=new Window();var dark=new SolidColorBrush(Color.FromRgb(30,30,30));window.Resources[EnvironmentColors.ToolWindowBackgroundBrushKey]=dark;
        theme.Invoke(null,new object[]{window});Check(ReferenceEquals(window.Background,dark),"window uses host theme");
        var light=new SolidColorBrush(Colors.White);window.Resources[EnvironmentColors.ToolWindowBackgroundBrushKey]=light;
        Check(ReferenceEquals(window.Background,light),"theme changes update an open window");window.Close();
        var settings=new PreferencesDialog();
        var tabs=((DockPanel)settings.Content).Children.OfType<TabControl>().Single();
        Check(tabs.Items.Count==3 && tabs.Items.Cast<TabItem>().Select(t=>t.Header as string).SequenceEqual(new[]{"Completamento","Formato SQL","Cache"}),"settings grouped into three pages");settings.Close();
        var dialog=(Window)Activator.CreateInstance(plugin.GetType("JoinAssist.DiagnosticDialog"),BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{new Func<string>(()=>"JoinAssist pronto.")},null);
        dialog.Measure(new Size(dialog.Width,dialog.Height));dialog.Arrange(new Rect(0,0,dialog.Width,dialog.Height));
        Check(((DockPanel)dialog.Content).Children.OfType<TextBox>().Single().Text.Contains("JoinAssist pronto."),"diagnostics available without database access");dialog.Close();
        EditorRouting(plugin);
        Placement(plugin);
        SchemaWindows(plugin);
        Console.WriteLine("PASS: "+checks+" compact-menu checks (native resources, package registration, margin-free editor, command routing, compact rows, theme changes, settings pages, diagnostics).");
    }
    static void Placement(Assembly plugin)
    {
        var type=plugin.GetType("JoinAssist.MenuPlacement");var contract=type.GetNestedType("IStore",BindingFlags.NonPublic);var apply=type.GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic);
        bool initialized=false;string value="{00000000-0000-0000-0000-000000000001},2048";int writes=0;
        string existing=value;
        var store=new InterfaceStub(contract,c=>{
            if(c.MethodName=="get_Initialized")return initialized;
            if(c.MethodName=="ReadAllowed")return value;
            if(c.MethodName=="WriteAllowed"){value=(string)c.Args[0];writes++;}
            if(c.MethodName=="MarkInitialized")initialized=true;
            return null;
        });
        apply.Invoke(null,new[]{store.GetTransparentProxy()});
        Check(initialized && writes==1 && value==existing+";{a2cba72d-020e-4a42-9000-c21be31bf419},4096","first-run placement appends only JoinAssist; preserves other menus");
        value=existing;apply.Invoke(null,new[]{store.GetTransparentProxy()});
        Check(writes==1 && value==existing,"later customization is respected; menu not forced back on every start");
        initialized=false;value="A2CBA72D-020E-4A42-9000-C21BE31BF419,4096";apply.Invoke(null,new[]{store.GetTransparentProxy()});
        Check(initialized && writes==1,"existing menu entry is not duplicated regardless of GUID formatting");
    }
    static void SchemaWindows(Assembly plugin)
    {
        var columns=new System.Collections.Generic.List<ColumnChoice>{new ColumnChoice{Selected=true,Sql="o.Id",Description="int · PK"},new ColumnChoice{Selected=true,Sql="o.Name",Description="nvarchar(40)"}};
        var star=(Window)Activator.CreateInstance(plugin.GetType("JoinAssist.StarDialog"),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{columns},null);
        star.Measure(new Size(star.Width,star.Height));star.Arrange(new Rect(0,0,star.Width,star.Height));
        var root=(DockPanel)star.Content;Check(root.Children.OfType<TextBox>().Single().Text=="o.Id, o.Name","star window previews selected columns");
        Preview(star,"star-expansion.png");
        var all=root.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToList();
        all.Single(b=>(string)b.Content=="Nessuna").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(columns.All(c=>!c.Selected) && !all.Single(b=>(string)b.Content=="Inserisci colonne").IsEnabled,"empty star selection disables insertion");
        all.Single(b=>(string)b.Content=="Tutte").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(columns.All(c=>c.Selected),"select all restores source order");star.Close();
        var info=(Window)Activator.CreateInstance(plugin.GetType("JoinAssist.ObjectInfoDialog"),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{new System.Collections.Generic.List<ObjectInfo>{new ObjectInfo{Name="o",Columns=columns,Relationships="FK_Orders_Customers"}}},null);
        info.Measure(new Size(info.Width,info.Height));info.Arrange(new Rect(0,0,info.Width,info.Height));
        var tabs=((DockPanel)info.Content).Children.OfType<TabControl>().Single();Check(tabs.Items.Count==2,"schema sheet separates columns and FK relationships");Preview(info,"object-info.png");info.Close();
        var plan=new DeletePlan{Script="-- Synthetic preview only\r\nBEGIN TRANSACTION;\r\nDELETE FROM Orders WHERE Id=@id;\r\nROLLBACK TRANSACTION;"};
        plan.Steps.Add(new DeleteStep{Table=new DeleteTable{Schema="dbo",Name="Items"},Explicit=true,Relationships="FK_Items_Orders"});
        plan.Steps.Add(new DeleteStep{Table=new DeleteTable{Schema="dbo",Name="Orders"},Explicit=true,Relationships="filtro originale"});
        var deletion=(Window)Activator.CreateInstance(plugin.GetType("JoinAssist.DeletePlanDialog"),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{plan},null);
        var deleteRoot=(DockPanel)deletion.Content;var deleteTabs=deleteRoot.Children.OfType<TabControl>().Single();
        Check(deleteTabs.Items.Count==2 && ((TextBox)((TabItem)deleteTabs.Items[1]).Content).IsReadOnly,"DELETE script is read-only in separate preview tab");
        var deleteButtons=deleteRoot.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToList();
        Check(deleteButtons.Single(b=>(string)b.Content=="Inserisci script di prova").IsEnabled && deleteButtons.Single(b=>(string)b.Content=="Annulla").IsCancel,"DELETE preview explicitly inserts or cancels; no execution action");
        Preview(deletion,"delete-plan.png");deletion.Close();
        var blocked=new DeletePlan();blocked.Problems.Add("Ciclo: Orders → Items → Orders. Piano non generato.");
        deletion=(Window)Activator.CreateInstance(plugin.GetType("JoinAssist.DeletePlanDialog"),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{blocked},null);
        deleteRoot=(DockPanel)deletion.Content;
        Check(!deleteRoot.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(b=>(string)b.Content=="Inserisci script di prova").IsEnabled,"blocked DELETE cannot be inserted");
        Preview(deletion,"delete-plan-blocked.png");deletion.Close();
    }
    internal static void Preview(Window window,string name)
    {
        string folder=Environment.GetEnvironmentVariable("JOINASSIST_TEST_PREVIEW");if(string.IsNullOrEmpty(folder))return;
        Directory.CreateDirectory(folder);var content=(FrameworkElement)window.Content;
        content.Measure(new Size(window.Width-40,window.Height-70));content.Arrange(new Rect(0,0,window.Width-40,window.Height-70));content.UpdateLayout();
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.Width-40,(int)window.Height-70,96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(window.Background,null,new Rect(0,0,image.PixelWidth,image.PixelHeight));image.Render(background);image.Render(content);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(folder,name)))png.Save(file);
    }
    static void EditorRouting(Assembly plugin)
    {
        var properties=new Microsoft.VisualStudio.Utilities.PropertyCollection();bool closed=false;
        var grid=new Grid();var buffer=new InterfaceStub(typeof(Microsoft.VisualStudio.Text.ITextBuffer),null);var caret=new InterfaceStub(typeof(ITextCaret),null);
        var view=new InterfaceStub(typeof(IWpfTextView),c=>c.MethodName=="get_VisualElement"?(object)grid:c.MethodName=="get_Properties"?properties:c.MethodName=="get_IsClosed"?closed:c.MethodName=="get_TextBuffer"?buffer.GetTransparentProxy():c.MethodName=="get_Caret"?caret.GetTransparentProxy():null);
        var type=plugin.GetType("JoinAssist.JoinEditorController");
        var resolve=type.GetMethod("FromView",BindingFlags.Static|BindingFlags.NonPublic);
        object[] argument={view.GetTransparentProxy()};
        Check(resolve.Invoke(null,argument)==null,"non-SQL active view never falls back to an earlier SQL tab");
        var controller=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{view.GetTransparentProxy(),null,null},null);
        properties.AddProperty(type,controller);
        Check(ReferenceEquals(resolve.Invoke(null,argument),controller),"menu resolves controller belonging to this view");
        closed=true;Check(resolve.Invoke(null,argument)==null,"closed query tab cannot receive commands");closed=false;
        ((IDisposable)controller).Dispose();Check(!properties.ContainsProperty(type) && resolve.Invoke(null,argument)==null,"closed controller unregisters itself");
        Check(view.Events.All(e=>e.Value==0) && buffer.Events.All(e=>e.Value==0) && caret.Events.All(e=>e.Value==0),"margin-free editor releases all handlers");
    }
}
