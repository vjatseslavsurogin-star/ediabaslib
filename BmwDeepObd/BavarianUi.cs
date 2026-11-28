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
        // Legacy member names Blue/White denote accent and foreground, not literal colors.
        internal static bool IsLight { get; private set; }
        private static Color C(string dark,string light)=>Color.ParseColor(IsLight?light:dark);
        internal static Color Bg=>C("#07090D","#F5F7FA");
        internal static Color Surface=>C("#10151F","#FFFFFF");
        internal static Color Line=>C("#252C37","#DDE3EA");
        internal static Color White=>C("#F7F8FA","#141A23");
        internal static Color Muted=>C("#9EA7B5","#586578");
        internal static Color Blue=>C("#FF781F","#BD4700");
        internal static Color Green=>C("#53D69A","#167348");
        internal static Color Warning=>C("#FFB04D","#935600");
        internal static Color Danger=>C("#FF7777","#B3262D");
        internal static Color Primary=>Color.ParseColor("#FF781F");
        internal static Color OnPrimary=>Color.ParseColor("#15100B");
        internal static Color Selected=>C("#2A1B13","#FFF0E5");
        internal static Color Compare=>C("#B8C6D9","#536D92");
        internal static string Appearance(Context c)=>c.GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetString("appearance","system")??"system";
        internal static bool Compact(Context c)=>c.GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetBoolean("compact",false);
        internal static void ApplyTheme(Activity a)
        {
            string mode=Appearance(a);
            IsLight=mode=="light"||(mode=="system"&&(a.Resources.Configuration.UiMode&Android.Content.Res.UiMode.NightMask)!=Android.Content.Res.UiMode.NightYes);
            a.SetTheme(IsLight?Resource.Style.BavarianAmberLight:Resource.Style.BavarianAmberDark);
        }
        internal static void ApplyWindow(Activity a)
        {
            a.Window.SetStatusBarColor(Bg);a.Window.SetNavigationBarColor(Bg);
            if(a.GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetBoolean("fullscreen",false))
                a.Window.SetFlags(WindowManagerFlags.Fullscreen,WindowManagerFlags.Fullscreen);
            else a.Window.ClearFlags(WindowManagerFlags.Fullscreen);
        }
        internal static void Feedback(View v)
        {
            if(v.Context.GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetBoolean("haptics",false))
                v.PerformHapticFeedback(FeedbackConstants.VirtualKey);
        }
        internal static int Dp(Context c,int n)=>(int)(n*c.Resources.DisplayMetrics.Density+0.5f);
        internal static GradientDrawable Shape(Context c,Color color,int radius=14)
        {
            var s=new GradientDrawable();s.SetColor(color);s.SetCornerRadius(Dp(c,radius));if(color.Equals(Surface))s.SetStroke(Dp(c,1),Line);return s;
        }
        internal static bool Animations(Context c)=>c.GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetBoolean("animations",true)&&(!OperatingSystem.IsAndroidVersionAtLeast(26)||Android.Animation.ValueAnimator.AreAnimatorsEnabled());
        internal static Drawable TouchSurface(Context c,Color color,int radius=14)
        {
            return new RippleDrawable(ColorStateList.ValueOf(Color.ParseColor("#30FF781F")),Shape(c,color,radius),Shape(c,Color.White,radius));
        }
        internal static void Enter(View view)
        {
            if(!Animations(view.Context))return;
            view.Animate().Cancel();view.Alpha=0.80f;view.TranslationY=Dp(view.Context,5);
            view.Animate().Alpha(1f).TranslationY(0f).SetDuration(150).Start();
        }
        internal static TextView Text(Context c,string text,int size,bool title=false)
        {
            var t=new TextView(c){Text=text,TextSize=size};t.SetTextColor(title?White:Muted);
            t.SetTypeface(Typeface.Create(title?"sans-serif-medium":"sans-serif",TypefaceStyle.Normal),TypefaceStyle.Normal);return t;
        }
        internal static void Style(Button b,bool primary=false)
        {
            var c=b.Context;b.SetAllCaps(false);b.TextSize=15;b.SetTextColor(White);
            var states=new StateListDrawable();
            states.AddState(new[]{-Android.Resource.Attribute.StateEnabled},Shape(c,Line,10));
            states.AddState(new[]{Android.Resource.Attribute.StatePressed},Shape(c,primary?Color.ParseColor("#E86A16"):Selected,10));
            states.AddState(Array.Empty<int>(),Shape(c,primary?Primary:Surface,10));
            b.Background=new RippleDrawable(ColorStateList.ValueOf(Color.ParseColor("#28FF781F")),states,Shape(c,Color.White,10));b.SetPadding(Dp(c,14),Dp(c,10),Dp(c,14),Dp(c,10));b.SetMinHeight(Dp(c,46));
            b.SetTextColor(new ColorStateList(new[]{new[]{-Android.Resource.Attribute.StateEnabled},Array.Empty<int>()},new[]{Muted.ToArgb(),(primary?OnPrimary:White).ToArgb()}));
        }
        internal static Button Action(LinearLayout parent,string label,EventHandler click,bool primary=false)
        {
            var b=new Button(parent.Context){Text=label};Style(b,primary);b.Click+=(s,e)=>{Feedback(b);click(s,e);};
            parent.AddView(b,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(parent.Context,8)});return b;
        }
        internal static void Checkbox(CheckBox b)
        {
            b.SetTextColor(White);b.ButtonTintList=ColorStateList.ValueOf(Blue);
        }
        internal static LinearLayout Shell(Activity a,int selected,bool ru,Action<int> navigate,out LinearLayout body)
        {
            ApplyWindow(a);
            var root=new LinearLayout(a){Orientation=Orientation.Vertical};root.SetBackgroundColor(Bg);root.SetFitsSystemWindows(true);
            var scroll=new ScrollView(a){FillViewport=true};
            body=new LinearLayout(a){Orientation=Orientation.Vertical};int pad=Compact(a)?12:18;body.SetPadding(Dp(a,pad),Dp(a,pad),Dp(a,pad),Dp(a,20));
            scroll.AddView(body);root.AddView(scroll,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,0,1));
            var divider=new View(a);divider.SetBackgroundColor(Line);root.AddView(divider,new LinearLayout.LayoutParams(-1,Dp(a,1)));
            var nav=new LinearLayout(a){Orientation=Orientation.Horizontal};nav.SetBackgroundColor(Surface);
            string[] labels=ru?new[]{"Главная","Диагностика","Данные","Сервис","Настройки"}:new[]{"Home","Diagnostics","Live Data","Service","Settings"};
            
            for(int i=0;i<5;i++)
            {
                int index=i;
                var cell=new LinearLayout(a){Orientation=Orientation.Vertical};cell.SetGravity(GravityFlags.Center);cell.SetPadding(0,Dp(a,5),0,Dp(a,5));
                var icon=new NavIcon(a,i,i==selected?Blue:Muted);cell.AddView(icon,new LinearLayout.LayoutParams(Dp(a,25),Dp(a,25)));
                var label=Text(a,labels[i],9,i==selected);label.SetTextColor(i==selected?Blue:Muted);label.Gravity=GravityFlags.Center;cell.AddView(label);
                cell.Background=TouchSurface(a,i==selected?Selected:Surface,10);cell.ContentDescription=labels[i];cell.Clickable=true;cell.Focusable=true;cell.Click+=(s,e)=>{Feedback(cell);navigate(index);};
                var indicator=new View(a);indicator.Background=Shape(a,i==selected?Blue:Surface,2);cell.AddView(indicator,new LinearLayout.LayoutParams(Dp(a,18),Dp(a,3)){TopMargin=Dp(a,3)});nav.AddView(cell,new LinearLayout.LayoutParams(0,Dp(a,64),1));
            }
            root.AddView(nav,new LinearLayout.LayoutParams(-1,-2));a.SetContentView(root);return root;
        }
        private sealed class NavIcon:View
        {
            private readonly int _kind;private readonly Paint _p=new Paint(PaintFlags.AntiAlias);
            public NavIcon(Context c,int kind,Color color):base(c){_kind=kind;_p.Color=color;}
            protected override void OnDraw(Canvas canvas)
            {
                base.OnDraw(canvas);canvas.Save();canvas.Scale(Width/24f,Height/24f);_p.SetStyle(Paint.Style.Stroke);_p.StrokeWidth=1.8f;_p.StrokeCap=Paint.Cap.Round;_p.StrokeJoin=Paint.Join.Round;
                void L(float x,float y,float a,float b)=>canvas.DrawLine(x,y,a,b,_p);
                if(_kind==0){L(3,10,12,3);L(12,3,21,10);canvas.DrawRect(5,10,19,21,_p);canvas.DrawRect(10,15,14,21,_p);}
                else if(_kind==1){canvas.DrawRoundRect(5,3,19,21,2,2,_p);L(9,8,15,8);L(9,12,15,12);L(9,16,13,16);}
                else if(_kind==2){canvas.DrawArc(new RectF(3,4,21,22),180,180,false,_p);L(12,13,17,8);canvas.DrawCircle(12,13,1,_p);L(5,17,19,17);}
                else if(_kind==3){canvas.DrawCircle(12,12,9,_p);L(12,7,12,17);L(7,12,17,12);}
                else{canvas.DrawCircle(12,12,7,_p);canvas.DrawCircle(12,12,3,_p);for(int i=0;i<8;i++){double t=i*Math.PI/4;L(12+(float)Math.Cos(t)*7,12+(float)Math.Sin(t)*7,12+(float)Math.Cos(t)*10,12+(float)Math.Sin(t)*10);}}
                canvas.Restore();
            }
        }
        internal static void NavigateHome(Activity a,int tab)
        {
            var intent=new Intent(a,typeof(BavarianHomeActivity));intent.PutExtra("bavarian_tab",tab);
            intent.AddFlags(ActivityFlags.ClearTop|ActivityFlags.SingleTop);a.StartActivity(intent);a.Finish();
        }
    }
}
#endif
