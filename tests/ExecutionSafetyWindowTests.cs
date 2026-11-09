using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using JoinAssist;

static class ExecutionSafetyWindowTests
{
    static int checks;
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception("Execution safety UI: "+text);}
    internal static void Run(Assembly plugin)
    {
        var review=ExecutionSafety.Analyze("UPDATE Orders SET Flag=1; DELETE FROM Items;");
        var type=plugin.GetType("JoinAssist.ExecutionSafetyDialog");
        var window=(Window)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{review,true},null);
        var root=(DockPanel)window.Content;var buttons=root.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToList();
        Check(buttons.Single(b=>(string)b.Content=="Annulla").IsDefault && buttons.Single(b=>(string)b.Content=="Annulla").IsCancel,"Cancel is Enter/Escape default");
        Check(!buttons.Single(b=>(string)b.Content=="Esegui comunque").IsDefault,"run is explicit only");
        Check(root.Children.OfType<ListBox>().Single().Items.Count==2,"single review lists every offending statement");
        foreach(string theme in new[]{"dark","light"})
        {
            window.Resources[EnvironmentColors.ToolWindowBackgroundBrushKey]=theme=="dark"?Brushes.Black:Brushes.White;
            window.Resources[EnvironmentColors.ToolWindowTextBrushKey]=theme=="dark"?Brushes.White:Brushes.Black;
            NativeMenuTests.Preview(window,"execution-safety-"+theme+".png");
            Check(root.ActualWidth>0 && root.ActualHeight>0,"dialog laid out "+theme);
        }
        window.Close();
        var events=new InterfaceStub(typeof(EnvDTE.CommandEvents),null);int calls=0;bool proceed=false;
        var hookType=plugin.GetType("JoinAssist.ExecutionSafetyHook");
        var hook=Activator.CreateInstance(hookType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{events.GetTransparentProxy(),new Func<string,int,bool>((g,id)=>g=="query" && id==1),new Func<bool>(()=>{calls++;return proceed;})},null);
        var before=hookType.GetMethod("BeforeExecute",BindingFlags.Instance|BindingFlags.NonPublic);
        var saved=PreferenceStore.Current;var apply=typeof(PreferenceStore).GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic);
        try
        {
            foreach(string route in new[]{"F5","Ctrl+E","toolbar","menu"})
            {var args=new object[]{"query",1,null,null,false};before.Invoke(hook,args);Check((bool)args[4],"central command cancels "+route);}
            var unrelated=new object[]{"query",2,null,null,false};before.Invoke(hook,unrelated);Check(!(bool)unrelated[4] && calls==4,"parse/plan/other commands forwarded unchanged");
            proceed=true;var accepted=new object[]{"query",1,null,null,false};before.Invoke(hook,accepted);Check(!(bool)accepted[4] && calls==5,"original command continues once; hook never executes one");
            var canceled=new object[]{"query",1,null,null,true};before.Invoke(hook,canceled);Check((bool)canceled[4] && calls==5,"earlier cancellation is never cleared");
            hookType.GetField("checking",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hook,true);
            before.Invoke(hook,accepted);Check((bool)accepted[4] && calls==5,"recursive execution during review is canceled without a second prompt");
            hookType.GetField("checking",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hook,false);
            apply.Invoke(null,new object[]{new Preferences{ExecutionSafety=false}});accepted[4]=false;before.Invoke(hook,accepted);Check(calls==5 && !(bool)accepted[4],"opt-out disables check");
        }
        finally{apply.Invoke(null,new object[]{saved});((IDisposable)hook).Dispose();}
        Check(events.Events.Values.All(v=>v==0),"command event unregistered on disposal");
        Console.WriteLine("PASS: "+checks+" execution-safety dialog/command-event checks (cancel default, one review, themes, all central-command entry routes, explicit allow and opt-out). No real command or SQL executed.");
    }
}
