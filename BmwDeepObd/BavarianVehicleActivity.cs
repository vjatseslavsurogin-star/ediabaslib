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
    public class BavarianVehicleActivity : AppCompatActivity
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
        private bool _faultScreen, _dashboard;
        private Button _chooseSignals;
        private TextView _selectedSummary;
        private string[] _selectedSignals;
        private string[] AvailableSignals=>_group=="d_egs.grp"?GearboxJobs:EngineJobs;
        private Button _scanAll;
        private BavarianFaultPanel _faultPanel;
        private TextView _detected;
        private bool AutoMode => _group != null;
        private static bool VariantMatches(string group,string name) =>
            Approved.ContainsKey(name) && (group=="d_motor.grp" ? name.StartsWith("d",StringComparison.Ordinal) :
            group=="d_egs.grp" && name.StartsWith("gs19",StringComparison.Ordinal));
        private static readonly string[] EngineJobs =
        {
            "STATUS_MOTORDREHZAHL","STATUS_KUEHLMITTELTEMPERATUR","STATUS_LADEDRUCK_IST",
            "STATUS_LADEDRUCK_SOLL","STATUS_RAILDRUCK_IST","STATUS_RAILDRUCK_SOLL","STATUS_UBATT"
        };
        private static readonly string[] GearboxJobs = { "STATUS_GETRIEBETEMPERATUR", "STATUS_ISTGANG" };
        private static readonly string[] EngineExtra = { "STATUS_LUFTMASSE_IST", "STATUS_LUFTMASSE_SOLL", "STATUS_LAUFUNRUHE_LLR_MENGE", "STATUS_ATMOSPHAERENDRUCK", "STATUS_ANSAUGLUFTTEMPERATUR" };
        private static readonly string[] GearboxExtra = { "STATUS_WH_POSITION", "STATUS_GETRIEBEPOSITION", "STATUS_TURBINENDREHZAHL", "STATUS_MOTORDREHZAHL", "STATUS_WK" };
        private Button _live, _stopLive, _extra, _csv;
        private BavarianLivePanel _panel;
        private string _csvExport;
        private bool _liveRunning;
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
            base.OnCreate(savedInstanceState);
            _ru=BavarianLanguage.IsRussian(this);
            string requested=Intent?.GetStringExtra("bavarian_mode");
            _faultScreen=requested=="faults";
            _dashboard=requested=="live_engine"||requested=="live_egs";
            _group=requested=="engine"||requested=="live_engine"?"d_motor.grp":requested=="transmission"||requested=="live_egs"?"d_egs.grp":null;
            if(_dashboard)
            {
                string saved=GetSharedPreferences("bavarian_dashboard",FileCreationMode.Private)?.GetString(_group,"");
                _selectedSignals=AvailableSignals.Where(j=>(saved??"").Split(',').Contains(j)).ToArray();
                if(_selectedSignals.Length==0)_selectedSignals=AvailableSignals.ToArray();
            }
            SupportActionBar?.Hide();
            var scroll=new ScrollView(this){FillViewport=true};
            scroll.SetFitsSystemWindows(true);
            _body=new LinearLayout(this){Orientation=Orientation.Vertical};
            _body.SetPadding(Dp(20),Dp(20),Dp(20),Dp(20));
            scroll.AddView(_body);
            SetContentView(scroll);
            if(_faultScreen)
            {
                AddText(T("Fault codes · DDE / EGS","Ошибки · DDE / EGS"),24);
                AddText(T("Read-only scan of engine and transmission. Park safely, ignition on.","Чтение двигателя и АКПП без записи. Автомобиль должен стоять, зажигание включено."),15);
                _state=AddText("",15);
                _permission=AddButton(T("Check USB / grant permission","Проверить USB / разрешение"),(s,e)=>RequestUsb());
                _scanAll=AddButton(T("Scan DDE + EGS","Прочитать DDE + EGS"),async(s,e)=>await ScanFaultsAsync());
                AddButton(T("Cancel scan","Остановить сканирование"),(s,e)=>_cancel?.Cancel());
                _output=AddText("",14);
                _faultPanel=new BavarianFaultPanel(this,_ru);
                _body.AddView(_faultPanel);
                AddButton(T("Share developer report","Поделиться инженерным отчётом"),(s,e)=>ShareReport());
                _poll=new Java.Lang.Runnable(()=>{if(!_visible)return;Refresh();_handler.PostDelayed(_poll,1000);});
                Refresh();
                return;
            }
            AddText(_dashboard?(_group=="d_motor.grp"?"Live Data · DDE":"Live Data · EGS"):AutoMode ? (_group=="d_motor.grp"?T("Engine · DDE","Двигатель · DDE"):T("Transmission · EGS","АКПП · EGS")) : T("Developer mode · USB","Инженерный режим · USB"),24);
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
            if(!AutoMode)_body.AddView(_confirm);
            _confirm.CheckedChange+=(s,e)=>Refresh();
            _faults=AddButton(T("Read fault memory","Чтение ошибок"),async(s,e)=>await RunAsync("FS_LESEN"));
            _snapshot=AddButton(T("Read parameters once","Прочитать параметры"),async(s,e)=>await RunAsync("SNAPSHOT"));
            if(_dashboard){_faults.Visibility=ViewStates.Gone;_snapshot.Visibility=ViewStates.Gone;}
            if(_dashboard)
            {
                _chooseSignals=AddButton(T("Edit sensor selection","Выбрать параметры"),(s,e)=>ChooseSignals());
                _selectedSummary=AddText("",14);UpdateSelectionLabel();
            }
            _live=AddButton(T("Start live data + CSV","Начать Live Data + CSV"),async(s,e)=>await RunAsync("LIVE"));
            _stopLive=AddButton(T("Stop live data","Остановить Live Data"),(s,e)=>_cancel?.Cancel());
            _extra=AddButton(T("Extra signals · experimental","Дополнительные сигналы · эксперимент"),async(s,e)=>await RunAsync("EXTRA"));
            if(_dashboard)_extra.Visibility=ViewStates.Gone;
            _csv=AddButton(T("Export latest CSV","Сохранить последний CSV"),(s,e)=>ExportCsv());
            _panel=new BavarianLivePanel(this,_group=="d_egs.grp");
            _panel.Visibility=_dashboard?ViewStates.Visible:ViewStates.Gone;
            if(_dashboard)_panel.ShowSelection(_selectedSignals);
            _body.AddView(_panel);
            AddButton(T("Cancel / disconnect","Отмена / отключение"),(s,e)=>{ResetIdentity();_cancel?.Cancel();Refresh();});
            _stats=AddText("",13);
            AddButton(T("Share developer report","Поделиться инженерным отчётом"),(s,e)=>ShareReport());
            AddButton(T("Clear local test report","Очистить локальный отчёт"),(s,e)=>{
                if(_busy)return;
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetMessage("Delete local diagnostic report?").SetPositiveButton("Delete",(d,a)=>{try{File.Delete(LogPath);_ok=_failed=0;Refresh();}catch(Exception ex){_output.Text=BavarianLanguage.Error("Report deletion failed",ex);}}).SetNegativeButton("Cancel",(d,a)=>{}).Show();
            });
            _output=AddText("",14);
            _poll=new Java.Lang.Runnable(()=>{if(!_visible)return;Refresh();_handler.PostDelayed(_poll,1000);});
            if(!AutoMode)_variant.ItemSelected+=(s,e)=>{ResetIdentity();Refresh();};
            Refresh();
        }

        protected override void OnResume(){base.OnResume();_visible=true;if(_state!=null)Refresh();if(_poll!=null)_handler.PostDelayed(_poll,1000);}
        protected override void OnStop(){_visible=false;if(_poll!=null)_handler.RemoveCallbacks(_poll);_cancel?.Cancel();ResetIdentity();base.OnStop();}
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
                        send.PutExtra(Intent.ExtraSubject,"Bavarian V1.3 diagnostic report");
                        send.PutExtra(Intent.ExtraText,report);
                        StartActivity(Intent.CreateChooser(send,"Share report"));
                    }).SetNegativeButton("Cancel",(d,a)=>{}).Show();
            }
            catch(Exception ex){_output.Text=BavarianLanguage.Error("Report sharing failed",ex);}
        }
        private TextView AddText(string text,int size)
        {
            var v=new TextView(this){Text=text,TextSize=size};
            v.SetTextIsSelectable(true);
            var lp=new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(12)};
            _body.AddView(v,lp);return v;
        }
        private Button AddButton(string label,EventHandler click)
        {
            var b=new Button(this){Text=label};b.Click+=click;_body.AddView(b);return b;
        }
        private UsbManager Manager=>GetSystemService(UsbService) as UsbManager;
        private UsbDevice GetDevice(UsbManager manager)
        {
            if(manager==null)return null;
            var drivers=EdFtdiInterface.GetDriverList(manager);
            return drivers.Count==1 ? drivers[0].Device : null;
        }
        private string Selected=>_group??_variant?.SelectedItem?.ToString();
        private void Refresh()
        {
            bool permitted=false;
            try
            {
                var manager=Manager; var device=GetDevice(manager);
                permitted=device!=null && manager.HasPermission(device);
                if(device==null || device.DeviceName!=_deviceName || !permitted){ResetIdentity();}
                if(_busy&&(!permitted||device?.DeviceName!=_activeDevice))_cancel?.Cancel();
                _state.Text=_busy?"Diagnostic request running...":device==null?"Connect exactly one supported USB adapter.":!permitted?"USB detected. Permission required.":_identified==null?"USB permission granted. ECU not identified.":(AutoMode?"ECU identified. Read requests enabled.":"ECU responded to IDENT. Confirm variant before interpreting results.");
            }
            catch(Exception){_state.Text="USB check failed.";ResetIdentity();_cancel?.Cancel();}
            _permission.Enabled=!_busy;
            if(_faultScreen)
            {
                _scanAll.Enabled=!_busy&&permitted;
                if(!_busy)_state.Text=permitted?T("USB ready. Start scan.","USB готов. Начните чтение."):T("Connect one supported USB adapter and grant permission.","Подключите один USB-адаптер и выдайте разрешение.");
                return;
            }
            _variant.Enabled=!_busy;
            _ident.Enabled=!_busy&&permitted&&Selected!=null;
            bool identified=permitted&&Selected!=null&&Selected==_identified;
            _confirm.Enabled=!_busy&&identified;
            bool ready=!_busy&&identified&&(AutoMode||_confirm.Checked);
            _faults.Enabled=ready;_snapshot.Enabled=ready;
            bool verifiedVariant=_resolved=="d50m57e0.prg"||_resolved=="gs19a.prg";
            _live.Enabled=ready&&AutoMode&&verifiedVariant;
            _extra.Enabled=ready&&AutoMode&&verifiedVariant;
            _stopLive.Enabled=_busy&&_liveRunning;
            if(_chooseSignals!=null)_chooseSignals.Enabled=!_busy;
            _csv.Enabled=!_busy&&AutoMode&&LatestCsv()!=null;
            _stats.Text="Jobs: "+_ok+" OK / "+_failed+" failed (this screen)";
        }
        private void UpdateSelectionLabel()
        {
            _selectedSummary.Text=_selectedSignals.Length+" / "+AvailableSignals.Length+T(" sensors selected"," параметров выбрано");
        }
        private void ChooseSignals()
        {
            if(_busy||!_dashboard)return;
            var options=new LinearLayout(this){Orientation=Orientation.Vertical};
            options.SetPadding(Dp(16),Dp(8),Dp(16),Dp(8));
            var checks=AvailableSignals.Select(j=>new CheckBox(this){Text=BavarianLiveFrame.Label(j,_ru),Checked=_selectedSignals.Contains(j)}).ToArray();
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
        private void RequestUsb()
        {
            try
            {
                var manager=Manager;var device=GetDevice(manager);
                if(device==null){Refresh();return;}
                if(!manager.HasPermission(device))
                {
                    var intent=new Intent(this,typeof(BavarianUsbPermissionReceiver));
                    intent.SetAction(ActivityCommon.AppNameSpace+".BAVARIAN_USB");
                    var flags=OperatingSystem.IsAndroidVersionAtLeast(31)?PendingIntentFlags.Mutable:0;
                    var pending=PendingIntent.GetBroadcast(this,510,intent,flags);
                    manager.RequestPermission(device,pending);
                    _output.Text="Grant USB permission, then press Check USB again.";
                }
                Refresh();
            }
            catch(Exception ex){_output.Text=BavarianLanguage.Error("USB permission request failed",ex);}
        }
        private async Task ScanFaultsAsync()
        {
            if(_busy)return;
            UsbManager manager;
            UsbDevice device;
            try{manager=Manager;device=GetDevice(manager);}
            catch(Exception ex){_output.Text=Describe(ex);return;}
            if(device==null||!manager.HasPermission(device)){Refresh();return;}
            _faultPanel.BeginScan();
            _busy=true;_activeDevice=device.DeviceName;_cancel=new CancellationTokenSource();
            _cancel.CancelAfter(TimeSpan.FromSeconds(60));
            var token=_cancel.Token;
            Refresh();_output.Text=T("Reading both modules...","Чтение обоих блоков...");
            try
            {
                foreach(string group in new[]{"d_motor.grp","d_egs.grp"})
                {
                    if(token.IsCancellationRequested)break;
                    string module=group=="d_motor.grp"?"DDE":"EGS";
                    try
                    {
                        Log("START FAULT_SCAN "+group);
                        var result=await Task.Run(()=>Execute(manager,device.DeviceName,group,new[]{"IDENT","FS_LESEN"},null,null,token,false,null,null));
                        token.ThrowIfCancellationRequested();
                        if(IsFinishing||IsDestroyed||!_visible)break;
                        _faultPanel.AddResult(module,result.Variant,result.FaultRows);
                    }
                    catch(Exception ex)
                    {
                        Log("FAULT_SCAN FAIL "+group+" "+Describe(ex));
                        if(IsFinishing||IsDestroyed||!_visible)break;
                        _faultPanel.AddFailure(module,token.IsCancellationRequested?T("Cancelled / timed out","Отменено / тайм-аут"):Describe(ex));
                    }
                }
                if(!IsFinishing&&!IsDestroyed&&_visible)
                    _output.Text=token.IsCancellationRequested?T("Scan incomplete / cancelled.","Сканирование не завершено / отменено."):T("Scan finished. See results by module below.","Чтение завершено. Результаты по блокам ниже.");
            }
            finally
            {
                _cancel.Dispose();_cancel=null;_busy=false;_activeDevice=null;
                if(!IsFinishing&&!IsDestroyed)Refresh();
            }
        }
        private async Task RunAsync(string operation)
        {
            if(_busy)return;
            string name=Selected;
            if(name==null || !(AutoMode?Groups.ContainsKey(name):Approved.ContainsKey(name)))return;
            UsbManager manager; UsbDevice device;
            try { manager=Manager; device=GetDevice(manager); }
            catch(Exception ex) { _output.Text=BavarianLanguage.Error("USB check failed",ex); return; }
            if(device==null || !manager.HasPermission(device)){Refresh();return;}
            if(operation!="IDENT" && (_identified!=name || _deviceName!=device.DeviceName || (!AutoMode&&!_confirm.Checked) || _fingerprint==null || _resolved==null))return;
            if(operation=="IDENT")ResetIdentity();
            string expected=_fingerprint;
            string expectedVariant=_resolved;
            bool live=operation=="LIVE", extra=operation=="EXTRA";
            if((live||extra)&&(!AutoMode||(expectedVariant!="d50m57e0.prg"&&expectedVariant!="gs19a.prg")))return;
            _liveRunning=live;
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
                _panel.ResetHistory();
                _panel.ShowSelection(_dashboard?_selectedSignals:AvailableSignals);
                _panel.Visibility=ViewStates.Visible;
                progress=new Progress<BavarianLiveFrame>(frame=>{
                    if(!IsFinishing&&!IsDestroyed&&_visible&&_liveRunning)_panel.UpdateFrame(frame);
                });
            }
            else if(!_dashboard)_panel.Visibility=ViewStates.Gone;
            _activeDevice=device.DeviceName;
            Log("START "+operation+" SGBD="+name+" USB="+device.VendorId.ToString("X4")+":"+device.ProductId.ToString("X4"));
            _busy=true;_cancel=new CancellationTokenSource();
            _cancel.CancelAfter(live?TimeSpan.FromMinutes(10):TimeSpan.FromSeconds(45));
            var token=_cancel.Token;
            Refresh();_output.Text="Waiting for ECU response...";
            try
            {
                bool gearbox=name=="d_egs.grp"||name.StartsWith("gs19",StringComparison.Ordinal);
                string[] jobs=live&&_dashboard?_selectedSignals.ToArray():live||operation=="SNAPSHOT"?(gearbox?GearboxJobs:EngineJobs):extra?(gearbox?GearboxExtra:EngineExtra):new[]{operation};
                var result=await Task.Run(()=>Execute(manager,device.DeviceName,name,jobs,expected,expectedVariant,token,live,progress,csvPath));
                if(IsFinishing||IsDestroyed)return;
                if(token.IsCancellationRequested&&!live)return;
                _output.Text=live?T("Live session ended. Export CSV to save the readings.","Сессия завершена. Сохрани показания через CSV.")+"\n"+result.Text:result.Text;
                _ok+=result.Ok;_failed+=result.Failed;
                if(operation=="IDENT")
                {
                    _identified=name;_deviceName=device.DeviceName;_fingerprint=result.Identity;_resolved=result.Variant;
                    _detected.Text=T("Detected ECU: ","Определённый блок: ")+result.Variant;
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
        private RequestResult Execute(UsbManager manager,string deviceName,string name,string[] jobs,string expected,string expectedVariant,CancellationToken token,bool live,IProgress<BavarianLiveFrame> progress,string csvPath)
        {
            if(!TransportLock.Wait(0))throw new InvalidOperationException("Transport busy");
            try
            {
                token.ThrowIfCancellationRequested();
                var current=GetDevice(manager);
                if(current==null||current.DeviceName!=deviceName||!manager.HasPermission(current))throw new IOException("USB changed");
                bool groupMode=Groups.TryGetValue(name,out string groupHash);
                VerifyFile(name,groupMode?groupHash:Approved[name]);
                using var ediabas=new EdiabasNet();
                ediabas.SetConfigProperty("EcuPath",Root);
                if(!groupMode)ediabas.SgbdFileName=name;
                ediabas.AbortJobFunc=()=>token.IsCancellationRequested;
                var transport=new EdInterfaceObd();
                ediabas.EdInterfaceClass=transport;
                transport.ComPort=EdFtdiInterface.PortId+"0";
                transport.ConnectParameter=new EdFtdiInterface.ConnectParameterType(manager);
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
                        VerifyFile(variant,Approved[variant]);
                        Log("RESOLVE "+name+" -> "+variant+" "+timer.ElapsedMilliseconds+" ms");
                    }
                    catch(Exception ex){Log("RESOLVE FAIL "+name+" "+timer.ElapsedMilliseconds+" ms "+Describe(ex));throw;}
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
                    bool allowedExtra=!live&&((variant=="d50m57e0.prg"&&EngineExtra.Contains(job))||(variant=="gs19a.prg"&&GearboxExtra.Contains(job)));
                    if(job!="IDENT"&&job!="FS_LESEN"&&!EngineJobs.Contains(job)&&!GearboxJobs.Contains(job)&&!allowedExtra)throw new InvalidOperationException("Job blocked");
                    var sw=Stopwatch.StartNew();
                    var jobLines=new List<string>();
                    try
                    {
                    ediabas.ArgString=string.Empty;
                    ediabas.ArgBinaryStd=null;
                    ediabas.ResultsRequests=string.Empty;
                    ediabas.ExecuteJob(job);
                    var rows=ediabas.ResultSets;
                    if(rows==null||rows.Count<2)throw new IOException("No ECU results");
                    var data=rows.Skip(1).ToArray();
                    var statuses=data.Where(r=>r.ContainsKey("JOB_STATUS")).Select(r=>Convert.ToString(r["JOB_STATUS"].OpData,CultureInfo.InvariantCulture)).ToArray();
                    if(statuses.Any(s=>!string.Equals(s,"OKAY",StringComparison.OrdinalIgnoreCase)))throw new IOException("ECU reported unsuccessful job");
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
                    string report=string.Join("\n",jobLines);
                    if(live&&job!="IDENT")
                    {
                        string key="STAT_"+job.Substring(7);
                        var values=data.Where(r=>r.ContainsKey(key+"_WERT")).ToArray();
                        if(values.Length!=1)throw new IOException("Expected one numeric value for "+job);
                        var row=values[0];
                        string val=Convert.ToString(row[key+"_WERT"].OpData,CultureInfo.InvariantCulture);
                        string unit=row.ContainsKey(key+"_EINH")?Convert.ToString(row[key+"_EINH"].OpData,CultureInfo.InvariantCulture):"";
                        if(!double.TryParse(val,NumberStyles.Float,CultureInfo.InvariantCulture,out double numeric)||!double.IsFinite(numeric)||!BavarianLiveFrame.UnitMatches(job,unit))
                            throw new IOException("Unexpected numeric result or unit for "+job+": "+unit);
                        var sample=new BavarianSample(job,numeric,unit,DateTimeOffset.UtcNow,sw.ElapsedMilliseconds);
                        frame.Samples.Add(sample);csv.WriteLine(sample.ToCsv(false,variant));csv.Flush();
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
                        if(live||job=="IDENT"||jobs.Length<=2||token.IsCancellationRequested)throw;
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
                if(live)Log("LIVE ended "+variant+" jobs="+result.Ok);
                result.Text=live?"Recorded jobs: "+result.Ok:string.Join("\n",lines);
                return result;
            }
            finally{TransportLock.Release();}
        }
    }
}
#endif
