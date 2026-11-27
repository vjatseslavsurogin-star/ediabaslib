#if BAVARIAN_USB_BASELINE
using System;
using Android.App;
using Android.Content;
using ColorStateList = Android.Content.Res.ColorStateList;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
namespace BmwDeepObd
{
    internal static class BavarianUi
    {
        internal static readonly Color Bg=Color.ParseColor("#0B1018"),Surface=Color.ParseColor("#19283B"),Line=Color.ParseColor("#26354A"),White=Color.ParseColor("#F2F6FC"),Muted=Color.ParseColor("#A8B6C8"),Blue=Color.ParseColor("#62ABFF"),Green=Color.ParseColor("#59D9A1");
        internal static int Dp(Context c,int n)=>(int)(n*c.Resources.DisplayMetrics.Density+0.5f);
        internal static GradientDrawable Shape(Context c,Color color,int radius=14)
        {
            var s=new GradientDrawable();s.SetColor(color);s.SetCornerRadius(Dp(c,radius));return s;
        }
        internal static TextView Text(Context c,string text,int size,bool title=false)
        {
            var t=new TextView(c){Text=text,TextSize=size};t.SetTextColor(title?White:Muted);
            if(title)t.SetTypeface(Typeface.Default,TypefaceStyle.Bold);return t;
        }
        internal static void Style(Button b,bool primary=false)
        {
            var c=b.Context;b.SetAllCaps(false);b.TextSize=15;b.SetTextColor(White);
            var states=new StateListDrawable();
            states.AddState(new[]{-Android.Resource.Attribute.StateEnabled},Shape(c,Color.ParseColor("#1B2533"),10));
            states.AddState(new[]{Android.Resource.Attribute.StatePressed},Shape(c,Color.ParseColor("#294970"),10));
            states.AddState(Array.Empty<int>(),Shape(c,primary?Color.ParseColor("#2368C8"):Surface,10));
            b.Background=states;b.SetPadding(Dp(c,14),Dp(c,10),Dp(c,14),Dp(c,10));b.SetMinHeight(Dp(c,46));
            b.SetTextColor(new ColorStateList(new[]{new[]{-Android.Resource.Attribute.StateEnabled},Array.Empty<int>()},new[]{Color.ParseColor("#77869A").ToArgb(),White.ToArgb()}));
        }
        internal static Button Action(LinearLayout parent,string label,EventHandler click,bool primary=false)
        {
            var b=new Button(parent.Context){Text=label};Style(b,primary);b.Click+=click;
            parent.AddView(b,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(parent.Context,8)});return b;
        }
        internal static void Checkbox(CheckBox b)
        {
            b.SetTextColor(White);b.ButtonTintList=ColorStateList.ValueOf(Blue);
        }
        internal static LinearLayout Shell(Activity a,int selected,bool ru,Action<int> navigate,out LinearLayout body)
        {
            a.Window.SetStatusBarColor(Bg);a.Window.SetNavigationBarColor(Bg);
            var root=new LinearLayout(a){Orientation=Orientation.Vertical};root.SetBackgroundColor(Bg);root.SetFitsSystemWindows(true);
            var scroll=new ScrollView(a){FillViewport=true};
            body=new LinearLayout(a){Orientation=Orientation.Vertical};body.SetPadding(Dp(a,18),Dp(a,18),Dp(a,18),Dp(a,20));
            scroll.AddView(body);root.AddView(scroll,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,0,1));
            var divider=new View(a);divider.SetBackgroundColor(Line);root.AddView(divider,new LinearLayout.LayoutParams(-1,Dp(a,1)));
            var nav=new LinearLayout(a){Orientation=Orientation.Horizontal};nav.SetBackgroundColor(Surface);
            string[] labels=ru?new[]{"Главная","Ошибки","Данные","Инструменты","Настройки"}:new[]{"Home","Diagnostics","Live Data","Tools","Settings"};
            string[] glyphs={"⌂","◇","≋","⊕","⚙"};
            for(int i=0;i<5;i++)
            {
                int index=i;
                var cell=new LinearLayout(a){Orientation=Orientation.Vertical};cell.SetGravity(GravityFlags.Center);cell.SetPadding(0,Dp(a,5),0,Dp(a,5));
                var icon=Text(a,glyphs[i],23,true);icon.SetTextColor(i==selected?Blue:Muted);icon.Gravity=GravityFlags.Center;cell.AddView(icon);
                var label=Text(a,labels[i],10,i==selected);label.SetTextColor(i==selected?Blue:Muted);label.Gravity=GravityFlags.Center;cell.AddView(label);
                cell.ContentDescription=labels[i];cell.Clickable=true;cell.Focusable=true;cell.Click+=(s,e)=>navigate(index);
                nav.AddView(cell,new LinearLayout.LayoutParams(0,Dp(a,60),1));
            }
            root.AddView(nav,new LinearLayout.LayoutParams(-1,-2));a.SetContentView(root);return root;
        }
        internal static void NavigateHome(Activity a,int tab)
        {
            var intent=new Intent(a,typeof(BavarianHomeActivity));intent.PutExtra("bavarian_tab",tab);
            intent.AddFlags(ActivityFlags.ClearTop|ActivityFlags.SingleTop);a.StartActivity(intent);a.Finish();
        }
    }
}
#endif
