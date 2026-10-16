using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows;

class InterfaceStub : RealProxy
{
    readonly Func<IMethodCallMessage, object> handler;
    public object[] OutArguments;
    public readonly Dictionary<string,int> Events = new Dictionary<string,int>();
    public InterfaceStub(Type contract, Func<IMethodCallMessage,object> handle) : base(contract) { handler=handle; }
    public override IMessage Invoke(IMessage raw)
    {
        var call=(IMethodCallMessage)raw;
        try
        {
            if(call.MethodName.StartsWith("add_")||call.MethodName.StartsWith("remove_")) {
                string name=call.MethodName.Substring(call.MethodName.IndexOf('_')+1);if(!Events.ContainsKey(name))Events[name]=0;
                Events[name]+=call.MethodName.StartsWith("add_")?1:-1;
                return new ReturnMessage(null,null,0,call.LogicalCallContext,call);
            }
            var result=handler==null?null:handler(call);
            return new ReturnMessage(result,OutArguments??call.Args,call.ArgCount,call.LogicalCallContext,call);
        }
        catch(Exception ex){return new ReturnMessage(ex,call);}
    }
}
class EditorControllerSmoke
{
    [STAThread] static int Main(string[] args)
    {
        string ide=args[0];string[] dirs={ide,ide+@"\Extensions\Application",ide+@"\CommonExtensions\Microsoft\Editor",ide+@"\PublicAssemblies",ide+@"\PrivateAssemblies"};
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(var dir in dirs){string f=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll");if(File.Exists(f))return Assembly.LoadFrom(f);}return null;};
        try
        {
            var assembly=Assembly.LoadFrom(Path.GetFullPath(args[1]));var type=assembly.GetType("JoinAssist.AutomaticCompletion",true);
            var ctor=type.GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single();var parameters=ctor.GetParameters();
            var element=new Grid();
            Type viewType=parameters[0].ParameterType;
            var bufferType=viewType.GetInterfaces().SelectMany(i=>i.GetProperties()).Concat(viewType.GetProperties()).First(p=>p.Name=="TextBuffer").PropertyType;
            var caretType=viewType.GetInterfaces().SelectMany(i=>i.GetProperties()).Concat(viewType.GetProperties()).First(p=>p.Name=="Caret").PropertyType;
            var buffer=new InterfaceStub(bufferType,null);var caret=new InterfaceStub(caretType,null);
            var view=new InterfaceStub(viewType,c=>c.MethodName=="get_VisualElement"?(object)element:c.MethodName=="get_TextBuffer"?buffer.GetTransparentProxy():c.MethodName=="get_Caret"?caret.GetTransparentProxy():null);
            var adapterType=parameters[1].ParameterType.GetMethod("GetViewAdapter").ReturnType;
            var add=adapterType.GetMethods().First(m=>m.Name=="AddCommandFilter");var targetType=add.GetParameters()[0].ParameterType;
            int forwarded=0,added=0,removed=0;
            var next=new InterfaceStub(targetType,c=>{forwarded++;return 1234;});
            InterfaceStub adapter=null;adapter=new InterfaceStub(adapterType,c=>{
                if(c.MethodName=="AddCommandFilter"){added++;adapter.OutArguments=c.Args;adapter.OutArguments[1]=next.GetTransparentProxy();return 0;}
                if(c.MethodName=="RemoveCommandFilter"){removed++;return 0;}
                return 0;
            });
            var factory=new InterfaceStub(parameters[1].ParameterType,c=>c.MethodName=="GetViewAdapter"?adapter.GetTransparentProxy():null);
            var broker=new InterfaceStub(parameters[2].ParameterType,null);
            var controller=ctor.Invoke(new object[]{view.GetTransparentProxy(),factory.GetTransparentProxy(),broker.GetTransparentProxy(),new Action<string>(s=>{})});
            var filter=type.GetField("filter",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
            if(filter==null||added!=1)throw new Exception("Filtro tastiera non agganciato");
            var exec=filter.GetType().GetMethod("Exec");var group=new Guid("1496A755-94DE-11D0-8C3F-00C04FC2AAE2");
            foreach(uint id in new uint[]{1,2,3,4,11,13,103}) {
                var result=(int)exec.Invoke(filter,new object[]{group,id,(uint)0,IntPtr.Zero,IntPtr.Zero});
                if(result!=1234)throw new Exception("Comando editor intercettato con popup chiuso: id="+id+", risultato="+result+", next="+(filter.GetType().GetField("next",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(filter)==null?"null":"presente"));
            }
            ((IDisposable)controller).Dispose();((IDisposable)controller).Dispose();
            if(removed!=1||forwarded!=7||view.Events.Any(p=>p.Value!=0)||buffer.Events.Any(p=>p.Value!=0)||caret.Events.Any(p=>p.Value!=0))throw new Exception("Rilascio eventi o inoltro comandi errato");
            Console.WriteLine("PASS: controller automatico costruito con editor simulato; 7 comandi normali inoltrati; filtro ed eventi rimossi correttamente. Nessun popup mostrato né connessione aperta.");
            ControllerCommitTests.Run(assembly);
            var presentation=assembly.GetType("JoinAssist.ProposalPresentation",true).GetMethod("Template",BindingFlags.NonPublic|BindingFlags.Static);
            var joinTemplate=(DataTemplate)presentation.Invoke(null,null);joinTemplate.Seal();
            var text=(TextBlock)joinTemplate.LoadContent();
            text.DataContext=new JoinAssist.JoinProposal { Target="Users", Source="o", ForeignKeyName="FK_Creator", Predicate="o.CreatedByUserId = Users.Id" };
            text.GetBindingExpression(TextBlock.TextProperty).UpdateTarget();
            var frame=new System.Windows.Threading.DispatcherFrame();
            text.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            if(!text.Text.Contains("\nON o.CreatedByUserId = Users.Id"))throw new Exception("JOIN presentation did not bind the complete relationship: "+text.Text+"; "+text.GetBindingExpression(TextBlock.TextProperty).Status);
            Console.WriteLine("PASS: WPF JOIN template binds the actual predicate and FK name as plain text.");
            UpdateWindow(assembly);
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void UpdateWindow(Assembly assembly)
    {
        var type=assembly.GetType("JoinAssist.UpdateDialog",true);
        var dialog=(Window)Activator.CreateInstance(type,true);
        try
        {
            var grid=(Grid)dialog.Content;
            var buttons=grid.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
            if(buttons.Length!=4 || !buttons[0].IsEnabled || buttons[1].IsEnabled || buttons[2].IsEnabled)
                throw new Exception("Update window initial button state incorrect");
            if(!grid.Children.OfType<TextBox>().Single().IsReadOnly)throw new Exception("Release notes must be plain read-only text");
            string version=assembly.GetName().Version.ToString(3);
            if(!grid.Children.OfType<TextBlock>().Any(t=>t.Text.Contains(version)))throw new Exception("Installed version missing from update window");
            var releaseType=assembly.GetType("JoinAssist.UpdateRelease",true);
            const string json="{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v99.0.0\",\"html_url\":\"https://github.com/DragonPalowski/JoinAssist/releases/tag/v99.0.0\",\"body\":\"Synthetic notes\",\"assets\":[{\"name\":\"JoinAssist-99.0.0.vsix\",\"state\":\"uploaded\",\"size\":10,\"digest\":\"sha256:0000000000000000000000000000000000000000000000000000000000000000\",\"browser_download_url\":\"https://github.com/DragonPalowski/JoinAssist/releases/download/v99.0.0/JoinAssist-99.0.0.vsix\"}]}";
            var release=releaseType.GetMethod("Parse",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{json});
            type.GetField("release",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(dialog,release);
            var setBusy=type.GetMethod("SetBusy",BindingFlags.NonPublic|BindingFlags.Instance);
            setBusy.Invoke(dialog,new object[]{false});
            if(!buttons[1].IsEnabled || buttons[2].IsEnabled)throw new Exception("New release should enable download only");
            setBusy.Invoke(dialog,new object[]{true});
            if(buttons.Take(3).Any(b=>b.IsEnabled))throw new Exception("Network operation should disable conflicting actions");
            type.GetField("downloaded",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(dialog,"synthetic.vsix");
            setBusy.Invoke(dialog,new object[]{false});
            if(buttons[1].IsEnabled || !buttons[2].IsEnabled)throw new Exception("Verified download should enable folder button");
            var content=(FrameworkElement)dialog.Content;
            content.Measure(new Size(600,420));content.Arrange(new Rect(0,0,600,420));content.UpdateLayout();
            if(content.ActualWidth<=0 || content.ActualHeight<=0)throw new Exception("Update window layout failed");
            Console.WriteLine("PASS: WPF update window constructs, displays installed version, lays out, and handles available/busy/downloaded button states. No network or installer.");
        }
        finally { dialog.Close(); }
    }
}
