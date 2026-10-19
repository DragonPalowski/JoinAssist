using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace JoinAssist
{
    internal static class UiTheme
    {
        internal static void Owner(Window window)
        {
            var handle=System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if(handle!=IntPtr.Zero)new System.Windows.Interop.WindowInteropHelper(window).Owner=handle;
        }
        internal static void Surface(FrameworkElement element)
        {
            Fallback(element,EnvironmentColors.ToolWindowBackgroundBrushKey,SystemColors.WindowBrush);
            Fallback(element,EnvironmentColors.ToolWindowTextBrushKey,SystemColors.WindowTextBrush);
            Fallback(element,EnvironmentColors.ToolWindowBorderBrushKey,SystemColors.ActiveBorderBrush);
            var control=element as Control;
            if(control!=null){control.SetResourceReference(Control.BackgroundProperty,EnvironmentColors.ToolWindowBackgroundBrushKey);control.SetResourceReference(Control.ForegroundProperty,EnvironmentColors.ToolWindowTextBrushKey);}
            if(element is ListBox && element.TryFindResource(VsResourceKeys.ThemedDialogListBoxStyleKey) is Style)
                element.SetResourceReference(FrameworkElement.StyleProperty,VsResourceKeys.ThemedDialogListBoxStyleKey);
            var border=element as Border;
            if(border!=null){border.SetResourceReference(Border.BackgroundProperty,EnvironmentColors.ToolWindowBackgroundBrushKey);border.SetResourceReference(Border.BorderBrushProperty,EnvironmentColors.ToolWindowBorderBrushKey);}
        }
        static void Fallback(FrameworkElement element,object key,Brush value){if(element.TryFindResource(key)==null)element.Resources[key]=value;}
        internal static void Dialog(Window window)
        {
            Surface(window);
            var defaults=window.TryFindResource(VsResourceKeys.ThemedDialogDefaultStylesKey) as ResourceDictionary;
            if(defaults!=null){window.Resources.MergedDictionaries.Add(defaults);return;}
            foreach(var type in new[]{typeof(Button),typeof(TextBox),typeof(ComboBox),typeof(TabControl),typeof(TabItem),typeof(CheckBox),typeof(ListBox)})
            {
                var inherited=window.TryFindResource(type) as Style;
                if(inherited!=null){window.Resources[type]=inherited;continue;}
                var style=new Style(type);
                style.Setters.Add(new Setter(Control.BackgroundProperty,new System.Windows.DynamicResourceExtension(EnvironmentColors.ToolWindowBackgroundBrushKey)));
                style.Setters.Add(new Setter(Control.ForegroundProperty,new System.Windows.DynamicResourceExtension(EnvironmentColors.ToolWindowTextBrushKey)));
                style.Setters.Add(new Setter(Control.BorderBrushProperty,new System.Windows.DynamicResourceExtension(EnvironmentColors.ToolWindowBorderBrushKey)));
                window.Resources[type]=style;
            }
        }
    }
}
