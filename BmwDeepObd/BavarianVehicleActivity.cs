#if BAVARIAN_USB_BASELINE
using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
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
    [BroadcastReceiver(Exported = false)]
    public class BavarianUsbPermissionReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent) { }
    }

    [Activity(Label = "Vehicle diagnostics", Exported = false,
        Name = ActivityCommon.AppNameSpace + "." + nameof(BavarianVehicleActivity))]
    public partial class BavarianVehicleActivity : AppCompatActivity
    {
        private static readonly SemaphoreSlim TransportLock = new SemaphoreSlim(1, 1);
        private static readonly Dictionary<string,string> Approved = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["d50m57c0.prg"]="9da5becea95792b7befba23aa4731bc8cb0ef9ae4f857a5565af595e2a0ecbfe",
            ["d50m57d0.prg"]="75751edc17d26199a2300015fc93d2f91d3ad6bd9c208a6595f0e274b615adc4",
            ["d50m57e0.prg"]="7d3964204c5f6c9f6ba630585d7754ea57b5c5a5adc1cb8d3177a0b2f19370a4",
            ["d60m57a0.prg"]="5e9ac1f8ff66975d22d74c16a5d3904f3b6177c5610f48ad3cac0e33400fd58c",
            ["d60m57d0.prg"]="05039bb238c7ebc4bd8acbda18530e8f4936537cafbec86ea8d1a49c179fbfe0",
            ["d62m57a0.prg"]="2c8597349da72e11780d6c1ec2029fab152ac55640c11fa0379de7cbce95d082",
            ["d62m57b0.prg"]="5470bf3de295eca45d903fd1434ec4a1c2714aa300d4b1dce6c19884fc65af32",
            ["gs19.prg"]="48b54e3e6505f53e28e4ac827a420e630eea620dd403488afe1c027ebd6cb235",
            ["gs19a.prg"]="a4e8e1280a46db684607a5456f8831683eb7ef563236b544ca388c44dd58c266",
            ["gs19b.prg"]="a7fd76d1ca9a7c8947df65d03ee038e995af5f3afc9ab0aa321e37f8f11bb0bd",
            ["gs19d.prg"]="a49b6a6f0d036faaa47a6cc34dcb9d1f3b71affa62f37228dfd841cf28ec5b1e"
        };
        private static readonly Dictionary<string,string> Groups = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["d_motor.grp"]="f5646d3147a8e96d6a4c9b40eae746f8f1a0d23e0a7897f94c2583790bbac73a",
            ["d_egs.grp"]="9d59ca58c01d052fc5680e6f9030ac52a95924a0b60143b4c331f3795ebf7df4"
        };
        private string _group, _resolved;
        private bool _health;
        private string[] _scopedGroups;
        private bool _faultScreen, _fullScan, _dashboard, _injectors, _serviceScreen, _adaptations;
        private Button _adaptButton;
        private Button _cancelScan,_actions;
        private CheckBox _readDetails;
        private Button _milButton,_clearDde,_clearEgs,_historyButton;
        private TextView _milText;
        private readonly Dictionary<string,(string Variant,string Identity)> _scanIdentities=new Dictionary<string,(string,string)>();
        private string _scanDevice;
        private string HistoryDir=>Path.Combine(FilesDir.AbsolutePath,"bavarian-history");
        private Button _chooseSignals;
        private TextView _selectedSummary;
        private string[] _selectedSignals;
        private string[] BaseSignals=>_group=="d_egs.grp"?GearboxJobs:EngineJobs;
        private string[] AvailableSignals=>BaseSignals.Concat(_group=="d_egs.grp"?GearboxExtra:EngineExtra).Distinct(StringComparer.Ordinal).ToArray();
        private static bool ExtraFor(string variant,string job)=>
            (variant=="d50m57e0.prg"&&EngineExtra.Contains(job))||(variant=="gs19a.prg"&&GearboxExtra.Contains(job));
        private Button _scanAll;
        private BavarianFaultPanel _faultPanel;
        private TextView _detected;
        private bool AutoMode => _group != null;
        private static bool VariantMatches(string group,string name) =>
            BavarianScanCatalog.IsExtended(group)?BavarianScanCatalog.TryVariantHash(group,name,out _):
            Approved.ContainsKey(name) && (group=="d_motor.grp" ? name.StartsWith("d",StringComparison.Ordinal) :
            group=="d_egs.grp" && name.StartsWith("gs19",StringComparison.Ordinal));
        private static readonly string[] EngineJobs =
        {
            "STATUS_MOTORDREHZAHL","STATUS_KUEHLMITTELTEMPERATUR","STATUS_LADEDRUCK_IST",
            "STATUS_LADEDRUCK_SOLL","STATUS_RAILDRUCK_IST","STATUS_RAILDRUCK_SOLL","STATUS_UBATT"
        };
        private static readonly string[] GearboxJobs = { "STATUS_GETRIEBETEMPERATUR", "STATUS_ISTGANG" };
        private static readonly string[] EngineExtra = { "STATUS_LUFTMASSE_IST", "STATUS_LUFTMASSE_SOLL", BavarianLiveFrame.InjectorJob, "STATUS_ATMOSPHAERENDRUCK", "STATUS_ANSAUGLUFTTEMPERATUR", "STATUS_KRAFTSTOFFTEMPERATUR", "STATUS_PWG_POTI_1" };
        private static readonly string[] AdaptationJobs={"STATUS_ADAPTIONSWERTE_PF","STATUS_ADAPTIONSWERTE_SF","STATUS_ADAPTIONSWERTE_PFN"};
        private static readonly string[] GearboxExtra = { "STATUS_WH_POSITION", "STATUS_GETRIEBEPOSITION", "STATUS_TURBINENDREHZAHL", "STATUS_MOTORDREHZAHL", "STATUS_WK", "STATUS_ABTRIEBSDREHZAHL", "STATUS_FAHRPEDALWINKEL", "STATUS_MOTORISTMOMENT", "STATUS_MOTORSOLLMOMENT", "STATUS_BATTERIESPANNUNG" };
        private Button _live, _stopLive, _extra, _csv;
        private BavarianLivePanel _panel;
        private string _csvExport;
        private bool _liveRunning;
        private int _liveGeneration;
        private string CsvDir=>Path.Combine(FilesDir.AbsolutePath,"bavarian-csv");
        private string CsvPrefix=>_group=="d_egs.grp"?"egs":"dde";
        private const int CsvRequest=611;
        private LinearLayout _body;
        private Spinner _variant;
        private TextView _state, _output;
        private CheckBox _confirm;
        private string _fingerprint;
        private readonly Handler _handler = new Handler(Looper.MainLooper);
        private Java.Lang.Runnable _poll;
        private string _activeDevice;
        private bool _visible;
        private int _ok, _failed;
        private TextView _stats;
        private string LogPath => Path.Combine(FilesDir.AbsolutePath,"bavarian-test.log");
        private Button _permission, _ident, _faults, _snapshot;
        private string _identified, _deviceName;
        private bool _busy, _ru;
        private CancellationTokenSource _cancel;
        private string Root => Path.Combine(FilesDir.AbsolutePath,"local-ecu");
        private string T(string en,string ru)=>_ru?ru:en;
        private int Dp(int n)=>(int)(n*Resources.DisplayMetrics.Density+0.5f);

        protected override void OnCreate(Bundle savedInstanceState)
        {
            BavarianUi.ApplyTheme(this);base.OnCreate(savedInstanceState);
            _ru=BavarianLanguage.IsRussian(this);
            string requested=Intent?.GetStringExtra("bavarian_mode");
            if(requested=="discovery"){CreateDiscovery();return;}
            _smartScan=requested=="smart_scan";
            if(IsMaintenanceMode(requested)){CreateMaintenance(requested);return;}
            _health=requested=="health";
            _scopedGroups=requested switch {"fault_engine"=>new[]{"d_motor.grp"},"fault_gearbox"=>new[]{"d_egs.grp"},"fault_chassis"=>new[]{"d_dsc.grp"},"fault_climate"=>new[]{"d_klima.grp"},"fault_body"=>new[]{"d_cas.grp","d_kombi.grp","d_lm.grp","d_ccc.grp","d_pdc.grp"},_=>null};
            _fullScan=requested=="vehicle_scan"||_health||_smartScan;
            _serviceScreen=requested=="service";
            _faultScreen=requested=="faults"||requested=="history"||_fullScan||_serviceScreen||_scopedGroups!=null;
            _injectors=requested=="injectors";_adaptations=requested=="adaptations";
            _dashboard=requested=="live_engine"||requested=="live_egs"||_injectors;
            _group=requested=="engine"||requested=="live_engine"||_injectors?"d_motor.grp":requested=="transmission"||requested=="live_egs"||_adaptations?"d_egs.grp":null;
            if(_dashboard)
            {
                string saved=GetSharedPreferences("bavarian_dashboard",FileCreationMode.Private)?.GetString(_group,"");
                _selectedSignals=AvailableSignals.Where(j=>(saved??"").Split(',').Contains(j)).ToArray();
                if(_selectedSignals.Length==0)_selectedSignals=BaseSignals.ToArray();
                if(_injectors)_selectedSignals=new[]{"STATUS_MOTORDREHZAHL",BavarianLiveFrame.InjectorJob};
            }
            SupportActionBar?.Hide();
            BavarianUi.Shell(this,_serviceScreen?3:_faultScreen?1:_dashboard?2:4,_ru,i=>{
                if(_busy){_output.Text=T("Stop the current request before navigating.","Сначала остановите текущий запрос.");return;}
                BavarianUi.NavigateHome(this,i);
            },out _body);
            _busyIndicator=new ProgressBar(this,null,Android.Resource.Attribute.ProgressBarStyleHorizontal){Indeterminate=true,Visibility=ViewStates.Gone};
            _busyIndicator.IndeterminateTintList=Android.Content.Res.ColorStateList.ValueOf(BavarianUi.Blue);_body.AddView(_busyIndicator,new LinearLayout.LayoutParams(-1,Dp(4)));
            if(_faultScreen)
            {
                AddText(_health?T("Vehicle health check","Комплексная проверка"):_scopedGroups!=null?string.Join(" / ",_scopedGroups.Select(BavarianScanCatalog.Module)):_serviceScreen?T("Service · DDE / EGS","Сервис · DDE / EGS"):_fullScan?T("Vehicle scan · E60","Диагностика автомобиля · E60"):T("Fault codes · DDE / EGS","Ошибки · DDE / EGS"),26);
                AddText(_health?T("Read-only fault scan and available battery, mileage, CBS and injector records. Not a complete safety or health assessment.","Чтение ошибок и доступных данных АКБ, пробега, CBS и форсунок. Не является полной оценкой исправности или безопасности."):_scopedGroups!=null?T("Read-only fault memory for selected systems.","Чтение памяти ошибок выбранных систем."):_smartScan?T("Automatic: VIN-matched profile, otherwise all 24 candidate groups. Park safely, ignition on.","Авто: профиль VIN, иначе все 24 группы-кандидата. Стоянка, зажигание включено."):_fullScan?T("Full read-only scan of 24 candidate ECU groups. Groups may overlap; non-response does not prove absence. Park safely, ignition on.","Чтение 24 групп-кандидатов ECU. Группы могут пересекаться; отсутствие ответа не доказывает отсутствие блока. Стоянка, зажигание включено."):T("Read-only scan of engine and transmission. Park safely, ignition on.","Чтение двигателя и АКПП без записи. Автомобиль должен стоять, зажигание включено."),15);
                _state=AddText("",15);
                _permission=AddButton(T("Check USB / grant permission","Проверить USB / разрешение"),(s,e)=>RequestUsb());
                _readDetails=new CheckBox(this){Text=T("Extra fault details · DDE/EGS (test)","Подробности ошибок · DDE/EGS (тест)")};
                BavarianUi.Checkbox(_readDetails);_body.AddView(_readDetails);
                _scanAll=AddButton(_health?T("Start check","Начать проверку"):_scopedGroups!=null?T("Read faults","Прочитать ошибки"):_fullScan?T("Scan vehicle","Сканировать автомобиль"):T("Scan DDE + EGS","Прочитать DDE + EGS"),async(s,e)=>await ScanFaultsAsync());
                BavarianUi.Style(_scanAll,true);
                _cancelScan=AddButton(T("Stop request","Остановить запрос"),(s,e)=>_cancel?.Cancel());
                _output=AddText("",14);
                _faultPanel=new BavarianFaultPanel(this,_ru);
                _body.AddView(_faultPanel);
                _milText=AddText(T("MIL: not read","Check Engine: не считан"),16);
                _milButton=AddButton(T("Read MIL · OBD Mode 01","Читать Check Engine · OBD Mode 01"),async(s,e)=>await ReadMilAsync());
                _clearDde=AddButton(T("Clear DDE fault memory","Удалить ошибки DDE"),(s,e)=>ConfirmClear("d_motor.grp"));
                _clearEgs=AddButton(T("Clear EGS fault memory","Удалить ошибки EGS"),(s,e)=>ConfirmClear("d_egs.grp"));
                _historyButton=AddButton(T("Scan history","История диагностики"),(s,e)=>ShowHistory());
                var share=AddButton(T("Share developer report","Поделиться инженерным отчётом"),(s,e)=>ShareReport());
                _milButton.Visibility=_historyButton.Visibility=share.Visibility=ViewStates.Gone;
                _clearDde.Visibility=_clearEgs.Visibility=ViewStates.Gone;
                _clearHint=AddText(T("Read DDE and EGS first. Clearing is only available for verified DDE/EGS over USB.","Сначала прочитайте DDE и EGS. Удаление доступно только для подтверждённых DDE/EGS через USB."),12);
                _actions=AddButton(T("More actions  ···","Действия  ···"),(s,e)=>ShowFaultActions());
                _poll=new Java.Lang.Runnable(()=>{if(!_visible)return;Refresh();_handler.PostDelayed(_poll,1000);});
                Refresh();
                if(requested=="history")_handler.Post(new Java.Lang.Runnable(ShowHistory));
                return;
            }
            AddText(_injectors?T("Cylinder smooth running","Коррекции цилиндров"):_dashboard?(_group=="d_motor.grp"?"Live Data · DDE":"Live Data · EGS"):AutoMode ? (_group=="d_motor.grp"?T("Engine · DDE","Двигатель · DDE"):T("Transmission · EGS","АКПП · EGS")) : T("Developer mode · USB","Инженерный режим · USB"),24);
            AddText("READ ONLY / EXPERIMENTAL",14);
            AddText(AutoMode?T("Park safely, ignition on. Connect one K+DCAN cable. The ECU variant is detected automatically.","Автомобиль должен стоять, зажигание включено. Подключите один K+DCAN. Вариант ECU определяется автоматически."):T("Park safely, ignition on. Select a known ECU variant. Connect only one K+DCAN cable.","Автомобиль должен стоять, зажигание включено. Выберите известный вариант ECU. Подключите один K+DCAN."),15);
            _state=AddText("",15);
            _permission=AddButton(T("Check USB / grant permission","Проверить USB / разрешение"),(s,e)=>RequestUsb());
            _variant=new Spinner(this);
            var variants=Approved.Keys.Where(n=>File.Exists(Path.Combine(Root,n))).ToArray();
            var adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerItem,variants);
            adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _variant.Adapter=adapter;
            if(!AutoMode)_body.AddView(_variant);
            _detected=AddText("",14);
            if(AutoMode)
            {
                string last=GetSharedPreferences("bavarian_ident",FileCreationMode.Private)?.GetString(_group,null);
                _detected.Text=last==null?T("No ECU identified yet.","Блок ещё не определён."):T("Last detected (not verified now): ","Ранее обнаружен (сейчас не проверен): ")+last;
            }
            _ident=AddButton(AutoMode?T("Connect / detect ECU","Подключить / определить ECU"):T("Identify ECU (IDENT)","Идентификация ECU"),async(s,e)=>await RunAsync("IDENT"));
            _confirm=new CheckBox(this){Text=T("I checked the identification and selected ECU variant","Я проверил идентификацию и выбранный вариант ECU"),Enabled=false};
            BavarianUi.Checkbox(_confirm);if(!AutoMode)_body.AddView(_confirm);
            _confirm.CheckedChange+=(s,e)=>Refresh();
            _faults=AddButton(T("Read fault memory","Чтение ошибок"),async(s,e)=>await RunAsync("FS_LESEN"));
            _snapshot=AddButton(T("Read parameters once","Прочитать параметры"),async(s,e)=>await RunAsync("SNAPSHOT"));
            if(_dashboard){_faults.Visibility=ViewStates.Gone;_snapshot.Visibility=ViewStates.Gone;}
            if(_dashboard)
            {
                if(!_injectors)_chooseSignals=AddButton(T("Edit sensor selection","Выбрать параметры"),(s,e)=>ChooseSignals());
                if(_injectors)AddText(T("Experimental quantity corrections. Test at warm idle while parked. No automatic injector diagnosis.","Экспериментальные коррекции количества топлива. Проверка на прогретом холостом ходу. Не является автоматическим диагнозом форсунок."),14);
                _selectedSummary=AddText("",14);UpdateSelectionLabel();
            }
            _live=AddButton(T("Start monitoring","Начать мониторинг"),async(s,e)=>{if(_dashboard&&_identified==null)await RunAsync("IDENT");if(_identified!=null&&_visible&&!IsFinishing)await RunAsync("LIVE");});
            BavarianUi.Style(_live,true);
            if(_dashboard)_ident.Visibility=ViewStates.Gone;
            _stopLive=AddButton(T("Stop live data","Остановить Live Data"),(s,e)=>_cancel?.Cancel());
            _extra=AddButton(T("Extra signals · experimental","Дополнительные сигналы · эксперимент"),async(s,e)=>await RunAsync("EXTRA"));
            if(_dashboard)_extra.Visibility=ViewStates.Gone;
            _csv=AddButton(T("Export latest CSV","Сохранить последний CSV"),(s,e)=>ExportCsv());
            if(_adaptations)_adaptButton=AddButton(T("Read adaptations · raw ECU fields","Читать адаптации · поля ECU"),async(s,e)=>{if(_identified==null)await RunAsync("IDENT");if(_visible&&!IsFinishing&&!IsDestroyed&&_resolved=="gs19a.prg")await RunAsync("ADAPTATIONS");else _output.Text=T("GS19A required","Требуется GS19A");});
            _panel=new BavarianLivePanel(this,_group=="d_egs.grp");
            if(_dashboard){var viewMode=AddButton(T("View: gauges / tiles / list","Вид: приборы / плитки / список"),(s,e)=>{if(!_busy)_panel.ToggleGauges();});}
            _panel.Visibility=_dashboard?ViewStates.Visible:ViewStates.Gone;
            if(_dashboard)_panel.ShowSelection(_selectedSignals);
            _body.AddView(_panel);
            var disconnect=AddButton(T("Cancel / disconnect","Отмена / отключение"),(s,e)=>{ResetIdentity();_cancel?.Cancel();Refresh();});
            if(_dashboard)disconnect.Visibility=ViewStates.Gone;
            _stats=AddText("",13);
            var shareReport=AddButton(T("Share developer report","Поделиться инженерным отчётом"),(s,e)=>ShareReport());
            var clearReport=AddButton(T("Clear local test report","Очистить локальный отчёт"),(s,e)=>{
                if(_busy)return;
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetMessage("Delete local diagnostic report?").SetPositiveButton("Delete",(d,a)=>{try{File.Delete(LogPath);_ok=_failed=0;Refresh();}catch(Exception ex){_output.Text=BavarianLanguage.Error("Report deletion failed",ex);}}).SetNegativeButton("Cancel",(d,a)=>{}).Show();
            });
            if(_dashboard){shareReport.Visibility=clearReport.Visibility=_stats.Visibility=ViewStates.Gone;_actions=AddButton(T("More actions  ···","Действия  ···"),(s,e)=>{if(!_busy)ShareReport();});}
            _output=AddText("",14);
            _poll=new Java.Lang.Runnable(()=>{if(!_visible)return;Refresh();_handler.PostDelayed(_poll,1000);});
            if(!AutoMode)_variant.ItemSelected+=(s,e)=>{ResetIdentity();Refresh();};
            Refresh();
        }

        protected override void OnResume(){base.OnResume();_visible=true;if(_state!=null)Refresh();if(_poll!=null){_handler.RemoveCallbacks(_poll);_handler.PostDelayed(_poll,1000);}}
        protected override void OnStop(){_maintenanceIdentity=null;_maintenanceReadAt=0;_visible=false;_scanIdentities.Clear();if(_poll!=null)_handler.RemoveCallbacks(_poll);_cancel?.Cancel();ResetIdentity();base.OnStop();}
        private void ResetIdentity(){bool had=_identified!=null;_identified=null;_deviceName=null;_fingerprint=null;_resolved=null;if(had&&_detected!=null)_detected.Text=T("ECU not currently verified.","Блок сейчас не подтверждён.");if(_confirm!=null&&_confirm.Checked)_confirm.Checked=false;}
        private static readonly object LogLock=new object();
        private void Log(string message)
        {
            lock(LogLock)
            {
                try
                {
                    if(File.Exists(LogPath)&&new FileInfo(LogPath).Length>512*1024)File.Delete(LogPath);
                    File.AppendAllText(LogPath,DateTimeOffset.UtcNow.ToString("o",CultureInfo.InvariantCulture)+" "+message+"\n",Encoding.UTF8);
                }
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
        }
        private void ShareReport()
        {
            try
            {
                if(_busy){_output.Text="Finish or cancel the request before sharing.";return;}
                string report;
                lock(LogLock){report=File.Exists(LogPath)?File.ReadAllText(LogPath):"No diagnostic requests recorded.";}
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("Share diagnostic report")
                    .SetMessage("The report may contain vehicle identifiers and ECU responses. Share only with a trusted recipient.")
                    .SetPositiveButton("Share",(d,a)=>{
                        var send=new Intent(Intent.ActionSend);send.SetType("text/plain");
                        send.PutExtra(Intent.ExtraSubject,"Bavarian V2.3 diagnostic report");
                        send.PutExtra(Intent.ExtraText,report);
                        StartActivity(Intent.CreateChooser(send,"Share report"));
                    }).SetNegativeButton("Cancel",(d,a)=>{}).Show();
            }
            catch(Exception ex){_output.Text=BavarianLanguage.Error("Report sharing failed",ex);}
        }
        private ProgressBar _busyIndicator;
        private TextView _clearHint;
        private TextView AddText(string text,int size)
        {
            var v=BavarianUi.Text(this,text,size,size>=24);
            v.SetTextIsSelectable(true);
            var lp=new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(12)};
            _body.AddView(v,lp);return v;
        }
        private Button AddButton(string label,EventHandler click)
        {
            return BavarianUi.Action(_body,label,click);
        }
        private UsbManager Manager=>GetSystemService(UsbService) as UsbManager;
        private BavarianEndpoint GetDevice(UsbManager manager)=>BavarianConnection.Endpoint(this,manager);
        private bool Ready(UsbManager manager,BavarianEndpoint endpoint)=>BavarianConnection.Ready(this,manager,endpoint);
        private string Selected=>_group??_variant?.SelectedItem?.ToString();
        private void Refresh()
        {
            if(_discovery){RefreshDiscovery();return;}
            if(_busyIndicator!=null)_busyIndicator.Visibility=_busy?ViewStates.Visible:ViewStates.Gone;
            if(_maintenanceMode!=null){RefreshMaintenance();return;}
            bool permitted=false;
            try
            {
                var manager=Manager; var device=GetDevice(manager);
                permitted=device!=null && Ready(manager,device);
                if(device==null || device.DeviceName!=_deviceName || !permitted){ResetIdentity();}
                if(_busy&&(!permitted||device?.DeviceName!=_activeDevice))_cancel?.Cancel();
                if(_faultScreen&&(!permitted||(_scanDevice!=null&&device?.DeviceName!=_scanDevice)))_scanIdentities.Clear();
                _state.SetTextColor(_busy?BavarianUi.Blue:!permitted?BavarianUi.Muted:_identified==null?BavarianUi.Warning:BavarianUi.Green);
                _state.Text=_busy?T("● Reading…","● Чтение…"):!permitted?T("○ Not connected · Home","○ Нет подключения · Главная"):_identified==null?T("● Adapter connected · ECU not checked","● Адаптер подключён · ECU не проверен"):T("● ECU identified","● ECU определён");
            }
            catch(Exception){_state.Text="USB check failed.";ResetIdentity();_cancel?.Cancel();}
            _permission.Enabled=!_busy;
            _permission.Visibility=ViewStates.Gone;

            if(permitted&&!_busy&&BavarianConnection.IsBluetooth(this)&&_identified==null)_state.Text=T("● Bluetooth selected · experimental","● Bluetooth выбран · тест");
            if(_faultScreen)
            {
                _scanAll.Enabled=!_busy&&permitted;
                _scanAll.Visibility=_busy?ViewStates.Gone:ViewStates.Visible;
                _cancelScan.Visibility=_busy?ViewStates.Visible:ViewStates.Gone;
                _actions.Enabled=!_busy;
                _readDetails.Enabled=!_busy;
                _milButton.Enabled=_serviceScreen&&!_busy&&permitted;
                _clearDde.Enabled=!_busy&&permitted&&!BavarianConnection.IsBluetooth(this)&&_scanIdentities.ContainsKey("d_motor.grp");
                _clearEgs.Enabled=!_busy&&permitted&&!BavarianConnection.IsBluetooth(this)&&_scanIdentities.ContainsKey("d_egs.grp")&&_scanIdentities.ContainsKey("d_motor.grp");
                _clearDde.Visibility=!_busy&&(_scopedGroups==null||_scopedGroups.Contains("d_motor.grp"))?ViewStates.Visible:ViewStates.Gone;
                _clearEgs.Visibility=!_busy&&(_scopedGroups==null||_scopedGroups.Contains("d_egs.grp"))?ViewStates.Visible:ViewStates.Gone;
                if(_clearHint!=null){_clearHint.Visibility=!_busy&&(_clearDde.Visibility==ViewStates.Visible||_clearEgs.Visibility==ViewStates.Visible)?ViewStates.Visible:ViewStates.Gone;_clearHint.Text=BavarianConnection.IsBluetooth(this)?T("Bluetooth is read-only.","Bluetooth: только чтение."):_scopedGroups!=null&&_scopedGroups.Contains("d_egs.grp")&&!_scanIdentities.ContainsKey("d_motor.grp")?T("To clear EGS, run the DDE + EGS scan first (engine-stop safety check).","Для удаления EGS сначала выполните чтение DDE + EGS (проверка остановки двигателя)."):T("A fresh scan is required. Fault history is saved before clearing; ignition on, engine off.","Нужно свежее чтение. История сохраняется до удаления; зажигание включено, двигатель заглушён.");}
                _historyButton.Enabled=!_busy;
                if(!_busy)_state.Text=permitted?(BavarianConnection.IsBluetooth(this)?T("● Bluetooth selected · experimental","● Bluetooth выбран · тест"):T("● Adapter connected · ready to scan","● Адаптер подключён · можно читать")):T("○ Not connected · Home","○ Нет подключения · Главная");
                return;
            }
            _variant.Enabled=!_busy;
            _ident.Enabled=!_busy&&permitted&&Selected!=null;
            bool identified=permitted&&Selected!=null&&Selected==_identified;
            _confirm.Enabled=!_busy&&identified;
            bool ready=!_busy&&identified&&(AutoMode||_confirm.Checked);
            _faults.Enabled=ready;_snapshot.Enabled=ready;
            bool verifiedVariant=_resolved=="d50m57e0.prg"||_resolved=="gs19a.prg";
            _live.Enabled=_dashboard?!_busy&&permitted&&(_identified==null||verifiedVariant):ready&&AutoMode&&verifiedVariant;
            if(_dashboard){_live.Visibility=_busy?ViewStates.Gone:ViewStates.Visible;_stopLive.Visibility=_busy?ViewStates.Visible:ViewStates.Gone;_actions.Enabled=!_busy;}
            _extra.Enabled=ready&&AutoMode&&verifiedVariant;
            if(_adaptButton!=null)_adaptButton.Enabled=_visible&&!_busy&&permitted&&(_identified==null||_resolved=="gs19a.prg");
            _stopLive.Enabled=_busy;
            if(_chooseSignals!=null){_chooseSignals.Enabled=!_busy;_chooseSignals.Visibility=_dashboard&&_busy?ViewStates.Gone:ViewStates.Visible;}
            if(_dashboard){_csv.Visibility=_busy?ViewStates.Gone:ViewStates.Visible;if(_selectedSummary!=null)_selectedSummary.Visibility=_busy?ViewStates.Gone:ViewStates.Visible;}
            _csv.Enabled=!_busy&&AutoMode&&LatestCsv()!=null;
            _stats.Text="Jobs: "+_ok+" OK / "+_failed+" failed (this screen)";
        }
        private void ShowFaultActions()
        {
            if(_busy)return;
            var labels=new List<string>();var actions=new List<Action>();
            void Add(Button button,Action action){if(button.Enabled&&button.Visibility!=ViewStates.Visible){labels.Add(button.Text);actions.Add(action);}}
            Add(_milButton,async()=>await ReadMilAsync());
            Add(_clearDde,()=>ConfirmClear("d_motor.grp"));
            Add(_clearEgs,()=>ConfirmClear("d_egs.grp"));
            Add(_historyButton,ShowHistory);
            labels.Add(T("Share developer report","Поделиться инженерным отчётом"));actions.Add(ShareReport);
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Actions","Действия")).SetItems(labels.ToArray(),(d,a)=>actions[a.Which]()).Show();
        }
        private void UpdateSelectionLabel()
        {
            _selectedSummary.Text=_selectedSignals.Length+" / "+AvailableSignals.Length+T(" sensor groups selected"," групп параметров выбрано");
        }
        private void ChooseSignals()
        {
            if(_busy||!_dashboard)return;
            var options=new LinearLayout(this){Orientation=Orientation.Vertical};
            options.SetPadding(Dp(16),Dp(8),Dp(16),Dp(8));
            var checks=AvailableSignals.Select(j=>new CheckBox(this){Text=BavarianLiveFrame.Label(j,_ru)+(BaseSignals.Contains(j)?"":T(" · experimental"," · тест")),Checked=_selectedSignals.Contains(j)}).ToArray();
            foreach(var check in checks)options.AddView(check);
            var scroll=new ScrollView(this);scroll.AddView(options);
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Select sensors","Выбор параметров")).SetView(scroll)
                .SetPositiveButton(T("Save","Сохранить"),(d,a)=>{
                    var selection=AvailableSignals.Where((j,i)=>checks[i].Checked).ToArray();
                    if(selection.Length==0){_output.Text=T("Select at least one sensor.","Выберите хотя бы один параметр.");return;}
                    _selectedSignals=selection;
                    GetSharedPreferences("bavarian_dashboard",FileCreationMode.Private)?.Edit()?.PutString(_group,string.Join(",",selection))?.Apply();
                    UpdateSelectionLabel();_panel.ShowSelection(selection);
                }).SetNegativeButton(T("Cancel","Отмена"),(d,a)=>{}).Show();
        }
        private string LatestCsv()
        {
            try {return Directory.Exists(CsvDir)?Directory.GetFiles(CsvDir,CsvPrefix+"-*.csv").OrderByDescending(x=>x,StringComparer.Ordinal).FirstOrDefault():null;}
            catch(IOException){return null;}
        }
        private void ExportCsv()
        {
            if(_busy)return;
            _csvExport=LatestCsv();
            if(_csvExport==null)return;
            var intent=new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);intent.SetType("text/csv");
            intent.PutExtra(Intent.ExtraTitle,Path.GetFileName(_csvExport));
            StartActivityForResult(intent,CsvRequest);
        }
        protected override void OnActivityResult(int requestCode,Result resultCode,Intent data)
        {
            base.OnActivityResult(requestCode,resultCode,data);
            if(requestCode!=CsvRequest||resultCode!=Result.Ok||data?.Data==null||_csvExport==null)return;
            try
            {
                using var input=File.OpenRead(_csvExport);
                using var output=ContentResolver.OpenOutputStream(data.Data,"wt");
                if(output==null)throw new IOException("Unable to open CSV destination");
                input.CopyTo(output);
                _output.Text=T("CSV exported.","CSV сохранён.");
            }
            catch(Exception ex){_output.Text=Describe(ex);}
        }
        private void RequestUsb(){BavarianUi.NavigateHome(this,0);}
        private async Task ScanFaultsAsync()
        {
            if(_busy)return;
            UsbManager manager;
            BavarianEndpoint device;
            try{manager=Manager;device=GetDevice(manager);}
            catch(Exception ex){_output.Text=Describe(ex);return;}
            if(device==null||!Ready(manager,device)){Refresh();return;}
            string[] targets=_scopedGroups??(_fullScan?(_health?BavarianScanCatalog.AllGroups:BavarianScanCatalog.DiscoveryGroups):new[]{"d_motor.grp","d_egs.grp"});
            _scanProfileNote="";
            bool details=_readDetails.Checked;
            _faultPanel.BeginScan(targets.Select(BavarianScanCatalog.Module).ToArray());
            _scanIdentities.Clear();_scanDevice=device.DeviceName;
            _busy=true;_activeDevice=device.DeviceName;_cancel=new CancellationTokenSource();
            _cancel.CancelAfter(TimeSpan.FromMinutes(_health?6:_fullScan?8:(_scopedGroups?.Length??0)>2?3:1));
            var token=_cancel.Token;
            bool incomplete=false;var extraReports=new List<string>();
            Refresh();_output.Text=T("Reading modules...","Чтение блоков...");
            try
            {
                if(_smartScan)
                {
                    _output.Text=T("Checking current VIN and saved discovery profile…","Проверка VIN и сохранённой конфигурации…");
                    try
                    {
                        using var preflight=CancellationTokenSource.CreateLinkedTokenSource(token);preflight.CancelAfter(TimeSpan.FromSeconds(20));
                        var cas=await Task.Run(()=>Execute(manager,device.DeviceName,"d_cas.grp",new[]{"IDENT"},null,null,preflight.Token,false,null,null,false,true));
                        if(!string.IsNullOrEmpty(cas.Vin))
                        {
                            var profile=DiscoveryProfile(cas.Vin);
                            if(profile.GetBoolean("saved",false)&&profile.GetBoolean("complete",false))
                            {
                                targets=BavarianScanCatalog.DiscoveryGroups.Where(g=>profile.GetString("observed_"+g,"")=="responded"||profile.GetString("owner_"+g,"")=="expected"||(BavarianScanCatalog.AllGroups.Contains(g)&&profile.GetString("owner_"+g,"")!="absent")).ToArray();
                                if(targets.Length>0)_scanProfileNote=T("VIN-matched saved profile; not a new discovery.","Профиль подтверждён по VIN; это не новый поиск оборудования.");
                                else targets=BavarianScanCatalog.DiscoveryGroups;
                            }
                        }
                    }
                    catch(Exception ex){Log("SMART PROFILE UNAVAILABLE "+Describe(ex));}
                    token.ThrowIfCancellationRequested();
                    if(string.IsNullOrEmpty(_scanProfileNote))_scanProfileNote=T("No matching VIN profile; full 24-group scan used.","Нет подходящего профиля VIN; выполнен полный опрос 24 групп.");
                    _faultPanel.BeginScan(targets.Select(BavarianScanCatalog.Module).ToArray());
                }
                foreach(string group in targets)
                {
                    if(token.IsCancellationRequested)break;
                    string module=BavarianScanCatalog.Module(group);
                    _output.Text=T("Reading ","Чтение ")+module;
                    using var moduleCancel=CancellationTokenSource.CreateLinkedTokenSource(token);
                    moduleCancel.CancelAfter(TimeSpan.FromSeconds(details&&(group=="d_motor.grp"||group=="d_egs.grp")?30:15));
                    try
                    {
                        Log("START FAULT_SCAN "+group);
                        var result=await Task.Run(()=>Execute(manager,device.DeviceName,group,new[]{"IDENT","FS_LESEN"},null,null,moduleCancel.Token,false,null,null,details,_fullScan));
                        token.ThrowIfCancellationRequested();
                        if(IsFinishing||IsDestroyed||!_visible)break;
                        _faultPanel.AddResult(module,result.Variant,result.FaultRows,result.ExtraDetails,result.Vin,result.ExtraNote);
                        SaveOverview(module,result.FaultRows,result.Vin);
                        if(!string.IsNullOrEmpty(result.Identity))RememberEcuReply(module+" · "+result.Variant);
                        if((result.Variant=="d50m57e0.prg"||result.Variant=="gs19a.prg")&&!string.IsNullOrEmpty(result.Identity)&&result.FaultRows!=null)_scanIdentities[group]=(result.Variant,result.Identity);
                    }
                    catch(Exception ex)
                    {
                        Log("FAULT_SCAN FAIL "+group+" "+Describe(ex));
                        if(IsFinishing||IsDestroyed||!_visible)break;
                        incomplete=true;
                        _faultPanel.AddFailure(module,moduleCancel.IsCancellationRequested?T("Cancelled / timed out","Отменено / тайм-аут"):Describe(ex));
                        SaveOverview(module,null,null);
                    }
                }
                if(_health)
                {
                    foreach(var check in new[]{"battery_read","mileage","cbs","ima"})
                    {
                        if(token.IsCancellationRequested||!_visible)break;
                        _output.Text=T("Reading: ","Чтение: ")+check;
                        using var step=CancellationTokenSource.CreateLinkedTokenSource(token);step.CancelAfter(TimeSpan.FromSeconds(45));
                        try
                        {
                            var report=await Task.Run(()=>ReadMaintenance(manager,device.DeviceName,check,false,null,step.Token));
                            extraReports.Add(check+"\n"+report.Text);incomplete|=report.Incomplete;
                        }
                        catch(Exception ex){incomplete=true;extraReports.Add(check+" · "+T("Not completed: ","Не завершено: ")+Describe(ex));}
                    }
                    if(extraReports.Count<4)incomplete=true;
                }
                if(!IsFinishing&&!IsDestroyed&&_visible)
                {
                    string reportText=(_smartScan?_scanProfileNote+"\n\n":"")+_faultPanel.ExportText()+(_health?"\n\n"+string.Join("\n\n",extraReports):"");
                    bool saved=SaveHistory(_health?T("Vehicle health check","Комплексная проверка"):T("Fault scan","Чтение ошибок"),reportText,incomplete||token.IsCancellationRequested);
                    _output.Text=token.IsCancellationRequested?T("Scan incomplete / cancelled.","Сканирование не завершено / отменено."):T("Scan finished. See results by module below.","Чтение завершено. Результаты по блокам ниже.");
                    if(_smartScan)_output.Text=_scanProfileNote+"\n"+_output.Text;
                    if(_health)_output.Text=(incomplete||token.IsCancellationRequested?T("Partial check · see unavailable readings.","Неполная проверка · есть недоступные данные."):T("Read-only check completed.","Чтение завершено."))+"\n\n"+string.Join("\n\n",extraReports);
                    if(saved)
                    {
                        GetSharedPreferences("bavarian_overview",FileCreationMode.Private).Edit()
                            .PutString("last_scan_time",DateTime.Now.ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture))
                            .PutString("last_scan_groups",_faultPanel.ReadGroupCount+"/"+_faultPanel.PlannedGroupCount)
                            .PutString("last_scan_records",_faultPanel.ReadGroupCount==0?"—":_faultPanel.RelevantRecordCount.ToString(CultureInfo.InvariantCulture))
                            .PutBoolean("last_scan_partial",incomplete||token.IsCancellationRequested).Apply();
                    }
                    else _output.Text+="\n"+T("History could not be saved.","История не сохранена.");
                }
            }
            finally
            {
                _cancel.Dispose();_cancel=null;_busy=false;_activeDevice=null;
                if(!IsFinishing&&!IsDestroyed)Refresh();
            }
        }
        private long _lastOverviewTick;
        private void SaveLiveOverview(BavarianLiveFrame frame)
        {
            if(frame.Demo||frame.Variant!="d50m57e0.prg")return;
            long now=SystemClock.ElapsedRealtime();if(now-_lastOverviewTick<2000)return;_lastOverviewTick=now;
            try
            {
                var edit=GetSharedPreferences("bavarian_overview",FileCreationMode.Private).Edit();
                foreach(var s in frame.Samples)
                {
                    string key=s.Job=="STATUS_KUEHLMITTELTEMPERATUR"?"coolant":s.Job=="STATUS_UBATT"?"voltage":s.Job=="STATUS_LADEDRUCK_IST"?"boost":null;
                    if(key==null||!BavarianLiveFrame.UnitMatches(s.Job,s.Unit))continue;
                    string unit=s.Unit=="deg C"?"°C":s.Unit=="hPa"?"mbar":s.Unit;
                    edit.PutString(key,s.Value.ToString(key=="voltage"?"0.00":"0.0",CultureInfo.InvariantCulture)+" "+unit);
                    edit.PutString(key+"_time",s.Time.ToLocalTime().ToString("dd.MM HH:mm",CultureInfo.InvariantCulture));
                }
                edit.Apply();
            }
            catch(Exception){ }
        }
        private void RememberEcuReply(string label)
        {
            try{GetSharedPreferences("bavarian_overview",FileCreationMode.Private).Edit().PutString("last_ecu_reply",label).PutString("last_ecu_reply_time",DateTime.Now.ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture)).Apply();}catch(Exception){ }
        }
        private void SaveOverview(string module,List<Dictionary<string,string>> rows,string vin)
        {
            try
            {
                var edit=GetSharedPreferences("bavarian_overview",FileCreationMode.Private).Edit();
                if(!string.IsNullOrEmpty(vin))edit.PutString("vin",vin);
                if(module=="DDE"||module=="EGS")
                {
                    var records=BavarianFaultPanel.ParseRecords(rows);
                    string State(Dictionary<string,string> r)=>r.TryGetValue("F_VERSION",out string ver)&&ver=="2"&&r.TryGetValue("F_VORHANDEN_NR",out string state)?state:"";
                    string summary=rows==null?"Read failed":records.Count==0?"No recognized records":
                        "Active "+records.Count(r=>State(r)=="35")+" · Stored "+records.Count(r=>State(r)=="33")+" · Other "+records.Count(r=>State(r)!="35"&&State(r)!="33");
                    edit.PutString(module+"_summary",summary);
                    edit.PutString(module+"_time",DateTime.Now.ToString("dd.MM HH:mm",CultureInfo.InvariantCulture));
                }
                edit.Apply();
            }
            catch(Exception){ }
        }
        private bool SaveHistory(string title,string report,bool incomplete)
        {
            try
            {
                Directory.CreateDirectory(HistoryDir);
                string name=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".txt";
                File.WriteAllText(Path.Combine(HistoryDir,name),DateTimeOffset.Now.ToString("o",CultureInfo.InvariantCulture)+"\n"+title+(incomplete?" · INCOMPLETE":"")+"\n\n"+report,Encoding.UTF8);
                foreach(var old in Directory.GetFiles(HistoryDir,"*.txt").OrderByDescending(x=>x,StringComparer.Ordinal).Skip(50))File.Delete(old);
                return true;
            }
            catch(Exception ex){Log("HISTORY FAIL "+ex.GetType().Name);return false;}
        }
        private void ShowHistory()
        {
            if(_busy)return;
            try
            {
                var files=Directory.Exists(HistoryDir)?Directory.GetFiles(HistoryDir,"*.txt").OrderByDescending(x=>x,StringComparer.Ordinal).ToArray():Array.Empty<string>();
                if(files.Length==0){_output.Text=T("No saved scans.","Нет сохранённых сессий.");return;}
                var labels=files.Select(x=>string.Join(" · ",File.ReadLines(x).Take(2))).ToArray();
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Scan history · local","История · локально")).SetItems(labels,(d,a)=>{
                    try
                    {
                        string report=File.ReadAllText(files[a.Which]);
                        new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(labels[a.Which]).SetMessage(report)
                            .SetPositiveButton("OK",(dd,aa)=>{}).SetNeutralButton(T("Share","Поделиться"),(dd,aa)=>{
                                var send=new Intent(Intent.ActionSend);send.SetType("text/plain");send.PutExtra(Intent.ExtraText,report);
                                StartActivity(Intent.CreateChooser(send,T("Share vehicle data only with trusted recipients","Передавайте данные авто только доверенным получателям")));
                            }).Show();
                    }
                    catch(Exception ex){_output.Text=Describe(ex);}
                }).SetNegativeButton(T("Close","Закрыть"),(d,a)=>{}).Show();
            }
            catch(Exception ex){_output.Text=Describe(ex);}
        }
        private async Task ReadMilAsync()
        {
            if(_busy)return;
            var manager=Manager;var device=GetDevice(manager);
            if(device==null||!Ready(manager,device)){Refresh();return;}
            _busy=true;_activeDevice=device.DeviceName;_cancel=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var token=_cancel.Token;
            _milText.Text=T("MIL: reading...","Check Engine: чтение...");Refresh();
            try
            {
                string result=await Task.Run(()=>ReadMil(manager,device.DeviceName,token));
                if(!IsFinishing&&!IsDestroyed&&_visible){_milText.Text=result;SaveHistory("OBD MIL",result,false);}
            }
            catch(Exception ex){if(!IsFinishing&&!IsDestroyed)_milText.Text=T("MIL: unknown / read failed","Check Engine: неизвестно / не прочитан")+"\n"+Describe(ex);}
            finally{_cancel.Dispose();_cancel=null;_busy=false;_activeDevice=null;if(!IsFinishing&&!IsDestroyed)Refresh();}
        }
        private string ReadMil(UsbManager manager,string deviceName,CancellationToken token)
        {
            if(!TransportLock.Wait(0))throw new InvalidOperationException("Transport busy");
            try
            {
                var device=GetDevice(manager);
                if(device==null||device.DeviceName!=deviceName||!Ready(manager,device))throw new IOException("Adapter changed");
                VerifyFile("obd.prg","a7601df89cfb54bb1f966e5d44a97a6e0d6de774df273f2286454bcfa5d891de");
                using var ediabas=new EdiabasNet();
                ediabas.SetConfigProperty("EcuPath",Root);ediabas.SgbdFileName="obd.prg";
                ediabas.AbortJobFunc=()=>token.IsCancellationRequested;
                var transport=new EdInterfaceObd();ediabas.EdInterfaceClass=transport;BavarianConnection.Configure(this,manager,device,transport);
                using var reg=token.Register(()=>transport.TransmitCancel(true));
                ediabas.ArgString="ALL;ID;0x01";ediabas.ArgBinaryStd=null;ediabas.ResultsRequests=string.Empty;
                token.ThrowIfCancellationRequested();ediabas.ExecuteJob("OBD_STATUS_LESEN_FUNKTIONAL");token.ThrowIfCancellationRequested();
                var rows=ediabas.ResultSets;
                if(rows==null||rows.Count<2)throw new IOException("No OBD response");
                var data=rows.Skip(1).Select(row=>row.ToDictionary(k=>k.Key,k=>k.Value.OpData is byte[] bytes?Convert.ToHexString(bytes):Convert.ToString(k.Value.OpData,CultureInfo.InvariantCulture))).ToList();
                string Get(Dictionary<string,string> r,string k)=>r.TryGetValue(k,out string value)?value??"":"";
                Log("OBD PID01\n"+string.Join("\n",data.SelectMany(r=>r.Select(k=>k.Key+": "+k.Value))));
                if(data.Any(r=>r.ContainsKey("JOB_STATUS")&&Get(r,"JOB_STATUS")!="OKAY"))throw new IOException("OBD job unsuccessful");
                var lamps=data.Where(r=>Get(r,"RESULT_NAME")=="MIL"&&(Get(r,"PID")=="1"||Get(r,"PID")=="0x01")).ToArray();
                if(lamps.Length==0)throw new IOException("No recognizable MIL result");
                return T("OBD MIL reported by ECU(s):","Статус MIL по данным ECU:")+"\n"+string.Join("\n",lamps.Select(r=>{
                    string val=Get(r,"RESULT_WERT").Trim().ToUpperInvariant();
                    string label=val=="MIL ON"||val=="MIL_STAT_ON"?T("ON","ВКЛ"):val=="MIL OFF"||val=="MIL_STAT_OFF"?T("OFF","ВЫКЛ"):T("Unknown","Неизвестно")+" ("+val+")";
                    return Get(r,"ECU_ADR")+" · "+label;
                }));
            }
            finally{TransportLock.Release();}
        }
        private void ConfirmClear(string group)
        {
            if(_busy||!_scanIdentities.TryGetValue(group,out var ident)||!_scanIdentities.ContainsKey("d_motor.grp")||BavarianConnection.IsBluetooth(this))return;
            string module=BavarianScanCatalog.Module(group);
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Clear faults · ","Удалить ошибки · ")+module)
                .SetMessage(T("Park, ignition ON, engine OFF. Clearing removes fault history and freeze-frame; DDE readiness may reset. Fix causes first. The current memory is saved locally before clearing. This is not a repair.","Стоянка, зажигание включено, двигатель заглушен. Удаляются ошибки и стоп-кадры; готовность OBD DDE может сброситься. Сначала устраните причину. Перед удалением память сохраняется локально. Это не ремонт."))
                .SetNegativeButton(T("Cancel","Отмена"),(d,a)=>{})
                .SetPositiveButton(T("Clear this ECU","Удалить в этом блоке"),async(d,a)=>await ClearModuleAsync(group,ident.Variant,ident.Identity)).Show();
        }
        private async Task ClearModuleAsync(string group,string variant,string identity)
        {
            if(_busy||!_scanIdentities.TryGetValue(group,out var ident)||ident.Identity!=identity||ident.Variant!=variant||!_scanIdentities.TryGetValue("d_motor.grp",out var engine))return;
            var manager=Manager;var device=GetDevice(manager);
            if(device==null||device.DeviceName!=_scanDevice||!Ready(manager,device)){Refresh();return;}
            _busy=true;_activeDevice=device.DeviceName;_cancel=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var token=_cancel.Token;
            _output.Text=T("Saving pre-clear scan...","Сохранение перед удалением...");Refresh();
            try
            {
                var check=await Task.Run(()=>Execute(manager,device.DeviceName,"d_motor.grp",new[]{"STATUS_MOTORDREHZAHL"},engine.Identity,engine.Variant,token,false,null,null));
                if(check.EngineRpm!=0)throw new IOException("Engine must be stopped; clear blocked");
                var before=await Task.Run(()=>Execute(manager,device.DeviceName,group,new[]{"FS_LESEN"},identity,variant,token,false,null,null,true));
                string raw=string.Join("\n",before.FaultRows.SelectMany(r=>r.Select(k=>k.Key+": "+k.Value)))+"\n"+(before.ExtraNote??"")+"\n"+string.Join("\n",before.ExtraDetails.SelectMany(p=>p.Value.SelectMany(r=>r.Select(k=>k.Key+": "+k.Value))));
                if(!SaveHistory("BEFORE CLEAR "+BavarianScanCatalog.Module(group),raw,false))throw new IOException("Pre-clear backup failed; nothing cleared");
                token.ThrowIfCancellationRequested();
                var after=await Task.Run(()=>Execute(manager,device.DeviceName,group,new[]{"CLEAR_CONFIRMED","FS_LESEN"},identity,variant,token,false,null,null));
                if(!IsFinishing&&!IsDestroyed&&_visible)
                {
                    _faultPanel.AddResult(BavarianScanCatalog.Module(group),after.Variant,after.FaultRows);
                    SaveOverview(BavarianScanCatalog.Module(group),after.FaultRows,null);
                    SaveHistory("AFTER CLEAR "+BavarianScanCatalog.Module(group),_faultPanel.ExportText(),false);
                    _output.Text=T("Clear acknowledged; memory re-read. Remaining records shown below.","Удаление подтверждено; память перечитана. Оставшиеся записи ниже.");
                }
            }
            catch(Exception ex){SaveOverview(BavarianScanCatalog.Module(group),null,null);Log("CLEAR FAIL "+Describe(ex));if(!IsFinishing&&!IsDestroyed)_output.Text=T("Clear not verified. Re-scan before retrying.","Удаление не подтверждено. Перечитайте память перед повтором.")+"\n"+Describe(ex);}
            finally{_scanIdentities.Remove(group);_cancel.Dispose();_cancel=null;_busy=false;_activeDevice=null;if(!IsFinishing&&!IsDestroyed)Refresh();}
        }
        private async Task RunAsync(string operation)
        {
            if(_busy)return;
            string name=Selected;
            if(name==null || !(AutoMode?Groups.ContainsKey(name):Approved.ContainsKey(name)))return;
            UsbManager manager; BavarianEndpoint device;
            try { manager=Manager; device=GetDevice(manager); }
            catch(Exception ex) { _output.Text=BavarianLanguage.Error("USB check failed",ex); return; }
            if(device==null || !Ready(manager,device)){Refresh();return;}
            if(operation!="IDENT" && (_identified!=name || _deviceName!=device.DeviceName || (!AutoMode&&!_confirm.Checked) || _fingerprint==null || _resolved==null))return;
            if(operation=="IDENT")ResetIdentity();
            string expected=_fingerprint;
            string expectedVariant=_resolved;
            bool live=operation=="LIVE", extra=operation=="EXTRA";
            if((live||extra)&&(!AutoMode||(expectedVariant!="d50m57e0.prg"&&expectedVariant!="gs19a.prg")))return;
            _liveRunning=live;int generation=++_liveGeneration;
            IProgress<BavarianLiveFrame> progress=null;
            string csvPath=null;
            if(live)
            {
                try
                {
                    Directory.CreateDirectory(CsvDir);
                    if(Directory.GetFiles(CsvDir).Length>=100)throw new IOException("Local session limit reached.");
                    csvPath=Path.Combine(CsvDir,CsvPrefix+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture)+".csv");
                }
                catch(Exception ex){_output.Text=Describe(ex);_liveRunning=false;return;}
                _panel.PrepareSession(_dashboard?_selectedSignals:BaseSignals);
                _panel.Visibility=ViewStates.Visible;
                progress=new Progress<BavarianLiveFrame>(frame=>{
                    if(!IsFinishing&&!IsDestroyed&&_visible&&_liveRunning&&generation==_liveGeneration){_panel.UpdateFrame(frame);SaveLiveOverview(frame);}
                });
            }
            else if(!_dashboard)_panel.Visibility=ViewStates.Gone;
            _activeDevice=device.DeviceName;
            Log("START "+operation+" SGBD="+name+" USB="+device.Name);
            _busy=true;_cancel=new CancellationTokenSource();
            _cancel.CancelAfter(live?TimeSpan.FromMinutes(10):TimeSpan.FromSeconds(45));
            var token=_cancel.Token;
            Refresh();_output.Text="Waiting for ECU response...";
            try
            {
                bool gearbox=name=="d_egs.grp"||name.StartsWith("gs19",StringComparison.Ordinal);
                string[] jobs=operation=="ADAPTATIONS"?AdaptationJobs:live&&_dashboard?_selectedSignals.ToArray():live||operation=="SNAPSHOT"?(gearbox?GearboxJobs:EngineJobs):extra?(gearbox?GearboxExtra:EngineExtra):new[]{operation};
                var result=await Task.Run(()=>Execute(manager,device.DeviceName,name,jobs,expected,expectedVariant,token,live,progress,csvPath));
                if(IsFinishing||IsDestroyed||!_visible)return;
                if(token.IsCancellationRequested&&!live){_output.Text=T("Request cancelled.","Запрос отменён.");return;}
                _output.Text=live?T("Live session ended. Export CSV to save the readings.","Сессия завершена. Сохрани показания через CSV.")+"\n"+result.Text:result.Text;
                _ok+=result.Ok;_failed+=result.Failed;
                if(operation=="IDENT")
                {
                    _identified=name;_deviceName=device.DeviceName;_fingerprint=result.Identity;_resolved=result.Variant;
                    _detected.Text=T("Detected ECU: ","Определённый блок: ")+result.Variant;
                    if(!string.IsNullOrEmpty(result.Identity))RememberEcuReply(result.Variant);
                    if(AutoMode)GetSharedPreferences("bavarian_ident",FileCreationMode.Private)?.Edit()?.PutString(_group,result.Variant)?.Apply();
                }
            }
            catch(Exception ex)
            {
                ResetIdentity();_failed++;
                string failure=token.IsCancellationRequested?(live?"Live session stopped. Partial CSV is available if samples were received.":"Diagnostic request cancelled or timed out."):Describe(ex);
                Log("FAIL "+failure);
                if(!IsFinishing&&!IsDestroyed)_output.Text=failure;
            }
            finally
            {
                _cancel.Dispose();_cancel=null;_busy=false;_activeDevice=null;_liveRunning=false;
                if(live&&!IsFinishing&&!IsDestroyed)_panel.MarkStopped();
                if(!IsFinishing&&!IsDestroyed)Refresh();
            }
        }
        private sealed class RequestResult
        {
            public string Text,Identity,Variant;
            public List<Dictionary<string,string>> FaultRows;
            public Dictionary<int,List<Dictionary<string,string>>> ExtraDetails=new Dictionary<int,List<Dictionary<string,string>>>();
            public string Vin,ExtraNote,VehicleOrder;
            public double? EngineRpm;
            public int Ok,Failed;
        }
        private static string Describe(Exception ex)
        {
            if(ex is EdiabasNet.EdiabasNetException ee)return "EDIABAS "+ee.ErrorCode+" — "+EdiabasNet.GetErrorDescription(ee.ErrorCode);
            if(ex is IOException || ex is InvalidOperationException)return ex.GetType().Name+": "+ex.Message;
            return BavarianLanguage.Error("Diagnostic request failed",ex);
        }
        private void VerifyFile(string name,string expectedHash)
        {
            using var stream=File.OpenRead(Path.Combine(Root,name));
            if(!string.Equals(Convert.ToHexString(SHA256.HashData(stream)),expectedHash,StringComparison.OrdinalIgnoreCase))
                throw new IOException("ECU file hash mismatch: "+name);
        }
        private RequestResult Execute(UsbManager manager,string deviceName,string name,string[] jobs,string expected,string expectedVariant,CancellationToken token,bool live,IProgress<BavarianLiveFrame> progress,string csvPath,bool readDetails=false,bool readVin=false,bool readFa=false)
        {
            if(!TransportLock.Wait(0))throw new InvalidOperationException("Transport busy");
            try
            {
                token.ThrowIfCancellationRequested();
                var current=GetDevice(manager);
                if(current==null||current.DeviceName!=deviceName||!Ready(manager,current))throw new IOException("Adapter changed");
                bool groupMode=Groups.TryGetValue(name,out string groupHash);
                if(!groupMode)groupMode=BavarianScanCatalog.TryGroupHash(name,out groupHash);
                VerifyFile(name,groupMode?groupHash:Approved[name]);
                if(groupMode)VerifyFile("t_grtb.prg","7b564434408d5256606d26b0f57b50739b7e029ef3cef967748a08eb05717791");
                using var ediabas=new EdiabasNet();
                ediabas.SetConfigProperty("EcuPath",Root);
                if(!groupMode)ediabas.SgbdFileName=name;
                ediabas.AbortJobFunc=()=>token.IsCancellationRequested;
                var transport=new EdInterfaceObd();
                ediabas.EdInterfaceClass=transport;
                BavarianConnection.Configure(this,manager,current,transport);
                using var registration=token.Register(()=>transport.TransmitCancel(true));
                string variant=name;
                if(groupMode)
                {
                    var timer=Stopwatch.StartNew();
                    try
                    {
                        ediabas.ResolveSgbdFile(name);
                        token.ThrowIfCancellationRequested();
                        variant=ediabas.SgbdFileName?.ToLowerInvariant()??"";
                        if(!VariantMatches(name,variant))throw new IOException("Detected variant is not approved: "+variant);
                        string variantHash=BavarianScanCatalog.IsExtended(name)&&BavarianScanCatalog.TryVariantHash(name,variant,out string scanHash)?scanHash:Approved[variant];
                        VerifyFile(variant,variantHash);
                        BavarianConnection.Log(this,"ECU resolved: "+variant);Log("RESOLVE "+name+" -> "+variant+" "+timer.ElapsedMilliseconds+" ms");
                    }
                    catch(Exception ex){BavarianConnection.Log(this,"Resolve failed: "+ex.GetType().Name);Log("RESOLVE FAIL "+name+" "+timer.ElapsedMilliseconds+" ms "+Describe(ex));throw;}
                }
                if(expectedVariant!=null&&expectedVariant!=variant)throw new IOException("ECU variant changed; reconnect.");
                var lines=new List<string>{"ECU: "+variant};
                var result=new RequestResult{Variant=variant};
                using var csv=live?new StreamWriter(csvPath,false,new UTF8Encoding(false)):null;
                if(csv!=null){csv.WriteLine(BavarianLiveFrame.CsvHeader);csv.Flush();}
                var liveTimer=Stopwatch.StartNew();
                string[] cycleJobs=expected!=null?new[]{"IDENT"}.Concat(jobs).ToArray():jobs;
                bool firstCycle=true;
                do
                {
                var frame=new BavarianLiveFrame(false,variant);
                foreach(string job in firstCycle?cycleJobs:jobs)
                {
                    token.ThrowIfCancellationRequested();
                    bool allowedExtra=ExtraFor(variant,job);
                    bool clear=!current.Bluetooth&&job=="CLEAR_CONFIRMED"&&expected!=null&&expectedVariant==variant&&((name=="d_motor.grp"&&variant=="d50m57e0.prg")||(name=="d_egs.grp"&&variant=="gs19a.prg"))&&!live;
                    if(job!="IDENT"&&job!="FS_LESEN"&&!EngineJobs.Contains(job)&&!GearboxJobs.Contains(job)&&!allowedExtra&&!clear&&!(variant=="gs19a.prg"&&!live&&expected!=null&&AdaptationJobs.Contains(job)))throw new InvalidOperationException("Job blocked");
                    var sw=Stopwatch.StartNew();
                    var jobLines=new List<string>();
                    try
                    {
                    ediabas.ArgString=clear&&variant=="d50m57e0.prg"?"0xFFFF":string.Empty;
                    ediabas.ArgBinaryStd=null;
                    ediabas.ResultsRequests=string.Empty;
                    ediabas.ExecuteJob(clear?"FS_LOESCHEN":job);
                    var rows=ediabas.ResultSets;
                    if(rows==null||rows.Count<2)throw new IOException("No ECU results");
                    var data=rows.Skip(1).ToArray();
                    var statuses=data.Where(r=>r.ContainsKey("JOB_STATUS")).Select(r=>Convert.ToString(r["JOB_STATUS"].OpData,CultureInfo.InvariantCulture)).ToArray();
                    if(((clear||AdaptationJobs.Contains(job))&&statuses.Length==0)||statuses.Any(s=>!string.Equals(s,"OKAY",StringComparison.OrdinalIgnoreCase)))throw new IOException("ECU reported unsuccessful job");
                    if(job=="IDENT"&&!data.Any(r=>r.Keys.Any(k=>k!="JOB_STATUS")))throw new IOException("No identification data");
                    jobLines.Add(job);
                    foreach(var row in data)
                        foreach(var kv in row)
                        {
                            if(kv.Key=="JOB_STATUS")continue;
                            string value=kv.Value.OpData is byte[] bytes?Convert.ToHexString(bytes):Convert.ToString(kv.Value.OpData,CultureInfo.InvariantCulture);
                            jobLines.Add(kv.Key+": "+value);
                        }
                    if(job=="IDENT")
                    {
                        result.Identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",jobLines.Skip(1).OrderBy(x=>x,StringComparer.Ordinal)))));
                        if(expected!=null&&expected!=result.Identity)throw new IOException("ECU identification changed; reconnect.");
                    }
                    token.ThrowIfCancellationRequested();
                    if(!live&&variant=="d50m57e0.prg"&&job=="STATUS_MOTORDREHZAHL")
                    {
                        var values=data.Where(r=>r.ContainsKey("STAT_MOTORDREHZAHL_WERT")&&r.ContainsKey("STAT_MOTORDREHZAHL_EINH")).ToArray();
                        if(values.Length==1&&Convert.ToString(values[0]["STAT_MOTORDREHZAHL_EINH"].OpData,CultureInfo.InvariantCulture)=="rpm"&&double.TryParse(Convert.ToString(values[0]["STAT_MOTORDREHZAHL_WERT"].OpData,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out double rpm)&&double.IsFinite(rpm)&&rpm>=0)result.EngineRpm=rpm;
                    }
                    string report=string.Join("\n",jobLines);
                    if(live&&job!="IDENT")
                    {
                        var numericRows=data.Select(row=>row.ToDictionary(k=>k.Key,k=>Convert.ToString(k.Value.OpData,CultureInfo.InvariantCulture))).ToList();
                        var samples=BavarianLiveFrame.ParseSamples(job,variant,numericRows,sw.ElapsedMilliseconds,allowedExtra);
                        foreach(var sample in samples)
                        {
                            frame.Samples.Add(sample);csv.WriteLine(sample.ToCsv(false,variant));
                        }
                        csv.Flush();
                        if(firstCycle&&allowedExtra)Log("EXPERIMENTAL "+job+"\n"+report);
                    }
                    else
                    {
                        if(job=="FS_LESEN")
                            result.FaultRows=data.Select(row=>row.ToDictionary(k=>k.Key,k=>k.Value.OpData is byte[] bytes?Convert.ToHexString(bytes):Convert.ToString(k.Value.OpData,CultureInfo.InvariantCulture))).ToList();
                        if(job=="FS_LESEN"&&AutoMode)lines.Clear();
                        if(job=="FS_LESEN"&&AutoMode)
                            lines.Add(BavarianLiveFrame.FormatFaults(data.Select(row=>row.ToDictionary(k=>k.Key,k=>Convert.ToString(k.Value.OpData,CultureInfo.InvariantCulture))),_ru));
                        else if(!AutoMode||job=="IDENT"||!EngineJobs.Contains(job)&&!GearboxJobs.Contains(job))
                            lines.Add(report+"\nResponse: "+sw.ElapsedMilliseconds+" ms\n");
                        else
                            lines.Add(BavarianLiveFrame.FormatStatus(job,data.Select(row=>row.ToDictionary(k=>k.Key,k=>Convert.ToString(k.Value.OpData,CultureInfo.InvariantCulture))),_ru));
                        Log("OK "+job+" "+sw.ElapsedMilliseconds+" ms\n"+report);
                    }
                    result.Ok++;
                    }
                    catch(Exception ex)
                    {
                        Log("FAIL "+job+" "+sw.ElapsedMilliseconds+" ms "+Describe(ex));
                        if(live||job=="IDENT"||clear||jobs.Length<=2||token.IsCancellationRequested)throw;
                        // Preserve successful readings; stop on transport errors.
                        if(ex is EdiabasNet.EdiabasNetException || ex is IOException)throw;
                        result.Failed++;
                        lines.Add(job+": "+Describe(ex));
                    }
                }
                if(live)
                {
                    frame.ElapsedSeconds=liveTimer.Elapsed.TotalSeconds;
                    progress?.Report(frame);
                    if(firstCycle)Log("LIVE first cycle OK "+variant);
                    firstCycle=false;
                    if(token.WaitHandle.WaitOne(200))break;
                }
                }while(live&&!token.IsCancellationRequested);
                if(!live&&(result.FaultRows!=null||readVin||readFa))
                {
                    try
                    {
                        List<Dictionary<string,string>> ReadAdditional(string job,string arg)
                        {
                            token.ThrowIfCancellationRequested();
                            ediabas.ArgString=arg;ediabas.ArgBinaryStd=null;ediabas.ResultsRequests=string.Empty;
                            ediabas.ExecuteJob(job);
                            token.ThrowIfCancellationRequested();
                            var r=ediabas.ResultSets;
                            if(r==null||r.Count<2)throw new IOException("No additional ECU results");
                            var rows=r.Skip(1).Select(row=>row.ToDictionary(k=>k.Key,k=>k.Value.OpData is byte[] bytes?Convert.ToHexString(bytes):Convert.ToString(k.Value.OpData,CultureInfo.InvariantCulture))).ToList();
                            if(rows.Any(row=>row.TryGetValue("JOB_STATUS",out string status)&&!string.Equals(status,"OKAY",StringComparison.OrdinalIgnoreCase)))throw new IOException("Additional job unsuccessful");
                            return rows;
                        }
                        if(readVin&&name=="d_cas.grp"&&variant=="cas.prg")
                        {
                            var vins=ReadAdditional("STATUS_FAHRGESTELLNUMMER","").Where(row=>row.ContainsKey("FGNUMMER")).Select(row=>row["FGNUMMER"]?.Trim()).Distinct().ToArray();
                            if(vins.Length!=1||vins[0]==null||!System.Text.RegularExpressions.Regex.IsMatch(vins[0],@"^[A-HJ-NPR-Z0-9]{17}$"))throw new IOException("VIN absent or invalid");
                            result.Vin=vins[0];Log("OK CAS VIN (not logged)");
                        }
                        if(readFa&&name=="d_cas.grp"&&variant=="cas.prg"&&result.Vin!=null)
                        {
                            var fa=ReadAdditional("C_FA_LESEN","");
                            var status=fa.Where(r=>r.ContainsKey("JOB_STATUS")).Select(r=>r["JOB_STATUS"]).ToArray();
                            var order=fa.Where(r=>r.ContainsKey("FAHRZEUGAUFTRAG")).Select(r=>r["FAHRZEUGAUFTRAG"]).Distinct().ToArray();
                            if(status.Length==0||status.Any(x=>x!="OKAY")||!fa.Any(r=>r.TryGetValue("SPEICHER_STATUS",out var occupied)&&occupied=="BELEGT")||order.Length!=1||string.IsNullOrWhiteSpace(order[0]))throw new IOException("FA unavailable or not occupied");
                            result.VehicleOrder=order[0];
                        }
                        if(readDetails&&result.FaultRows!=null&&(variant=="d50m57e0.prg"||variant=="gs19a.prg"))
                        {
                            var codes=BavarianFaultPanel.ParseRecords(result.FaultRows).Where(r=>r.TryGetValue("F_VORHANDEN_NR",out string state)&&(state=="33"||state=="35"))
                                .Select(r=>r.TryGetValue("F_ORT_NR",out string raw)&&int.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out int code)&&code>0&&code<=65535?code:0)
                                .Where(code=>code>0).Distinct().ToArray();
                            foreach(int code in codes.Take(6))
                            {
                                var detailRows=ReadAdditional("FS_LESEN_DETAIL",code.ToString(CultureInfo.InvariantCulture));
                                var match=BavarianFaultPanel.ParseRecords(detailRows).Where(r=>r.TryGetValue("F_ORT_NR",out string value)&&int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)&&n==code).ToList();
                                if(match.Count!=1)throw new IOException("Detail response did not match requested code "+code.ToString("X4"));
                                result.ExtraDetails[code]=match;
                                Log("OK FS_LESEN_DETAIL "+code.ToString("X4")+"\n"+string.Join("\n",match[0].Select(k=>k.Key+": "+k.Value)));
                            }
                            if(codes.Length>6)result.ExtraNote="Details limited to first six confirmed records.";
                        }
                    }
                    catch(Exception ex)
                    {
                        result.ExtraNote="Additional read incomplete: "+Describe(ex);
                        Log("EXTRA READ FAIL "+Describe(ex));
                    }
                }
                if(live)Log("LIVE ended "+variant+" jobs="+result.Ok);
                result.Text=live?"Recorded jobs: "+result.Ok:string.Join("\n",lines);
                return result;
            }
            finally{TransportLock.Release();}
        }
    }
}
#endif
