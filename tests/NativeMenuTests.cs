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
        Console.WriteLine("PASS: "+checks+" compact-menu checks (native resources, package registration, margin-free editor, command routing, compact rows, theme changes, settings pages, diagnostics).");
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
