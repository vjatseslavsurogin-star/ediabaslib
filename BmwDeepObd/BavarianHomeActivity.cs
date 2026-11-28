#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.Linq;
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
        private bool _ru,_ready,_returning,_opening;
        private string _lastRenderSnapshot;
        private string _report="";
        private int _tab,_systemPage;
        private string _system="";
        private Handler _handler;
        private Java.Lang.Runnable _poll;
        private string T(string en,string ru)=>_ru?ru:en;
        private int Dp(int n)=>BavarianUi.Dp(this,n);
        protected override void OnCreate(Bundle savedInstanceState)
        {
            BavarianUi.ApplyTheme(this);base.OnCreate(savedInstanceState);SupportActionBar?.Hide();
            _ru=BavarianLanguage.IsRussian(this);BavarianLanguage.EnsureEngineEnglish();
            _tab=savedInstanceState?.GetInt("tab",0)??Intent?.GetIntExtra("bavarian_tab",0)??0;
            _system=savedInstanceState?.GetString("system")??"";_systemPage=savedInstanceState?.GetInt("system_page",0)??0;
            Render();_handler=new Handler(Looper.MainLooper);_poll=new Java.Lang.Runnable(()=>{RefreshStatus();_handler.PostDelayed(_poll,1000);});
            _=LoadLibraryAsync();
        }
        protected override void OnNewIntent(Intent intent){base.OnNewIntent(intent);_tab=intent?.GetIntExtra("bavarian_tab",0)??0;_system="";_systemPage=0;Render();}
        protected override void OnSaveInstanceState(Bundle state){state.PutInt("tab",_tab);state.PutString("system",_system);state.PutInt("system_page",_systemPage);base.OnSaveInstanceState(state);}
        protected override void OnResume(){base.OnResume();_opening=false;if(_returning&&(_tab==0||_tab==1)&&_lastRenderSnapshot!=OverviewSnapshot())Render(false);_returning=false;RefreshStatus();if(_poll!=null){_handler.RemoveCallbacks(_poll);_handler.PostDelayed(_poll,1000);}}
        protected override void OnPause(){_returning=true;if(_poll!=null)_handler.RemoveCallbacks(_poll);base.OnPause();}
        private string OverviewSnapshot(){var p=GetSharedPreferences("bavarian_overview",FileCreationMode.Private);return string.Join("|",p.All.OrderBy(k=>k.Key).Select(k=>k.Key+"="+k.Value));}
        private void Render(bool animate=true)
        {
            _lastRenderSnapshot=OverviewSnapshot();
            _tab=Math.Max(0,Math.Min(4,_tab));_gated.Clear();_libraryStatus=null;_connect=null;
            BavarianUi.Shell(this,_tab,_ru,i=>{if(i!=_tab||_system!=""){_tab=i;_system="";_systemPage=0;Render();}},out _body);
            if(_tab==4){var edition=BavarianUi.Text(this,"BAVARIAN DIAGNOSTICS  /  2.4.2",12,true);edition.SetTextColor(BavarianUi.Blue);_body.AddView(edition);}
            string[] titles=_ru?new[]{"Автомобиль","Диагностика","Live Data","Сервис","Настройки"}:new[]{"Vehicle","Diagnostics","Live Data","Service","Settings"};
            Text(_tab==1&&_system!=""?SystemName(_system):titles[_tab],30,true,8);
            if(_tab==0)
            {
                var connection=Card();_usb=BavarianUi.Text(this,"",14,true);connection.AddView(_usb);
                _connect=BavarianUi.Action(connection,T("Connect","Подключить"),(s,e)=>{var ep=BavarianConnection.Endpoint(this,Manager);if(BavarianConnection.Ready(this,Manager,ep))ChooseAdapter();else Connect();},true);
            }
            else
            {
                _usb=Text("",12,true,8);
                _usb.Clickable=true;_usb.Click+=(s,e)=>{_tab=0;_system="";Render();};
            }
            if(_tab==0)
            {
                var car=Card();
                car.Background=BavarianUi.Shape(this,BavarianUi.Surface,18);
                var profile=BavarianUi.Text(this,T("PERSONAL VEHICLE PROFILE","ЛИЧНЫЙ ПРОФИЛЬ"),11,true);profile.SetTextColor(BavarianUi.Blue);car.AddView(profile);
                car.AddView(BavarianUi.Text(this,"BMW 530d",30,true));
                car.AddView(BavarianUi.Text(this,"E60  ·  M57  ·  160 kW  ·  2005",14,true));
                var prefs=GetSharedPreferences("bavarian_overview",FileCreationMode.Private);
                var vin=prefs.GetString("vin","");
                car.AddView(BavarianUi.Text(this,string.IsNullOrEmpty(vin)?T("Profile selected manually · VIN not read","Профиль выбран вручную · VIN не считан"):T("Last read VIN: ","Последний считанный VIN: ")+vin,12));
                _libraryStatus=BavarianUi.Text(this,"",12);car.AddView(_libraryStatus);
                var check=BavarianUi.Action(_body,T("Scan vehicle","Сканировать автомобиль"),(s,e)=>ChooseScan(),true);_gated.Add(check);
                Route(T("ECU modules","Блоки ECU"),T("Equipment, VIN and vehicle profile","Оборудование, VIN и профиль"),ShowEquipment);
                if(GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetBoolean("metrics",true)){
                Section(T("Last recorded values","Последние записанные данные"));
                var faults=LocalFaultSummary(prefs.GetString("DDE_summary","—"));
                MetricPair(T("DDE fault records","Записи DDE"),faults,T("Coolant","ОЖ"),LastValue("coolant"));
                MetricPair(T("DDE voltage","Напряжение"),LastValue("voltage"),T("Boost · absolute","Наддув · абс."),LastValue("boost"));
                Text(T("Saved readings, not live. Times shown when available.","Сохранённые данные, не текущие. Время указано при наличии."),11,false,8);
                }
                if(Favorites().Any(id=>Features().Any(f=>f.Id==id))){Section(T("Favorites","Избранное"));foreach(var id in Favorites().Distinct())FeatureRoute(id);}
                Route(T("Edit favorites","Настроить избранное"),T("Optional shortcuts","Дополнительные ярлыки"),EditFavorites,false);
            }
            else if(_tab==1)
            {
                if(_system=="")
                {
                    Section(T("Fault diagnostics","Диагностика ошибок"));
                    var scan=BavarianUi.Action(_body,T("Scan vehicle","Сканировать автомобиль"),(s,e)=>ChooseScan(),true);_gated.Add(scan);
                    Route(T("Fault memory actions","Действия с ошибками"),T("MIL and verified DDE / EGS clearing","MIL и подтверждённое удаление DDE / EGS"),()=>OpenFeature("service"));
                    Route(T("ECU modules","Блоки ECU"),T("Equipment, VIN and vehicle profile","Оборудование, VIN и профиль"),ShowEquipment);
                    Section(T("Individual systems","Отдельные системы"));SystemGrid();
                    Section(T("Additional checks","Дополнительные проверки"));
                    FeatureRoute("mileage");
                    Route(T("Reports & history","Отчёты и история"),T("Saved scans and PDF export","Сохранённые проверки и PDF"),ShowReports,false);
                    Route(T("Search functions","Поиск функций"),T("Find by name or ECU","По названию или ECU"),SearchFunctions,false);
                }
                else RenderSystem();
            }
            else if(_tab==2)
            {
                Section(T("Live dashboards","Приборные панели"));
                Route(T("Engine Live Data","Параметры двигателя"),T("Sensors and cylinder corrections","Датчики и коррекции цилиндров"),ShowEngineData);
                FeatureRoute("live_egs");
                Section(T("Recordings","Записи"));
                FeatureRoute("logs");FeatureRoute("demo");
            }
            else if(_tab==3)
            {
                Section(T("Maintenance & actions","Обслуживание и операции"));
                FeatureRoute("battery_read");FeatureRoute("cbs");
                Section(T("Engine & transmission","Двигатель и трансмиссия"));FeatureRoute("ima");FeatureRoute("adaptations");
                Text(T("Write operations require separate confirmation. CBS is read-only.","Операции записи требуют отдельного подтверждения. CBS — только чтение."),12,false,12);
            }
            else
            {
                Section(T("Appearance","Внешний вид"));
                var appearance=BavarianUi.Appearance(this);
                Route(T("Theme","Тема"),appearance=="light"?T("Light","Светлая"):appearance=="dark"?T("Dark","Тёмная"):T("System","Системная"),()=>new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Theme","Тема")).SetSingleChoiceItems(new[]{T("System","Системная"),T("Light","Светлая"),T("Dark","Тёмная")},appearance=="light"?1:appearance=="dark"?2:0,(d,a)=>{GetSharedPreferences("bavarian_home",FileCreationMode.Private).Edit().PutString("appearance",new[]{"system","light","dark"}[a.Which]).Apply();(d as IDialogInterface)?.Dismiss();Recreate();}).Show(),false);
                AppearanceToggle("compact",T("Compact layout","Компактный интерфейс"),false);
                AppearanceToggle("fullscreen",T("Full screen · hide status bar","Полный экран · скрыть строку состояния"),false);
                AppearanceToggle("haptics",T("Haptic feedback","Тактильная отдача"),false);
                Section(T("General","Общие"));
                Route(T("Language","Язык"),_ru?"Русский":"English",()=>new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("Language / Язык").SetSingleChoiceItems(new[]{"English","Русский"},_ru?1:0,(d,a)=>{BavarianLanguage.SetRussian(this,a.Which==1);_ru=a.Which==1;(d as IDialogInterface)?.Dismiss();Render();}).Show(),false);
                Route(T("Connection type","Тип подключения"),BavarianConnection.IsBluetooth(this)?"Bluetooth · experimental":"K+DCAN",ChooseAdapter,false);
                Route(T("Connection log","Журнал соединения"),T("Local event history","Локальная история"),()=>new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Connection log","Журнал соединения")).SetMessage(BavarianConnection.History(this)).SetPositiveButton("OK",(d,a)=>{}).Show(),false);
                Route(T("Animations","Анимации"),GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetBoolean("animations",true)?T("Enabled","Включены"):T("Disabled","Выключены"),()=>{var p=GetSharedPreferences("bavarian_home",FileCreationMode.Private);p.Edit().PutBoolean("animations",!p.GetBoolean("animations",true)).Apply();Render(false);},false);
                Route(T("Home widgets","Карточки главной"),T("Show or hide saved values","Показать или скрыть данные"),()=>{var p=GetSharedPreferences("bavarian_home",FileCreationMode.Private);p.Edit().PutBoolean("metrics",!p.GetBoolean("metrics",true)).Apply();Render();},false);
                Section(T("Engineering","Инженерный раздел"));
                Route("DDE",T("Identification and raw ECU details","Идентификация и данные блока"),()=>Open("engine"));
                Route("EGS",T("Identification and raw ECU details","Идентификация и данные блока"),()=>Open("transmission"));
                Route(T("Developer mode","Инженерный режим"),T("Manual PRG selection","Ручной выбор PRG"),()=>Open("developer"));
                Route(T("ECU library","Библиотека ECU"),T("Offline inspection","Просмотр без подключения"),()=>StartActivity(new Intent(this,typeof(BavarianEcuActivity))),false);
                _libraryStatus=Text("",12,false,12);
                Text("Based on Deep OBD / EdiabasLib · GPL-3.0",12,false,20);
            }
            RefreshStatus();UpdateLibrary();if(animate)BavarianUi.Enter(_body);
        }
        private void SystemGrid()
        {
            string[] ids={"engine","gearbox","power","chassis","climate","body"};
            int columns=Resources.Configuration.Orientation==Android.Content.Res.Orientation.Landscape?3:2;
            LinearLayout row=null;
            for(int i=0;i<ids.Length;i++)
            {
                string id=ids[i];
                if(i%columns==0){row=new LinearLayout(this){Orientation=Orientation.Horizontal};_body.AddView(row,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(8)});}
                var card=new LinearLayout(this){Orientation=Orientation.Vertical};
                card.SetPadding(Dp(12),Dp(12),Dp(12),Dp(12));card.Background=BavarianUi.TouchSurface(this,BavarianUi.Surface,14);
                var code=BavarianUi.Text(this,id switch {"engine"=>"DDE","gearbox"=>"EGS","power"=>"IBS","chassis"=>"DSC","climate"=>"IHKA",_=>"CAS / LM"},13,true);code.SetTextColor(BavarianUi.Blue);card.AddView(code);
                var title=BavarianUi.Text(this,SystemName(id),15,true);title.SetMinLines(2);card.AddView(title);
                card.Clickable=true;card.Focusable=true;card.ContentDescription=SystemName(id)+". "+SystemSubtitle(id);
                card.Click+=(s,e)=>{BavarianUi.Feedback(card);_tab=1;_system=id;_systemPage=0;Render();};
                row.AddView(card,new LinearLayout.LayoutParams(0,-2,1){RightMargin=Dp(4)});
            }
        }
        private void AppearanceToggle(string key,string label,bool fallback)
        {
            var p=GetSharedPreferences("bavarian_home",FileCreationMode.Private);
            bool enabled=p.GetBoolean(key,fallback);
            Route(label,enabled?T("Enabled","Включено"):T("Disabled","Выключено"),()=>{p.Edit().PutBoolean(key,!enabled).Apply();Render(false);},false);
        }
        private string SystemName(string id)=>id switch
        {
            "engine"=>T("Engine","Двигатель"),"gearbox"=>T("Transmission","Трансмиссия"),"power"=>T("Power supply","Электропитание"),
            "chassis"=>T("Chassis","Шасси"),"climate"=>T("Climate","Климат"),_=>T("Body & electronics","Кузов и электроника")
        };
        private string SystemSubtitle(string id)=>id switch
        {
            "engine"=>T("DDE · faults, sensors, injectors","DDE · ошибки, параметры, форсунки"),
            "gearbox"=>T("EGS · faults, live data, adaptations","EGS · ошибки, данные, адаптации"),
            "power"=>T("IBS · battery history and charge","IBS · история АКБ и заряд"),
            "chassis"=>T("DSC · fault memory","DSC · память ошибок"),
            "climate"=>T("IHKA · fault memory","IHKA · память ошибок"),
            _=>T("CAS, KOMBI, LM, CCC, PDC","CAS, KOMBI, LM, CCC, PDC")
        };
        private (string Id,string Title,string Sub)[] Features()=>new[]{
            ("health",T("Extended vehicle check","Расширенная проверка"),T("10 groups + battery, CBS, mileage, injector records","10 групп + АКБ, CBS, пробег, форсунки")),
            ("discovery",T("ECU equipment & profile","Оборудование и профиль ECU"),T("Discover 24 groups · VIN · map","Поиск 24 групп · VIN · карта")),
            ("smart_scan",T("Smart Scan","Умное сканирование"),T("VIN-matched equipment profile","Профиль оборудования по VIN")),
            ("vehicle_scan",T("Full fault scan","Полное чтение ошибок"),T("24 candidate ECU groups · read only","24 группы-кандидата · только чтение")),
            ("faults",T("Quick scan · DDE + EGS","Быстрая проверка · DDE + EGS"),"DDE / EGS"),
            ("fault_engine",T("Engine fault memory","Ошибки двигателя"),"DDE"),
            ("fault_gearbox",T("Transmission fault memory","Ошибки АКПП"),"EGS"),
            ("fault_chassis",T("Chassis fault memory","Ошибки шасси"),"DSC"),
            ("fault_climate",T("Climate fault memory","Ошибки климата"),"IHKA"),
            ("fault_body",T("Body fault memory","Ошибки кузова"),"CAS / KOMBI / LM / CCC / PDC"),
            ("live_engine",T("Engine dashboard","Параметры двигателя"),"DDE · Live Data"),
            ("live_egs",T("Transmission dashboard","Параметры АКПП"),"EGS · Live Data"),
            ("injectors",T("Cylinder corrections","Коррекции форсунок"),"DDE · Live Data"),
            ("ima",T("Injector codes","Коды форсунок"),"IMA · "+T("Read only","Чтение")),
            ("glow",T("Glow system","Свечи накаливания"),T("Fault records","Записи ошибок")),
            ("adaptations",T("Transmission adaptations","Адаптации АКПП"),"GS19A · "+T("Read only","Чтение")),
            ("battery_read",T("Battery management","Аккумулятор"),T("IBS · SOC / SOH · replacement","IBS · SOC / SOH · замена")),
            ("battery",T("Register new battery","Регистрация нового АКБ"),T("Read and register new battery","Чтение и регистрация нового АКБ")),
            ("cbs",T("CBS service","Сервисные интервалы"),"CBS · "+T("Read only","Чтение")),
            ("mileage",T("Mileage check","Проверка пробега"),"DDE / KOMBI / CAS"),
            ("ecu_info",T("ECU information","Информация о блоках"),"IDENT · DDE / EGS / KOMBI / CAS"),
            ("engine",T("Engineering · DDE","Инженерный раздел DDE"),"IDENT"),
            ("transmission",T("Engineering · EGS","Инженерный раздел EGS"),"IDENT"),
            ("service",T("Fault memory service","Обслуживание памяти ошибок"),T("MIL / confirmed DDE & EGS clear","MIL / подтверждённое удаление DDE и EGS")),
            ("history",T("Scan history","История диагностики"),T("Saved reports","Сохранённые отчёты")),
            ("pdf",T("PDF reports","Отчёты PDF"),T("Export","Экспорт")),
            ("logs",T("Saved logs","Сохранённые логи"),T("Offline charts and comparison","Графики и сравнение офлайн")),
            ("demo","DEMO",T("Simulated readings","Имитация данных"))
        };
        private bool Offline(string id)=>id=="history"||id=="pdf"||id=="logs"||id=="demo";
        private void OpenFeature(string id)
        {
            if(_opening||(!Offline(id)&&!_ready))return;
            if(id=="logs"||id=="pdf"){_opening=true;var i=new Intent(this,typeof(BavarianOfflineActivity));i.PutExtra("reports",id=="pdf");StartActivity(i);}
            else if(id=="demo"){_opening=true;StartActivity(new Intent(this,typeof(BavarianDemoActivity)));}
            else Open(id);
        }
        private void FeatureRoute(string id)
        {
            var f=Features().FirstOrDefault(x=>x.Id==id);if(f.Id==null)return;
            Route(f.Title,f.Sub,()=>OpenFeature(id),!Offline(id));
        }
        private void RenderSystem()
        {
            BavarianUi.Action(_body,T("‹ All systems","‹ Все системы"),(s,e)=>{_system="";_systemPage=0;Render();});
            string[] ids=_system switch
            {
                "engine"=>new[]{"fault_engine","glow"},
                "gearbox"=>new[]{"fault_gearbox"},
                "power"=>new[]{"battery_read"},
                "chassis"=>new[]{"fault_chassis"},
                "climate"=>new[]{"fault_climate"},
                _=>new[]{"fault_body"}
            };
            if(_system=="engine")ModuleSummary("DDE",T("Last saved scan","Последнее сохранённое чтение"));
            if(_system=="gearbox")ModuleSummary("EGS",T("Last saved scan","Последнее сохранённое чтение"));
            foreach(string id in ids)FeatureRoute(id);
        }
        private void ChooseScan()
        {
            if(!_ready)return;
            var ids=new[]{"smart_scan","faults","vehicle_scan","health"};
            var labels=new[]{
                T("Automatic · VIN profile or full scan","Авто · профиль VIN или полный опрос"),
                T("Quick · engine and transmission","Быстро · двигатель и АКПП"),
                T("Full · all 24 candidate groups","Полный · все 24 группы-кандидата"),
                T("Extended · 10 groups + service checks","Расширенный · 10 групп + сервисные проверки")
            };
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Scan vehicle","Сканирование автомобиля"))
                .SetItems(labels,(d,a)=>OpenFeature(ids[a.Which])).SetNegativeButton(T("Cancel","Отмена"),(d,a)=>{}).Show();
        }
        private void ShowEquipment()=>ChooseFeature(T("ECU modules","Блоки ECU"),new[]{"discovery","ecu_info"});
        private void ShowEngineData()=>ChooseFeature(T("Engine Live Data","Параметры двигателя"),new[]{"live_engine","injectors"});
        private void ShowReports()=>ChooseFeature(T("Reports & history","Отчёты и история"),new[]{"history","pdf"});
        private void ChooseFeature(string title,string[] ids)
        {
            var features=ids.Select(id=>Features().First(f=>f.Id==id)).ToArray();
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(title)
                .SetItems(features.Select(f=>f.Title+" · "+f.Sub).ToArray(),(d,a)=>OpenFeature(features[a.Which].Id))
                .SetNegativeButton(T("Cancel","Отмена"),(d,a)=>{}).Show();
        }
        private void SearchFunctions()
        {
            var input=new EditText(this){Hint=T("Search by name or ECU","Название или ECU")};input.SetSingleLine(true);
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Search functions","Поиск функций")).SetView(input)
                .SetPositiveButton(T("Search","Найти"),(d,a)=>{
                    string q=(input.Text??"").Trim();
                    var hits=Features().Where(f=>(f.Title+" "+f.Sub+" "+f.Id).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
                    if(hits.Length==0){new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetMessage(T("No matching functions.","Ничего не найдено.")).SetPositiveButton("OK",(x,y)=>{}).Show();return;}
                    new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetItems(hits.Select(f=>f.Title+" · "+f.Sub).ToArray(),(x,y)=>OpenFeature(hits[y.Which].Id)).Show();
                }).SetNegativeButton(T("Cancel","Отмена"),(d,a)=>{}).Show();
        }
        private string[] Favorites()=>GetSharedPreferences("bavarian_home",FileCreationMode.Private).GetString("favorites","").Split(',',StringSplitOptions.RemoveEmptyEntries);
        private void EditFavorites()
        {
                var features=Features();var existing=Favorites();var checks=features.Select(f=>existing.Contains(f.Id)).ToArray();
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Favorites","Избранное"))
                    .SetMultiChoiceItems(features.Select(f=>f.Title).ToArray(),checks,(d,a)=>checks[a.Which]=a.IsChecked)
                    .SetPositiveButton(T("Save","Сохранить"),(d,a)=>{GetSharedPreferences("bavarian_home",FileCreationMode.Private).Edit().PutString("favorites",string.Join(",",features.Where((f,i)=>checks[i]).Take(6).Select(f=>f.Id))).Apply();Render();})
                    .SetNegativeButton(T("Cancel","Отмена"),(d,a)=>{}).Show();
        }
        public override void OnBackPressed()
        {
            if(_tab==1&&_system!=""){_system="";_systemPage=0;Render();return;}
            if(_tab!=0){_tab=0;_system="";Render();return;}
            base.OnBackPressed();
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
            row.Background=BavarianUi.TouchSurface(this,BavarianUi.Surface,14);row.SetPadding(Dp(14),Dp(BavarianUi.Compact(this)?8:12),Dp(14),Dp(BavarianUi.Compact(this)?8:12));row.SetMinimumHeight(Dp(BavarianUi.Compact(this)?54:65));
            var bar=new View(this);bar.Background=BavarianUi.Shape(this,BavarianUi.Blue,2);row.AddView(bar,new LinearLayout.LayoutParams(Dp(3),Dp(30)){RightMargin=Dp(12)});
            var labels=new LinearLayout(this){Orientation=Orientation.Vertical};
            labels.AddView(BavarianUi.Text(this,title,17,true));labels.AddView(BavarianUi.Text(this,subtitle,12));row.AddView(labels,new LinearLayout.LayoutParams(0,-2,1));
            var arrow=BavarianUi.Text(this,"›",24,true);arrow.SetTextColor(BavarianUi.Blue);row.AddView(arrow);
            row.Clickable=true;row.Focusable=true;row.ContentDescription=title+". "+subtitle;row.Click+=(s,e)=>{if(!gated||_ready){BavarianUi.Feedback(row);action();}};
            _body.AddView(row,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(8)});
            if(gated){_gated.Add(row);row.Enabled=_ready;row.Alpha=_ready?1f:0.55f;}
        }
        private string LocalFaultSummary(string value)=>!_ru?value.Replace(" / не прочитан","").Replace(" / нет распознанных записей",""):value.Replace("Read failed / не прочитан","Не прочитан").Replace("No recognized records / нет распознанных записей","Нет распознанных записей").Replace("Read failed","Не прочитан").Replace("No recognized records","Нет распознанных записей").Replace("Active ","Активные ").Replace("Stored ","Сохранённые ").Replace("Other ","Прочие ");
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
                var parts=value.Split(new[]{'\n'},2);var t=BavarianUi.Text(this,parts[0],18,true);t.SetTextColor(BavarianUi.White);c.AddView(t);if(parts.Length>1)c.AddView(BavarianUi.Text(this,parts[1],11));
                row.AddView(c,new LinearLayout.LayoutParams(0,-2,1){RightMargin=Dp(4)});
            }
            Metric(label1,value1);Metric(label2,value2);
        }
        private void ModuleSummary(string module,string name)
        {
            var p=GetSharedPreferences("bavarian_overview",FileCreationMode.Private);
            var card=Card();card.AddView(BavarianUi.Text(this,module+" · "+name,18,true));
            var value=LocalFaultSummary(p.GetString(module+"_summary",""));
            card.AddView(BavarianUi.Text(this,string.IsNullOrEmpty(value)?T("Not scanned","Не проверен"):value,15));
            card.AddView(BavarianUi.Text(this,p.GetString(module+"_time",""),12));
        }
        private void Open(string mode)
        {
            if(_opening)return;_opening=true;
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
                _usb.SetTextColor(ready?BavarianUi.Warning:BavarianUi.Muted);
                var past=GetSharedPreferences("bavarian_overview",FileCreationMode.Private);
                string last=past.GetString("last_ecu_reply",""),lastTime=past.GetString("last_ecu_reply_time","");
                if(!string.IsNullOrEmpty(last))
                {
                    _usb.Text=(ready?T("● Adapter available","● Адаптер доступен"):T("○ Not connected","○ Нет подключения"))+"\n"+T("Last successful ECU reply (historical): ","Последний успешный ответ ECU (история): ")+last+" · "+lastTime;
                }
                if(_connect!=null){_connect.Visibility=ViewStates.Visible;_connect.Enabled=true;_connect.Text=ready?T("Connection settings","Управление подключением"):T("Connect","Подключить");}
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
