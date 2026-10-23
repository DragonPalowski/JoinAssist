using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
            if(defaults!=null)window.Resources.MergedDictionaries.Add(defaults);
            foreach(var type in new[]{typeof(Button),typeof(TextBox),typeof(ComboBox),typeof(TabControl),typeof(TabItem),typeof(CheckBox),typeof(ListBox)})
            {
                var inherited=window.TryFindResource(type) as Style;
                var style=new Style(type,inherited);
                // Let the checkbox template choose its glyph surface: Windows' fallback
                // draws a black tick and needs its native light box even in a dark dialog.
                if(type!=typeof(CheckBox))style.Setters.Add(new Setter(Control.BackgroundProperty,new System.Windows.DynamicResourceExtension(EnvironmentColors.ToolWindowBackgroundBrushKey)));
                style.Setters.Add(new Setter(Control.ForegroundProperty,new System.Windows.DynamicResourceExtension(EnvironmentColors.ToolWindowTextBrushKey)));
                style.Setters.Add(new Setter(Control.BorderBrushProperty,new System.Windows.DynamicResourceExtension(EnvironmentColors.ToolWindowBorderBrushKey)));
                if(type==typeof(Button))style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty,ButtonTextTemplate()));
                // Windows' default tab templates paint their own light surfaces, even when
                // Background is dark. Keep the host control styles, but own these surfaces.
                if(type==typeof(TabControl))style.Setters.Add(new Setter(Control.TemplateProperty,TabsTemplate()));
                if(type==typeof(TabItem))
                {
                    style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,6,10,6)));
                    style.Setters.Add(new Setter(Control.TemplateProperty,TabTemplate()));
                }
                window.Resources[type]=style;
            }
            // Labels must inherit the actual containing control's foreground, including
            // selected ListBoxItem text. A fixed dialog-wide brush breaks selection contrast.
            var text=new Style(typeof(TextBlock),window.TryFindResource(typeof(TextBlock)) as Style);
            text.Setters.Add(new Setter(TextBlock.ForegroundProperty,new Binding("Foreground"){
                RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Control),1)}));
            window.Resources[typeof(TextBlock)]=text;
        }
        static ControlTemplate TabsTemplate()
        {
            var dock=new FrameworkElementFactory(typeof(DockPanel));
            var headers=new FrameworkElementFactory(typeof(TabPanel));
            headers.SetValue(DockPanel.DockProperty,Dock.Top);
            headers.SetValue(Panel.IsItemsHostProperty,true);dock.AppendChild(headers);
            var surface=new FrameworkElementFactory(typeof(Border),"TabSurface");
            surface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
            surface.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
            surface.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            surface.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
            var content=new FrameworkElementFactory(typeof(ContentPresenter),"PART_SelectedContentHost");
            content.SetValue(ContentPresenter.ContentSourceProperty,"SelectedContent");
            surface.AppendChild(content);dock.AppendChild(surface);
            return new ControlTemplate(typeof(TabControl)){VisualTree=dock};
        }
        static ControlTemplate TabTemplate()
        {
            var surface=new FrameworkElementFactory(typeof(Border),"TabHeaderSurface");
            surface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
            surface.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
            surface.SetValue(Border.BorderThicknessProperty,new Thickness(1,1,1,0));
            surface.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
            var header=new FrameworkElementFactory(typeof(TextBlock),"TabHeader");
            header.SetBinding(TextBlock.TextProperty,new Binding("Header"){RelativeSource=RelativeSource.TemplatedParent});
            header.SetValue(TextBlock.ForegroundProperty,new TemplateBindingExtension(Control.ForegroundProperty));
            surface.AppendChild(header);
            var template=new ControlTemplate(typeof(TabItem)){VisualTree=surface};
            var selected=new Trigger{Property=TabItem.IsSelectedProperty,Value=true};
            selected.Setters.Add(new Setter(System.Windows.Documents.TextElement.FontWeightProperty,FontWeights.SemiBold,"TabHeader"));
            selected.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(1,1,1,3),"TabHeaderSurface"));
            selected.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension(SystemColors.HighlightBrushKey),"TabHeaderSurface"));
            template.Triggers.Add(selected);
            foreach(var property in new[]{UIElement.IsMouseOverProperty,UIElement.IsKeyboardFocusWithinProperty})
            {
                var active=new Trigger{Property=property,Value=true};
                active.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension(SystemColors.HighlightBrushKey),"TabHeaderSurface"));
                template.Triggers.Add(active);
            }
            var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.55));template.Triggers.Add(disabled);
            return template;
        }
        static DataTemplate ButtonTextTemplate()
        {
            // Generated string presenters can pick up an application-level black TextBlock
            // style instead of the window style. Bind explicitly, retaining button states.
            var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding());
            text.SetBinding(TextBlock.ForegroundProperty,new Binding("Foreground"){
                RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});
            return new DataTemplate{VisualTree=text};
        }
    }
}
