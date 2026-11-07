using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GlassBongoCat
{
    internal sealed class SettingsWindow : Window
    {
        private readonly CatOptions options;
        private readonly Action changed;
        internal SettingsWindow(CatOptions options,Action changed,Action reset,string status)
        {
            this.options=options;this.changed=changed;
            Title="Glass Bongo Cat · 设置";Width=460;Height=655;MinWidth=410;MinHeight=560;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Background=ColorBrush(246,248,249);FontFamily=new FontFamily("Microsoft YaHei UI");
            FontSize=13;Foreground=ColorBrush(30,43,49);
            var scroll=new ScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            var panel=new StackPanel {Margin=new Thickness(28,22,28,24)};scroll.Content=panel;Content=scroll;
            panel.Children.Add(new TextBlock {Text="Glass Bongo Cat",FontSize=25,FontWeight=FontWeights.SemiBold});
            panel.Children.Add(new TextBlock {Text="会敲键盘的玻璃猫",Margin=new Thickness(0,4,0,22),Foreground=ColorBrush(94,111,119)});
            Slider(panel,"折射强度",.3,2.5,options.Refraction,delegate(double v){options.Refraction=v;},"0.00");
            Slider(panel,"玻璃厚度",.65,1.8,options.Thickness,delegate(double v){options.Thickness=v;},"0.00");
            Slider(panel,"边缘高光",0,2,options.Highlight,delegate(double v){options.Highlight=v;},"0.00");
            Slider(panel,"清透程度",0,1,options.Clarity,delegate(double v){options.Clarity=v;},"P0");
            var sizeSlider=Slider(panel,"桌宠宽度",160,480,options.Size,delegate(double v){options.Size=v;},"0");
            var presets=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,14)};
            Button(presets,"紧凑",delegate{sizeSlider.Value=230;});
            Button(presets,"标准",delegate{sizeSlider.Value=300;});
            Button(presets,"放大",delegate{sizeSlider.Value=400;});panel.Children.Add(presets);
            Toggle(panel,"跟着键盘、鼠标敲爪子",options.InputEnabled,delegate(bool v){options.InputEnabled=v;});
            Toggle(panel,"点击穿透",options.ClickThrough,delegate(bool v){options.ClickThrough=v;});
            Toggle(panel,"录屏可见（透明高光，无桌面折射）",options.RecordingMode,delegate(bool v){options.RecordingMode=v;});
            panel.Children.Add(new TextBlock {Text="拖动猫咪移动位置 · 双击打开设置\n右键或托盘可隐藏、暂停和退出",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,8),Foreground=ColorBrush(94,111,119)});
            if(!String.IsNullOrEmpty(status))panel.Children.Add(new TextBlock {Text=status,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=ColorBrush(116,92,48),Margin=new Thickness(0,0,0,10)});
            var actions=new StackPanel {Orientation=Orientation.Horizontal};
            Button(actions,"找回猫咪",reset);Button(actions,"完成",delegate{Close();});panel.Children.Add(actions);
        }
        private Slider Slider(Panel panel,string label,double min,double max,double value,Action<double> set,string format)
        {
            var row=new DockPanel {Margin=new Thickness(0,0,0,4)};
            var readout=new TextBlock {Text=value.ToString(format,CultureInfo.InvariantCulture),HorizontalAlignment=HorizontalAlignment.Right,Foreground=ColorBrush(52,106,111)};
            DockPanel.SetDock(readout,Dock.Right);row.Children.Add(readout);row.Children.Add(new TextBlock {Text=label});panel.Children.Add(row);
            var slider=new Slider {Minimum=min,Maximum=max,Value=value,Margin=new Thickness(0,0,0,15),IsMoveToPointEnabled=true};
            slider.ValueChanged+=delegate{set(slider.Value);readout.Text=slider.Value.ToString(format,CultureInfo.InvariantCulture);changed();};panel.Children.Add(slider);
            return slider;
        }
        private void Toggle(Panel panel,string label,bool value,Action<bool> set)
        {
            var box=new CheckBox {Content=label,IsChecked=value,Margin=new Thickness(0,4,0,5)};
            box.Click+=delegate{set(box.IsChecked==true);changed();};panel.Children.Add(box);
        }
        private static void Button(Panel parent,string label,Action action)
        {
            var button=new Button {Content=label,Padding=new Thickness(17,7,17,7),Margin=new Thickness(0,0,10,0),Background=Brushes.White,BorderBrush=ColorBrush(218,227,230),BorderThickness=new Thickness(1)};
            button.Click+=delegate{action();};parent.Children.Add(button);
        }
        private static SolidColorBrush ColorBrush(byte r,byte g,byte b){var bsh=new SolidColorBrush(Color.FromRgb(r,g,b));bsh.Freeze();return bsh;}
    }
}
