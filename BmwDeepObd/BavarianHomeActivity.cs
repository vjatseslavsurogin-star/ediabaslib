#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using EdiabasLib;
namespace BmwDeepObd
{
    [Activity(Label="@string/app_name",MainLauncher=true,Exported=true,Name=ActivityCommon.AppNameSpace+"."+nameof(BavarianHomeActivity))]
    public class BavarianHomeActivity : AppCompatActivity
    {
        private LinearLayout _body;
        private TextView _usb,_libraryStatus;
        private Button _connect;
        private readonly List<Button> _gated=new List<Button>();
        private bool _ru,_ready;
        private string _report="";
        private int _tab;
        private Handler _handler;
        private Java.Lang.Runnable _poll;
        private string T(string en,string ru)=>_ru?ru:en;
        private int Dp(int n)=>BavarianUi.Dp(this,n);
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);SupportActionBar?.Hide();
            _ru=BavarianLanguage.IsRussian(this);BavarianLanguage.EnsureEngineEnglish();
            _tab=savedInstanceState?.GetInt("tab",0)??Intent?.GetIntExtra("bavarian_tab",0)??0;
            Render();_handler=new Handler(Looper.MainLooper);_poll=new Java.Lang.Runnable(()=>{RefreshStatus();_handler.PostDelayed(_poll,1000);});
            _=LoadLibraryAsync();
        }
        protected override void OnNewIntent(Intent intent){base.OnNewIntent(intent);_tab=intent?.GetIntExtra("bavarian_tab",0)??0;Render();}
        protected override void OnSaveInstanceState(Bundle state){state.PutInt("tab",_tab);base.OnSaveInstanceState(state);}
        protected override void OnResume(){base.OnResume();RefreshStatus();if(_poll!=null){_handler.RemoveCallbacks(_poll);_handler.PostDelayed(_poll,1000);}}
        protected override void OnPause(){if(_poll!=null)_handler.RemoveCallbacks(_poll);base.OnPause();}
        private void Render()
        {
            _tab=Math.Max(0,Math.Min(4,_tab));_gated.Clear();_libraryStatus=null;
            BavarianUi.Shell(this,_tab,_ru,i=>{if(i!=_tab){_tab=i;Render();}},out _body);
            var brand=BavarianUi.Text(this,"BAVARIAN  /  1.7",12,true);brand.SetTextColor(BavarianUi.Blue);_body.AddView(brand);
            string[] titles=_ru?new[]{"Автомобиль","Диагностика","Live Data","Сервис","Настройки"}:new[]{"Vehicle","Diagnostics","Live Data","Service","Settings"};
            Text(titles[_tab],30,true,8);
            var connection=Card();_usb=BavarianUi.Text(this,"",14,true);connection.AddView(_usb);
            _connect=BavarianUi.Action(connection,T("Connect USB","Подключить USB"),(s,e)=>Connect(),true);
            if(_tab==0)
            {
                var car=Card();car.AddView(BavarianUi.Text(this,"BMW E60",26,true));
                car.AddView(BavarianUi.Text(this,"M57  ·  K+DCAN",15));
                car.AddView(BavarianUi.Text(this,T("Selected vehicle profile · not yet identified","Выбранный профиль · автомобиль ещё не определён"),12));
                _libraryStatus=BavarianUi.Text(this,"",13);car.AddView(_libraryStatus);
                Section(T("Quick actions","Быстрые действия"));
                Route(T("Vehicle scan","Диагностика авто"),T("Read supported ECU groups","Опрос поддерживаемых групп ECU"),()=>Open("vehicle_scan"));
                Route(T("Live dashboard","Приборная панель"),T("Engine sensor readings","Показания двигателя"),()=>Open("live_engine"));
            }
            else if(_tab==1)
            {
                Route(T("Vehicle scan","Диагностика авто"),T("10 ECU groups · experimental","10 групп ECU · тест"),()=>Open("vehicle_scan"));
                Route(T("Fault codes","Ошибки"),T("Engine & transmission · DDE / EGS","Двигатель и АКПП · DDE / EGS"),()=>Open("faults"));
                Text(T("Details and reports are available within each scan.","Подробности и отчёты доступны в результатах чтения."),13,false,12);
            }
            else if(_tab==2)
            {
                Route(T("Engine","Двигатель"),T("DDE · customizable live dashboard","DDE · настраиваемая панель"),()=>Open("live_engine"));
                Route(T("Transmission","АКПП"),T("EGS · temperatures and states","EGS · температуры и состояния"),()=>Open("live_egs"));
                Route(T("Cylinder corrections","Коррекции цилиндров"),T("Six-cylinder view · experimental","Шесть цилиндров · тест"),()=>Open("injectors"));
                Route("DEMO",T("Simulated readings · offline","Имитация данных · без авто"),()=>StartActivity(new Intent(this,typeof(BavarianDemoActivity))),false);
            }
            else if(_tab==3)
            {
                Route(T("Fault memory service","Обслуживание памяти ошибок"),T("Read MIL and clear DDE / EGS after a fresh scan","MIL и удаление ошибок после нового чтения"),()=>Open("service"));
                Route(T("Scan history","История диагностики"),T("Saved local reports","Сохранённые локальные отчёты"),()=>Open("history"),false);
                Text(T("Fault clearing requires confirmation and engine off.","Удаление ошибок требует подтверждения и остановленного двигателя."),13,false,12);
            }
            else
            {
                Route(T("Language","Язык"),_ru?"Русский":"English",()=>new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("Language / Язык").SetSingleChoiceItems(new[]{"English","Русский"},_ru?1:0,(d,a)=>{BavarianLanguage.SetRussian(this,a.Which==1);_ru=a.Which==1;(d as IDialogInterface)?.Dismiss();Render();}).Show(),false);
                Section(T("Engineering","Инженерный раздел"));
                Route("DDE",T("Identification and raw ECU details","Идентификация и данные блока"),()=>Open("engine"));
                Route("EGS",T("Identification and raw ECU details","Идентификация и данные блока"),()=>Open("transmission"));
                Route(T("Developer mode","Инженерный режим"),T("Manual PRG selection","Ручной выбор PRG"),()=>Open("developer"));
                Route(T("ECU library","Библиотека ECU"),T("Offline inspection","Просмотр без подключения"),()=>StartActivity(new Intent(this,typeof(BavarianEcuActivity))),false);
                _libraryStatus=Text("",12,false,12);
                Text("Based on Deep OBD / EdiabasLib · GPL-3.0",12,false,20);
            }
            RefreshStatus();UpdateLibrary();
        }
        private TextView Text(string text,int size,bool title,int top=4)
        {
            var t=BavarianUi.Text(this,text,size,title);_body.AddView(t,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(top)});return t;
        }
        private LinearLayout Card()
        {
            var card=new LinearLayout(this){Orientation=Orientation.Vertical};card.Background=BavarianUi.Shape(this,BavarianUi.Surface);
            card.SetPadding(Dp(16),Dp(14),Dp(16),Dp(14));_body.AddView(card,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(12)});return card;
        }
        private void Section(string text){var t=Text(text,13,true,20);t.SetTextColor(BavarianUi.Blue);}
        private void Route(string title,string subtitle,Action action,bool gated=true)
        {
            var b=BavarianUi.Action(_body,title+"  ›\n"+subtitle,(s,e)=>action());
            b.Gravity=GravityFlags.CenterVertical|GravityFlags.Start;b.SetMinHeight(Dp(68));
            if(gated){_gated.Add(b);b.Enabled=_ready;}
        }
        private void Open(string mode)
        {
            var intent=new Intent(this,typeof(BavarianVehicleActivity));intent.PutExtra("bavarian_mode",mode);StartActivity(intent);
        }
        private async Task LoadLibraryAsync()
        {
            try{_report=await Task.Run(()=>BavarianEcuSeed.Initialize(ApplicationContext));_ready=_report.StartsWith("E60 V74 ECU library: 675 / 675 verified",StringComparison.Ordinal);}
            catch(Exception){_report=T("ECU library check failed","Ошибка проверки библиотеки ECU");_ready=false;}
            if(!IsFinishing&&!IsDestroyed)UpdateLibrary();
        }
        private void UpdateLibrary(){if(_libraryStatus!=null)_libraryStatus.Text=string.IsNullOrEmpty(_report)?T("Checking ECU library…","Проверка библиотеки ECU…"):_report;foreach(var b in _gated)b.Enabled=_ready;}
        private UsbManager Manager=>GetSystemService(UsbService) as UsbManager;
        private UsbDevice Device(UsbManager manager)
        {
            if(manager==null)return null;var drivers=EdFtdiInterface.GetDriverList(manager);return drivers.Count==1?drivers[0].Device:null;
        }
        private void RefreshStatus()
        {
            if(_usb==null||IsFinishing||IsDestroyed)return;
            try
            {
                var manager=Manager;var d=Device(manager);bool ready=d!=null&&manager.HasPermission(d);
                _usb.Text=ready?T("● USB ready · ECU not checked","● USB готов · ECU не проверен"):d==null?T("○ Connect one K+DCAN adapter","○ Подключите один K+DCAN"):T("○ USB permission required","○ Требуется разрешение USB");
                _usb.SetTextColor(ready?BavarianUi.Blue:BavarianUi.Muted);
                _connect.Visibility=ready?ViewStates.Gone:ViewStates.Visible;_connect.Enabled=d!=null;
            }
            catch(Exception){_usb.Text=T("USB check failed","Ошибка USB");_connect.Enabled=false;}
        }
        private void Connect()
        {
            try
            {
                var m=Manager;var d=Device(m);if(d==null||m.HasPermission(d)){RefreshStatus();return;}
                var intent=new Intent(this,typeof(BavarianUsbPermissionReceiver));intent.SetAction(ActivityCommon.AppNameSpace+".BAVARIAN_USB");
                m.RequestPermission(d,PendingIntent.GetBroadcast(this,510,intent,OperatingSystem.IsAndroidVersionAtLeast(31)?PendingIntentFlags.Mutable:0));
            }
            catch(Exception){RefreshStatus();}
        }
    }
}
#endif
