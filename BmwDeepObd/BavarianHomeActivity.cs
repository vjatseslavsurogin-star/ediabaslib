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
        private readonly List<View> _gated=new List<View>();
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
        protected override void OnResume(){base.OnResume();Render();RefreshStatus();if(_poll!=null){_handler.RemoveCallbacks(_poll);_handler.PostDelayed(_poll,1000);}}
        protected override void OnPause(){if(_poll!=null)_handler.RemoveCallbacks(_poll);base.OnPause();}
        private void Render()
        {
            _tab=Math.Max(0,Math.Min(4,_tab));_gated.Clear();_libraryStatus=null;_connect=null;
            BavarianUi.Shell(this,_tab,_ru,i=>{if(i!=_tab){_tab=i;Render();}},out _body);
            var brand=BavarianUi.Text(this,"BAVARIAN  /  2.0  ·  PERSONAL",12,true);brand.SetTextColor(BavarianUi.Blue);_body.AddView(brand);
            string[] titles=_ru?new[]{"Автомобиль","Диагностика","Live Data","Инструменты","Настройки"}:new[]{"Vehicle","Diagnostics","Live Data","Tools","Settings"};
            Text(titles[_tab],30,true,8);
            if(_tab==0)
            {
                var connection=Card();_usb=BavarianUi.Text(this,"",14,true);connection.AddView(_usb);
                _connect=BavarianUi.Action(connection,T("Connect","Подключить"),(s,e)=>Connect(),true);
            }
            else
            {
                _usb=Text("",12,true,8);
                _usb.Clickable=true;_usb.Click+=(s,e)=>{_tab=0;Render();};
            }
            if(_tab==0)
            {
                var car=Card();
                var hero=new Android.Graphics.Drawables.GradientDrawable(Android.Graphics.Drawables.GradientDrawable.Orientation.TlBr,new[]{Android.Graphics.Color.ParseColor("#183D72").ToArgb(),Android.Graphics.Color.ParseColor("#13253C").ToArgb()});
                hero.SetCornerRadius(Dp(18));car.Background=hero;
                var profile=BavarianUi.Text(this,T("PERSONAL VEHICLE PROFILE","ЛИЧНЫЙ ПРОФИЛЬ"),11,true);profile.SetTextColor(BavarianUi.Blue);car.AddView(profile);
                car.AddView(BavarianUi.Text(this,"BMW 530d",30,true));
                car.AddView(BavarianUi.Text(this,"E60  ·  M57  ·  160 kW  ·  2005",14,true));
                var prefs=GetSharedPreferences("bavarian_overview",FileCreationMode.Private);
                var vin=prefs.GetString("vin","");
                car.AddView(BavarianUi.Text(this,string.IsNullOrEmpty(vin)?T("Profile selected manually · VIN not read","Профиль выбран вручную · VIN не считан"):T("Last read VIN: ","Последний считанный VIN: ")+vin,12));
                _libraryStatus=BavarianUi.Text(this,"",12);car.AddView(_libraryStatus);
                if(GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetBoolean("metrics",true)){
                Section(T("Last recorded values","Последние записанные данные"));
                var faults=prefs.GetString("DDE_summary","—");
                MetricPair(T("DDE fault records","Записи DDE"),faults,T("Coolant","ОЖ"),LastValue("coolant"));
                MetricPair(T("DDE voltage","Напряжение"),LastValue("voltage"),T("Boost · absolute","Наддув · абс."),LastValue("boost"));
                Text(T("Saved readings, not live. Times shown when available.","Сохранённые данные, не текущие. Время указано при наличии."),11,false,8);
                }
                Section(T("Quick actions","Быстрые действия"));
                Route(T("Vehicle scan","Диагностика авто"),T("10 ECU groups","10 групп ECU"),()=>Open("vehicle_scan"));
                Route(T("Live dashboard","Приборная панель"),T("Engine · DDE","Двигатель · DDE"),()=>Open("live_engine"));
            }
            else if(_tab==1)
            {
                Section(T("Last scan · saved","Последнее чтение · сохранено"));
                ModuleSummary("DDE",T("Engine","Двигатель"));ModuleSummary("EGS",T("Transmission","АКПП"));
                Route(T("Vehicle scan","Сканировать авто"),T("10 ECU groups · experimental","10 групп ECU · тест"),()=>Open("vehicle_scan"));
                Route(T("Fault codes","Ошибки"),T("Engine & transmission · DDE / EGS","Двигатель и АКПП · DDE / EGS"),()=>Open("faults"));
                Route(T("History","История"),T("Saved reports","Сохранённые отчёты"),()=>Open("history"),false);
            }
            else if(_tab==2)
            {
                Route(T("Engine dashboard","Двигатель"),T("DDE · live gauges and graphs","DDE · приборы и графики"),()=>Open("live_engine"));
                Route(T("Transmission","АКПП"),T("EGS · temperatures and converter","EGS · температура и гидротрансформатор"),()=>Open("live_egs"));
                Route(T("Transmission adaptations","Адаптации АКПП"),T("GS19A · read only · experimental","GS19A · только чтение · тест"),()=>Open("adaptations"));
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
                Route(T("Connection type","Тип подключения"),BavarianConnection.IsBluetooth(this)?"Bluetooth · experimental":"K+DCAN",ChooseAdapter,false);
                Route(T("Connection log","Журнал соединения"),T("Local event history","Локальная история"),()=>new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Connection log","Журнал соединения")).SetMessage(BavarianConnection.History(this)).SetPositiveButton("OK",(d,a)=>{}).Show(),false);
                Route(T("Home widgets","Карточки главной"),T("Show or hide saved values","Показать или скрыть данные"),()=>{var p=GetSharedPreferences("bavarian_home",FileCreationMode.Private);p.Edit().PutBoolean("metrics",!p.GetBoolean("metrics",true)).Apply();Render();},false);
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
            var row=new LinearLayout(this){Orientation=Orientation.Horizontal};row.SetGravity(GravityFlags.CenterVertical);
            row.Background=BavarianUi.Shape(this,BavarianUi.Surface);row.SetPadding(Dp(14),Dp(12),Dp(14),Dp(12));row.SetMinimumHeight(Dp(65));
            var bar=new View(this);bar.Background=BavarianUi.Shape(this,BavarianUi.Blue,2);row.AddView(bar,new LinearLayout.LayoutParams(Dp(3),Dp(30)){RightMargin=Dp(12)});
            var labels=new LinearLayout(this){Orientation=Orientation.Vertical};
            labels.AddView(BavarianUi.Text(this,title,17,true));labels.AddView(BavarianUi.Text(this,subtitle,12));row.AddView(labels,new LinearLayout.LayoutParams(0,-2,1));
            var arrow=BavarianUi.Text(this,"›",24,true);arrow.SetTextColor(BavarianUi.Blue);row.AddView(arrow);
            row.Clickable=true;row.Focusable=true;row.ContentDescription=title+". "+subtitle;row.Click+=(s,e)=>{if(!gated||_ready)action();};
            _body.AddView(row,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(8)});
            if(gated){_gated.Add(row);row.Enabled=_ready;row.Alpha=_ready?1f:0.55f;}
        }
        private string LastValue(string key)
        {
            var p=GetSharedPreferences("bavarian_overview",FileCreationMode.Private);
            string value=p.GetString(key,"—"),time=p.GetString(key+"_time","");
            return string.IsNullOrEmpty(time)?value:value+"\n"+time;
        }
        private void MetricPair(string label1,string value1,string label2,string value2)
        {
            var row=new LinearLayout(this){Orientation=Orientation.Horizontal};
            _body.AddView(row,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(8)});
            void Metric(string label,string value)
            {
                var c=new LinearLayout(this){Orientation=Orientation.Vertical};c.Background=BavarianUi.Shape(this,BavarianUi.Surface);
                c.SetPadding(Dp(12),Dp(10),Dp(12),Dp(10));c.AddView(BavarianUi.Text(this,label,12));
                var t=BavarianUi.Text(this,value,16,true);t.SetTextColor(BavarianUi.White);c.AddView(t);
                row.AddView(c,new LinearLayout.LayoutParams(0,-2,1){RightMargin=Dp(4)});
            }
            Metric(label1,value1);Metric(label2,value2);
        }
        private void ModuleSummary(string module,string name)
        {
            var p=GetSharedPreferences("bavarian_overview",FileCreationMode.Private);
            var card=Card();card.AddView(BavarianUi.Text(this,module+" · "+name,18,true));
            var value=p.GetString(module+"_summary","");
            card.AddView(BavarianUi.Text(this,string.IsNullOrEmpty(value)?T("Not scanned","Не проверен"):value,15));
            card.AddView(BavarianUi.Text(this,p.GetString(module+"_time",""),12));
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
        private void UpdateLibrary(){if(_libraryStatus!=null)_libraryStatus.Text=string.IsNullOrEmpty(_report)?T("Checking ECU library…","Проверка базы…"):_tab==0?(_ready?T("ECU library ready · 675 files","База ECU готова · 675 файлов"):T("ECU library not verified","База ECU не проверена")):_report;foreach(var b in _gated){b.Enabled=_ready;b.Alpha=_ready?1f:0.55f;}}
        private UsbManager Manager=>GetSystemService(UsbService) as UsbManager;
        private string _lastConnectionState;
        private void RefreshStatus()
        {
            if(_usb==null||IsFinishing||IsDestroyed)return;
            try
            {
                var endpoint=BavarianConnection.Endpoint(this,Manager);
                bool bt=BavarianConnection.IsBluetooth(this),ready=BavarianConnection.Ready(this,Manager,endpoint);
                string state=ready?(bt?"bt selected":"usb ready"):"not ready";
                if(_lastConnectionState!=state){_lastConnectionState=state;BavarianConnection.Log(this,state);}
                _usb.Text=ready?(bt?T("● Bluetooth selected · ECU not checked","● Bluetooth выбран · ECU не проверен"):T("● Adapter connected · ECU not checked","● Адаптер подключён · ECU не проверен")):T("○ Not connected","○ Нет подключения");
                _usb.SetTextColor(ready&&!bt?BavarianUi.Green:BavarianUi.Muted);
                if(_connect!=null){_connect.Visibility=ViewStates.Visible;_connect.Enabled=true;_connect.Text=T("Connect","Подключить");}
            }
            catch(Exception){_usb.Text=T("Connection check failed","Ошибка соединения");}
        }
        private void Connect()
        {
            if(!BavarianConnection.HasChoice(this)){ChooseAdapter();return;}
            if(BavarianConnection.IsBluetooth(this)){ChooseBluetooth();return;}
            ConnectUsb();
        }
        private void ChooseAdapter()
        {
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Connection type","Тип подключения"))
                .SetItems(new[]{"K+DCAN",T("Bluetooth · vLinker / OBDLink · experimental","Bluetooth · vLinker / OBDLink · тест")},(d,a)=>{
                    if(a.Which==0){BavarianConnection.SelectUsb(this);ConnectUsb();}else ChooseBluetooth();
                }).Show();
        }
        private void ChooseBluetooth()
        {
            if(!BavarianConnection.BtPermission(this))
            {
                RequestPermissions(new[]{"android.permission.BLUETOOTH_CONNECT","android.permission.BLUETOOTH_SCAN"},720);return;
            }
            try
            {
                var paired=BavarianConnection.Paired(this);
                if(paired.Length==0){new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetMessage(T("Enable Bluetooth and pair a Classic/SPP adapter in Android settings first.","Включите Bluetooth и выполните сопряжение Classic/SPP адаптера в настройках Android.")).SetPositiveButton("OK",(d,a)=>{}).Show();return;}
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Paired adapters · experimental","Сопряжённые адаптеры · тест"))
                    .SetItems(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(paired,p=>(p.Name??"Bluetooth")+" · "+p.Address)),(d,a)=>{
                        var selected=paired[a.Which];
                        new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetMessage(T("Experimental ELM-compatible Classic Bluetooth, read-only. BMW K-Line compatibility is not verified. Pairing is not an ECU connection.","Экспериментальный ELM-совместимый Classic Bluetooth, только чтение. Совместимость с K-Line BMW не проверена. Сопряжение не означает связь с ECU."))
                        .SetPositiveButton(T("Select","Выбрать"),(x,y)=>{BavarianConnection.SelectBluetooth(this,selected.Address);RefreshStatus();}).SetNegativeButton(T("Cancel","Отмена"),(x,y)=>{}).Show();
                    }).Show();
            }
            catch(Exception ex){BavarianConnection.Log(this,"Bluetooth selection failed: "+ex.GetType().Name);}
        }
        public override void OnRequestPermissionsResult(int requestCode,string[] permissions,Android.Content.PM.Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode,permissions,grantResults);
            if(requestCode==720&&BavarianConnection.BtPermission(this))ChooseBluetooth();
        }
        private void ConnectUsb()
        {
            try
            {
                var m=Manager;var d=BavarianConnection.Endpoint(this,m);
                if(d?.Usb==null){new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetMessage(T("Plug in exactly one K+DCAN adapter.","Подключите один K+DCAN адаптер.")).SetPositiveButton("OK",(x,y)=>{}).Show();return;}
                if(!m.HasPermission(d.Usb))
                {
                    var intent=new Intent(this,typeof(BavarianUsbPermissionReceiver));intent.SetAction(ActivityCommon.AppNameSpace+".BAVARIAN_USB");
                    m.RequestPermission(d.Usb,PendingIntent.GetBroadcast(this,510,intent,OperatingSystem.IsAndroidVersionAtLeast(31)?PendingIntentFlags.Mutable:0));
                }
                RefreshStatus();
            }
            catch(Exception ex){BavarianConnection.Log(this,"USB permission failed: "+ex.GetType().Name);RefreshStatus();}
        }
    }
}
#endif
